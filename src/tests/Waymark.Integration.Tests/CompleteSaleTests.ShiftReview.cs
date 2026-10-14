using Microsoft.EntityFrameworkCore;
using Waymark.Application.Commands;
using Waymark.Application.IdGenerator;
using Waymark.Application.Organisation;
using Waymark.Application.Sales;
using Waymark.Application.Statistics;
using RefusalCodes = Waymark.Contracts.Pos.RefusalCodes;
using Waymark.Domain;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Values;
using Waymark.Persistence;
using Waymark.Persistence.Catalogue;
using Waymark.Persistence.Organisation;
using Waymark.Persistence.Privacy;
using Waymark.Persistence.Sales;
using Waymark.Persistence.Sync;

namespace Waymark.Integration.Tests;

/// <summary>
/// The block C review (12/10): what its extreme cases found, kept as tests. The silent failures: a
/// session left open for weeks that can no longer be read, paid out of or closed; a refund in a later
/// session rewriting what an earlier Z says; a ticket worth nothing, struck empty or rounded to no
/// cash putting a report out of balance; people ordered by anything but the hour, which the other
/// tests cannot see because their clock never moves.
/// </summary>
public sealed partial class CompleteSaleTests
{
    /// <summary>A clock at one instant, for the tests where time has to move between two operations.</summary>
    private sealed class ClockAt(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    private async Task<CompletedSale> SellAt(Shop shop, DateTimeOffset at, string staff, params SaleLineRequest[] lines)
    {
        await using var context = database.NewContext(storeId: shop.StoreId);
        var clock = new ClockAt(at);
        var calendar = new FixedCalendar();
        var ids = new UlidGenerator();
        var unitOfWork = new WaymarkUnitOfWork(context);
        var executor = new CommandExecutor(unitOfWork, ids, new ProcessingLogWriter(context, ids, new FixedCurrentStore(shop.StoreId), clock));
        var handler = new CompleteSaleHandler(
            new ProductLookup(context, calendar), new SalesLedger(context), unitOfWork, new OutboxSequence(context),
            new NullTier2Writer(), calendar, clock, new Waymark.Persistence.Reference.ReasonCodes(context), TabChargesFor.Context(context));
        return await executor.ExecuteAsync(handler, new CompleteSale(shop.TerminalId, staff, lines));
    }

    [Fact]
    public async Task A_session_of_more_tickets_than_sqlite_takes_variables_is_still_read_paid_out_of_and_closed()
    {
        // Nothing closes a session by itself (D-111). Read as a list of ids, its tickets became one SQL
        // variable each, and past 32 766 the X, a paid-out and the close itself all failed with "too
        // many SQL variables": a drawer nobody could ever close again.
        CashReasons();
        var shop = new Shop(database, drawerOpen: false);
        var opened = await OpenDrawer(shop, 100_000);
        using (var connection = database.Connect())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                $"""
                WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 33000)
                INSERT INTO transactions (transaction_id, store_id, terminal_id, cash_session_id, staff_id, invoice_number, occurred_at, status, created_at, updated_at)
                SELECT 'bulk-{shop.StaffId}-' || i, '{shop.StoreId}', '{shop.TerminalId}', '{opened.SessionId}', '{shop.StaffId}', 'B-{shop.StaffId}-' || i,
                       '2026-09-19 09:30:00', 'completed', '2026-09-19 09:30:00', '2026-09-19 09:30:00'
                FROM n
                """;
            Assert.Equal(33_000, command.ExecuteNonQuery());
        }

        var x = (await ReadX(shop, new ReportSettings()))!;
        Assert.Equal(33_000, x.Activity.Sold.Count);
        Assert.True(x.TakingsReconcile && x.CashiersReconcile);

        await Cash(shop, CashDirection.Out, 10_000, BankDeposit);
        var closed = await CloseWith(shop, 90_000, new ReportSettings());

        Assert.Equal(Dzd(0), closed.Variance);
        Assert.True((await ReadZ(shop, closed.ZReportNumber, new ReportSettings()))!.DrawerReconciles);
    }

    [Fact]
    public async Task A_refund_in_a_later_session_is_that_sessions_and_the_earlier_z_says_what_it_said()
    {
        Reasons();
        ReturnReasons();
        CashReasons();
        var shop = new Shop(database, drawerOpen: false);
        shop.Receive(database, shop.Milk, 20, daysAgo: 5);
        var settings = new ReportSettings();

        await OpenDrawer(shop, 500_000);
        var milk = await Sell(shop, (shop.Milk, 1));
        var first = await CloseWith(shop, (await ReadX(shop, settings))!.Expected.MinorUnits, settings);
        var before = (await ReadZ(shop, first.ZReportNumber, settings))!;

        // The next day: the float, then yesterday's milk comes back.
        await OpenDrawer(shop, 20_000);
        await Refund(shop, milk.TransactionId, [Back(shop.Milk, 1, 14_300)]);

        // The earlier Z is the sale's, to the centime, though its ticket now reads "refunded".
        var after = (await ReadZ(shop, first.ZReportNumber, settings))!;
        Assert.True(after.TakingsReconcile && after.DrawerReconciles && after.CashiersReconcile);
        Assert.Equal(
            (before.Takings.SalesTotal, before.Takings.Net, before.Activity, before.Expected),
            (after.Takings.SalesTotal, after.Takings.Net, after.Activity, after.Expected));
        Assert.Equal((new Tally(1, Dzd(14_300)), new Tally(0, Dzd(0))), (after.Activity.Sold, after.Activity.Refunded));

        // The refund is today's: nothing sold, 143,00 given back, 145,00 out of the drawer.
        var today = (await ReadX(shop, settings))!;
        Assert.True(today.TakingsReconcile && today.DrawerReconciles && today.CashiersReconcile);
        Assert.Equal((new Tally(0, Dzd(0)), new Tally(1, Dzd(14_300))), (today.Activity.Sold, today.Activity.Refunded));
        Assert.Equal((Dzd(-14_300), Dzd(-200), Dzd(5_500)), (today.Takings.Net, today.TenderRounding, today.Expected));

        var second = await CloseWith(shop, 5_500, settings);
        Assert.Equal((first.ZReportNumber + 1, Dzd(0)), (second.ZReportNumber, second.Variance));
    }

    [Fact]
    public async Task Tickets_worth_nothing_struck_empty_or_rounded_to_no_cash_leave_the_report_in_balance()
    {
        Reasons();
        VoidReasons();
        var shop = new Shop(database, drawerOpen: false);
        shop.Receive(database, shop.Milk, 3, daysAgo: 9);   // two batches: a line of five is split over them
        shop.Receive(database, shop.Milk, 30, daysAgo: 5);
        shop.Receive(database, shop.Bread, 30, daysAgo: 5);
        await OpenDrawer(shop, 0);

        var free = await Sell(shop, Percent(10_000, shop.StaffId), new SaleLineRequest(shop.Milk.Barcode, 1));                              // 100 % off the ticket
        var given = await Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 1, Discount: Percent(10_000, shop.StaffId)));             // 100 % off the line
        var coins = await Sell(shop, null, new SaleLineRequest(shop.Bread.Barcode, 1, Discount: Amount(11_850, shop.StaffId)));             // 2,00: no cash at all
        var split = await Sell(shop, Percent(1_000, shop.StaffId), new SaleLineRequest(shop.Milk.Barcode, 5), new SaleLineRequest(shop.Bread.Barcode, 2));
        var empty = await Cancel(shop, lines: [], removed: [new RemovedLine(shop.Milk.Barcode, 1, null, Now)]);                             // struck empty (D-106)
        var cancelled = await Cancel(shop, lines: [new SaleLineRequest(shop.Bread.Barcode, 1)], removed: [new RemovedLine(shop.Milk.Barcode, 2, null, Now)]);
        var struck = await Execute(shop, new CompleteSale(
            shop.TerminalId, shop.StaffId, [new SaleLineRequest(shop.Bread.Barcode, 1)], Removed: [new RemovedLine(shop.Milk.Barcode, 1, null, Now)]));
        Assert.Equal((Dzd(0), Dzd(0), Dzd(200), Dzd(0)), (free.Total, given.Total, coins.Total, empty.Total));

        var x = (await ReadX(shop, new ReportSettings(voidCount: 1, voidValue: 0)))!;

        Assert.True(x.TakingsReconcile && x.DrawerReconciles && x.CashiersReconcile);

        // Five tickets sold, two of them for nothing and with no payment row; three paid in cash.
        var takings = coins.Total + split.Total + struck.Total;
        Assert.Equal(new Tally(5, takings), x.Activity.Sold);
        Assert.Equal([new TenderLine(PaymentMethod.Cash, 3, takings)], x.Takings.Sales);

        // Two cancels, one of them worth nothing; four milks struck over three lines, in no total.
        Assert.Equal(new Tally(2, cancelled.Total), x.Activity.Cancelled);
        Assert.Equal(new Tally(3, Dzd(4 * 14_300)), x.Activity.StruckLines);

        // What was given away: the two line discounts, and the two tickets' (143,00 and a tenth of 956,00).
        Assert.Equal(new Tally(2, Dzd(14_300 + 11_850)), x.Reductions.LineDiscounts);
        Assert.Equal(new Tally(2, Dzd(14_300 + 9_560)), x.Reductions.TicketDiscounts);

        // The drawer: the cash step took 2,00 off one ticket whole, and the lines still add up.
        Assert.Equal(3, x.RoundedTickets);
        Assert.Equal(x.Movements.CashSales + x.TenderRounding, x.Expected);
        Assert.Equal(0, x.Expected.MinorUnits % Currency.Dzd.CashRoundingStep);

        // A cancel worth nothing counts as a cancel and is above no value.
        Assert.Equal([ShiftFlagKind.TooManyCancels, ShiftFlagKind.CancelAboveValue], x.Flags.Select(flag => flag.Kind));
        Assert.Equal(2, x.Flags[0].Count);
    }

    [Fact]
    public async Task People_come_in_the_order_they_first_did_something_as_the_hours_pass_and_whoever_counts_is_seen_last()
    {
        // Every other test of the report runs at one instant; here the session lasts a day and a night.
        var shop = new Shop(database, drawerOpen: false);
        shop.Receive(database, shop.Milk, 30, daysAgo: 5);
        var samia = SecondCashier(shop, "Samia K.");
        var opening = new DateTimeOffset(2026, 10, 12, 7, 0, 0, TimeSpan.Zero);

        await Run(shop, (context, work) => new OpenCashSessionHandler(new SalesLedger(context), work, new ClockAt(opening)),
            new OpenCashSession(shop.TerminalId, shop.StaffId, 0));
        await SellAt(shop, opening.AddMinutes(10), samia, new SaleLineRequest(shop.Milk.Barcode, 1));
        await SellAt(shop, opening.AddMinutes(20), shop.StaffId, new SaleLineRequest(shop.Milk.Barcode, 2));
        await SellAt(shop, opening.AddHours(26), samia, new SaleLineRequest(shop.Milk.Barcode, 1)); // past midnight, the same session

        var x = (await ReadX(shop, new ReportSettings()))!;
        Assert.Equal(
            [(shop.StaffId, opening, opening.AddMinutes(20)), (samia, opening.AddMinutes(10), opening.AddHours(26))],
            x.Cashiers.Select(share => (share.StaffId, share.First, share.Last)));

        var closed = await Run(shop, (context, work) => new CloseCashSessionHandler(
                new SalesLedger(context), new CashSessionLedger(context), new ReportSettings(), new ClockAt(opening.AddHours(27))),
            new CloseCashSession(shop.TerminalId, samia, x.Expected.MinorUnits, null, true, null));
        var z = (await ReadZ(shop, closed.ZReportNumber, new ReportSettings()))!;

        Assert.Equal(opening.AddHours(27), z.Cashiers.Single(share => share.StaffId == samia).Last);
        Assert.True(z.CashiersReconcile);
    }

    // ------------------------------------------------------------------ D-119: the blind count the server keeps

    private Task<long> KeepBlind(Shop shop, long counted) =>
        Run(shop, (context, _) => new KeepBlindCountHandler(new SalesLedger(context), new CashSessionLedger(context), new FixedClock()),
            new KeepBlindCount(shop.TerminalId, counted));

    [Fact]
    public async Task A_blind_count_is_kept_and_another_figure_is_not_taken_until_cash_moves()
    {
        CashReasons();
        var shop = new Shop(database, drawerOpen: false);
        shop.Receive(database, shop.Milk, 20, daysAgo: 5);
        await OpenDrawer(shop, 4_835_000);

        // The first count is written; every other figure tried after it comes back as that one.
        Assert.Equal(4_000_000, await KeepBlind(shop, 4_000_000));
        Assert.Equal(4_000_000, await KeepBlind(shop, 4_815_000));
        Assert.Equal(4_000_000, await KeepBlind(shop, 4_835_000));
        Assert.Equal((Dzd(4_000_000), 0L), ((await Session(shop)).PendingCount, (await Session(shop)).PendingCountMoves));

        // A card sale moves no cash: the count still stands.
        await Paid(shop, [new GivenTender(PaymentMethod.Card, 14_300, "4417")], (shop.Milk, 1));
        Assert.Equal(4_000_000, await KeepBlind(shop, 4_835_000));

        // A cash sale does: the drawer no longer holds what was counted, and it is counted again.
        await Sell(shop, (shop.Milk, 1));
        Assert.Equal(4_849_500, await KeepBlind(shop, 4_849_500));
        Assert.Equal(4_849_500, await KeepBlind(shop, 1));

        // So does cash in or out with no sale.
        await Cash(shop, CashDirection.In, 10_000, Float);
        Assert.Equal(4_859_500, await KeepBlind(shop, 4_859_500));
    }

    [Fact]
    public async Task The_close_of_a_blind_count_is_asked_with_the_count_that_stands()
    {
        // What the host does for somebody who is not shown the figures: keep, then close with the answer.
        var shop = new Shop(database, drawerOpen: false);
        await OpenDrawer(shop, 4_835_000);
        async Task<string?> Try(long typed)
        {
            var counted = await KeepBlind(shop, typed);
            return (await Assert.ThrowsAsync<CashSessionRefusedException>(() => CloseDrawer(shop, counted, closerMay: false, varianceAlert: 20_000))).Code;
        }

        // 8 350,00 short, then every figure that used to walk to the threshold's edge: one answer.
        Assert.Equal(RefusalCodes.CloseNeedsNote, await Try(4_000_000));
        Assert.All(new long[] { 4_815_000, 4_835_000, 4_855_000 }, typed => Assert.Equal(RefusalCodes.CloseNeedsNote, Try(typed).GetAwaiter().GetResult()));

        var closed = await CloseDrawer(shop, await KeepBlind(shop, 4_835_000), note: "Compté une fois.", closerMay: false, authorisedBy: shop.StaffId, varianceAlert: 20_000);
        Assert.Equal((Dzd(4_000_000), Dzd(-835_000)), (closed.Counted, closed.Variance));
    }

    [Fact]
    public async Task No_blind_count_is_kept_with_the_drawer_shut()
    {
        var shop = new Shop(database, drawerOpen: false);

        Assert.Equal(RefusalCodes.NoOpenSession, (await Assert.ThrowsAsync<CashSessionRefusedException>(() => KeepBlind(shop, 100))).Code);
    }

    // ------------------------------------------------------------------ D-121: the clock, the note, the settings

    [Fact]
    public async Task A_close_stamped_before_its_opening_is_refused_by_its_name_and_the_session_stays_open()
    {
        var shop = new Shop(database, drawerOpen: false);
        var opened = new DateTimeOffset(2026, 10, 12, 23, 0, 0, TimeSpan.Zero);
        await Run(shop, (context, work) => new OpenCashSessionHandler(new SalesLedger(context), work, new ClockAt(opened)), new OpenCashSession(shop.TerminalId, shop.StaffId, 0));

        var refused = await Assert.ThrowsAsync<CashSessionRefusedException>(() => Run(shop, (context, work) => new CloseCashSessionHandler(
                new SalesLedger(context), new CashSessionLedger(context), new ReportSettings(), new ClockAt(opened.AddHours(-2))),
            new CloseCashSession(shop.TerminalId, shop.StaffId, 0, null, true, null)));

        Assert.Equal(RefusalCodes.ClockBehind, refused.Code);
        Assert.Equal(CashSessionStatus.Open, (await Session(shop)).Status);

        // The clock put right, it closes.
        var closed = await Run(shop, (context, work) => new CloseCashSessionHandler(
                new SalesLedger(context), new CashSessionLedger(context), new ReportSettings(), new ClockAt(opened.AddMinutes(1))),
            new CloseCashSession(shop.TerminalId, shop.StaffId, 0, null, true, null));
        Assert.Equal(1, closed.ZReportNumber);
    }

    [Fact]
    public async Task A_note_longer_than_a_note_may_be_is_refused_on_a_close_on_cash_out_and_on_a_discount()
    {
        Reasons();
        CashReasons();
        var shop = new Shop(database, drawerOpen: false);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);
        await OpenDrawer(shop, 100_000);
        var tooLong = new string('x', 501);

        var cash = await Assert.ThrowsAsync<CashMovementRefusedException>(() => Cash(shop, CashDirection.Out, 1_000, Sundry, note: tooLong));
        var sale = await Assert.ThrowsAsync<SaleRefusedException>(() =>
            Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 1, Discount: Amount(100, shop.StaffId) with { Note = tooLong })));
        var close = await Assert.ThrowsAsync<CashSessionRefusedException>(() => CloseDrawer(shop, 0, note: tooLong));

        Assert.Equal((RefusalCodes.NoteTooLong, RefusalCodes.NoteTooLong, RefusalCodes.NoteTooLong), (cash.Code, sale.Code, close.Code));
        Assert.Equal(["500"], close.Args);
        using (var read = Read(shop))
        {
            Assert.Equal((0, 0), (await read.CashMovements.CountAsync(), await read.Transactions.CountAsync()));
        }

        // Five hundred characters are a note.
        var closed = await CloseDrawer(shop, 0, note: new string('x', 500));
        Assert.Equal(500, (await Session(shop)).Notes!.Length);
        Assert.Equal(1, closed.ZReportNumber);
    }

    [Fact]
    public async Task A_blind_close_with_the_reports_handed_below_it_is_refused_whichever_key_completes_the_pair()
    {
        var shop = new Shop(database);
        Task<TenantSettings> Set(SetTenantSettings command) =>
            Run(shop, (context, _) => new SetTenantSettingsHandler(new TenantConfigurationStore(context, TabChargesFor.Dzd), TabChargesFor.Dzd, new FixedClock()), command);

        try
        {
            // Both at once; then each key on top of the other.
            await Assert.ThrowsAsync<TenantSettingRefusedException>(() => Set(new SetTenantSettings(BlindClose: true, XReportMinRank: 1)));
            await Set(new SetTenantSettings(BlindClose: true));
            await Assert.ThrowsAsync<TenantSettingRefusedException>(() => Set(new SetTenantSettings(XReportMinRank: 1)));
            await Set(new SetTenantSettings(BlindClose: false, XReportMinRank: 1));
            await Assert.ThrowsAsync<TenantSettingRefusedException>(() => Set(new SetTenantSettings(BlindClose: true)));

            // Whoever reads also closes alone: the pair is no longer one.
            var both = await Set(new SetTenantSettings(BlindClose: true, CloseSessionMinRank: 1));
            Assert.Equal((true, 1L, 1L), (both.BlindClose, both.XReportMinRank, both.CloseSessionMinRank));

            // And raising the close back above the reports is the same pair, from the third key.
            await Assert.ThrowsAsync<TenantSettingRefusedException>(() => Set(new SetTenantSettings(CloseRankOff: true)));
        }
        finally
        {
            // The tenant's row is every test's: left as it was found.
            await Set(new SetTenantSettings(BlindClose: false, XReportRankOff: true, CloseRankOff: true));
        }
    }

    // ------------------------------------------------------------------ D-120: a close is whole

    [Fact]
    public async Task A_close_is_whole_or_it_is_not_one_and_a_till_has_one_drawer_that_is_not_closed()
    {
        CashReasons();
        var shop = new Shop(database, drawerOpen: false);
        await OpenDrawer(shop, 100_000);
        var closed = await CloseDrawer(shop, 100_000);
        using var connection = database.Connect();
        string Refused(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => command.ExecuteNonQuery()).Message;
        }

        // A row that says "closed" and gives no Z number, no expected cash, nobody who counted.
        Assert.Contains("ck_cash_sessions_status_2", Refused(
            $"""
            INSERT INTO cash_sessions (session_id, store_id, terminal_id, opened_by, opened_at, closed_at, counted_cash, status, created_at, updated_at)
            VALUES ('half-{shop.StaffId}', '{shop.StoreId}', '{shop.TerminalId}', '{shop.StaffId}', '2026-01-01 08:00:00', '2026-01-01 09:00:00', 0, 'closed', '2026-01-01 08:00:00', '2026-01-01 08:00:00')
            """), StringComparison.Ordinal);

        // A suspended session is still the till's one drawer: no second one beside it.
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText =
                $"""
                INSERT INTO cash_sessions (session_id, store_id, terminal_id, opened_by, opened_at, status, created_at, updated_at)
                VALUES ('held-{shop.StaffId}', '{shop.StoreId}', '{shop.TerminalId}', '{shop.StaffId}', '2026-01-01 08:00:00', 'suspended', '2026-01-01 08:00:00', '2026-01-01 08:00:00')
                """;
            Assert.Equal(1, insert.ExecuteNonQuery());
        }

        Assert.Contains("UNIQUE", Refused(
            $"""
            INSERT INTO cash_sessions (session_id, store_id, terminal_id, opened_by, opened_at, status, created_at, updated_at)
            VALUES ('second-{shop.StaffId}', '{shop.StoreId}', '{shop.TerminalId}', '{shop.StaffId}', '2026-01-01 08:00:00', 'open', '2026-01-01 08:00:00', '2026-01-01 08:00:00')
            """), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ D-122: a store that charges no TVA

    private Task<TaxRegime> Regime(Shop shop, TaxRegime regime) =>
        Run(shop, (context, _) => new SetTaxRegimeHandler(new Waymark.Persistence.Organisation.StoreSettings(context), new FixedClock()), new SetTaxRegime(regime));

    [Fact]
    public async Task A_store_that_is_not_subject_to_tva_records_none_and_its_price_is_its_price()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 20, daysAgo: 5);
        shop.Receive(database, shop.Bread, 20, daysAgo: 5);

        // Subject, as the fixture has it: milk at 9 %, bread at 19 %, the tax inside the price.
        var taxed = await Sell(shop, (shop.Milk, 2), (shop.Bread, 1));
        Assert.True(taxed.TaxTotal.IsPositive);

        await Regime(shop, TaxRegime.NotSubject);
        var plain = await Sell(shop, (shop.Milk, 2), (shop.Bread, 1));

        // The customer pays the same; nothing of it is called tax, on the ticket or on any row.
        Assert.Equal(taxed.Total, plain.Total);
        Assert.Equal(Dzd(0), plain.TaxTotal);
        using (var read = Read(shop))
        {
            var ticket = await read.Transactions.SingleAsync(row => row.TransactionId == plain.TransactionId);
            Assert.Equal((plain.Total, Dzd(0)), (ticket.Subtotal, ticket.TaxTotal));
            var rows = await read.TransactionItems.Where(row => row.TransactionId == plain.TransactionId).ToListAsync();
            Assert.All(rows, row => Assert.Equal(Dzd(0), row.TaxAmount));

            // The ticket sold while the store was subject keeps what its own rows say.
            var before = await read.Transactions.SingleAsync(row => row.TransactionId == taxed.TransactionId);
            Assert.Equal(taxed.TaxTotal, before.TaxTotal);
        }

        // And back: one setting, nothing in the catalogue.
        await Regime(shop, TaxRegime.Subject);
        Assert.Equal(taxed.TaxTotal, (await Sell(shop, (shop.Milk, 2), (shop.Bread, 1))).TaxTotal);
    }

    [Fact]
    public async Task Receipts_are_in_french_until_the_tenant_says_arabic_and_in_no_third_language()
    {
        var shop = new Shop(database);
        Task<TenantSettings> Set(SetTenantSettings command) =>
            Run(shop, (context, _) => new SetTenantSettingsHandler(new TenantConfigurationStore(context, TabChargesFor.Dzd), TabChargesFor.Dzd, new FixedClock()), command);

        try
        {
            Assert.Equal(Language.Fr, TenantSettings.Defaults.ReceiptLanguage);
            Assert.Equal(Language.Ar, (await Set(new SetTenantSettings(ReceiptLanguage: Language.Ar))).ReceiptLanguage);
            await using (var context = database.NewContext())
            {
                Assert.Equal(Language.Ar, (await new TenantConfigurationStore(context, TabChargesFor.Dzd).CurrentAsync()).ReceiptLanguage);
            }

            // Another key set leaves it as it is; English is the notices', not the paper's.
            Assert.Equal(Language.Ar, (await Set(new SetTenantSettings(TabAsPart: true))).ReceiptLanguage);
            await Assert.ThrowsAsync<TenantSettingRefusedException>(() => Set(new SetTenantSettings(ReceiptLanguage: Language.En)));
        }
        finally
        {
            await Set(new SetTenantSettings(ReceiptLanguage: Language.Fr));
        }
    }

    [Fact]
    public async Task A_store_is_not_subject_to_tva_until_somebody_says_it_is()
    {
        // The column's own default, as a store commissioned with nothing said gets it.
        using var connection = database.Connect();
        using var command = connection.CreateCommand();
        var id = "regime-" + Guid.NewGuid().ToString("N")[..8];
        command.CommandText =
            $"""
            INSERT INTO stores (store_id, store_code, store_name, store_type, created_at, updated_at)
            VALUES ('{id}', '{id}', 'Sans régime', 'grocery', '2026-01-01 08:00:00', '2026-01-01 08:00:00');
            SELECT tax_regime FROM stores WHERE store_id = '{id}';
            """;
        Assert.Equal("not_subject", command.ExecuteScalar());

        command.CommandText = $"UPDATE stores SET tax_regime = 'sometimes' WHERE store_id = '{id}'";
        Assert.Contains("ck_stores_tax_regime", Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => command.ExecuteNonQuery()).Message, StringComparison.Ordinal);
    }
}
