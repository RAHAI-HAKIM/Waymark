using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waymark.Application.Customers;
using Waymark.Application.Sales;
using Waymark.Contracts.Pos;
using Waymark.Domain.Customers;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Values;
using Waymark.Persistence.Organisation;
using Waymark.Persistence.Sales;

namespace Waymark.Integration.Tests;

/// <summary>
/// C1 (D-111): the cash session opened with a counted float and closed with a count, against a real
/// database. The silent failures: a sale on a till nobody opened, in a session with no float; a tab
/// repayment's rounding counted twice in what the drawer should hold; a card part counted as cash; a
/// Z number given twice; a closed session edited until its shortage balances.
/// </summary>
public sealed partial class CompleteSaleTests
{
    /// <summary>The tenant's settings as one test wants them, without writing the tenant's own row: it is shared by every test in the database.</summary>
    private sealed class DrawerSettings(long? varianceAlert) : ITenantConfiguration
    {
        public Task<TenantSettings> CurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(TenantSettings.Defaults with { VarianceAlertValue = varianceAlert is { } cents ? Dzd(cents) : null });

        public Task StageAsync(TenantSettings settings, string? updatedBy, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<long>> ActiveRanksAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private Task<OpenedCashSession> OpenDrawer(Shop shop, long opening) =>
        Run(shop, (context, work) => new OpenCashSessionHandler(new SalesLedger(context), work, new FixedClock()),
            new OpenCashSession(shop.TerminalId, shop.StaffId, opening));

    private Task<ClosedCashSession> CloseDrawer(
        Shop shop, long counted, string? note = null, bool closerMay = true, string? authorisedBy = null, long? varianceAlert = null) =>
        Run(shop, (context, work) => new CloseCashSessionHandler(
                new SalesLedger(context), new CashSessionLedger(context), new DrawerSettings(varianceAlert), new FixedClock()),
            new CloseCashSession(shop.TerminalId, shop.StaffId, counted, note, closerMay, authorisedBy));

    private async Task<CashSession> Session(Shop shop)
    {
        using var read = Read(shop);
        return await read.CashSessions.OrderByDescending(session => session.OpenedAt).ThenByDescending(session => session.SessionId).FirstAsync();
    }

    // ------------------------------------------------------------------ opening

    [Fact]
    public async Task With_no_session_open_nothing_sells_and_no_cash_moves_and_nothing_is_written()
    {
        CashReasons();
        var shop = new Shop(database, drawerOpen: false);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);

        var sale = await Assert.ThrowsAsync<SaleRefusedException>(() => Sell(shop, (shop.Milk, 1)));
        var cash = await Assert.ThrowsAsync<CashMovementRefusedException>(() => Cash(shop, CashDirection.In, 10_000, Float));

        Assert.Equal(RefusalCodes.NoOpenSession, sale.Code);
        Assert.Equal(RefusalCodes.NoOpenSession, cash.Code);
        using var read = Read(shop);
        Assert.Equal(0, await read.CashSessions.CountAsync());
        Assert.Equal(0, await read.Transactions.CountAsync());
        Assert.Equal(0, await read.CashMovements.CountAsync());
    }

    [Fact]
    public async Task Opening_writes_the_counted_float_and_names_who_counted_it()
    {
        var shop = new Shop(database, drawerOpen: false);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);

        var opened = await OpenDrawer(shop, 500_000);
        var sale = await Sell(shop, (shop.Milk, 1));

        var session = await Session(shop);
        Assert.Equal(opened.SessionId, session.SessionId);
        Assert.Equal((CashSessionStatus.Open, Dzd(500_000), shop.StaffId, Now), (session.Status, session.OpeningFloat, session.OpenedBy, session.OpenedAt));
        Assert.Null(session.CountedCash);
        using var read = Read(shop);
        Assert.Equal(session.SessionId, (await read.Transactions.SingleAsync(t => t.TransactionId == sale.TransactionId)).CashSessionId);
    }

    [Fact]
    public async Task An_empty_drawer_opens_at_zero_and_a_float_below_zero_does_not()
    {
        var shop = new Shop(database, drawerOpen: false);

        await Assert.ThrowsAsync<CashSessionRefusedException>(() => OpenDrawer(shop, -1));
        await OpenDrawer(shop, 0);

        Assert.Equal(Dzd(0), (await Session(shop)).OpeningFloat);
    }

    [Fact]
    public async Task A_till_has_one_drawer_a_second_opening_is_refused_by_the_handler_and_by_the_schema()
    {
        var shop = new Shop(database, drawerOpen: false);
        await OpenDrawer(shop, 500_000);

        var refusal = await Assert.ThrowsAsync<CashSessionRefusedException>(() => OpenDrawer(shop, 100_000));
        Assert.Equal(RefusalCodes.SessionAlreadyOpen, refusal.Code);

        // And behind the handler, ux_cash_sessions_one_open: a second open row for the till never lands.
        using var write = Read(shop);
        write.CashSessions.Add(new CashSession
        {
            SessionId = $"second-{shop.TerminalId}", StoreId = shop.StoreId, TerminalId = shop.TerminalId, OpenedBy = shop.StaffId, OpenedAt = Now,
            OpeningFloat = Dzd(0), CreatedAt = Now, UpdatedAt = Now,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync());
    }

    // ------------------------------------------------------------------ closing

    [Fact]
    public async Task A_close_sets_the_count_against_everything_that_moved_cash_and_nothing_that_did_not()
    {
        Reasons();
        ReturnReasons();
        CashReasons();
        VoidReasons();
        var shop = new Shop(database, drawerOpen: false);
        shop.Receive(database, shop.Milk, 20, daysAgo: 5);
        shop.Receive(database, shop.Bread, 20, daysAgo: 5);
        await ConfigureAsync(shop);
        var karim = await Create(shop, "Karim", NewPhone());
        await Limit(shop, karim.CustomerId, LimitChange.Set, 500_000);
        await OpenDrawer(shop, 500_000);

        // Cash: 143,00 on the ticket, 145,00 in the drawer.
        var milk = await Sell(shop, (shop.Milk, 1));
        // 406,50: 200,00 by card, which is no cash at all, and the rest 206,50 collected as 205,00.
        var split = await Paid(shop, [new GivenTender(PaymentMethod.Card, 20_000, "4417")], (shop.Milk, 2), (shop.Bread, 1));
        // A refund of one milk, in cash.
        var refund = await Refund(shop, milk.TransactionId, [Back(shop.Milk, 1, 14_300)]);
        // On the tab: nothing in the drawer. Repaid whole: 143,00 cleared, 145,00 taken, and a
        // rounding row of its own that must not be counted a second time (D-108).
        await OnTab(shop, karim.CustomerId, 14_300);
        var repaid = await Repay(shop, karim.CustomerId, 14_300);
        await Cash(shop, CashDirection.In, 200_000, Float);
        await Cash(shop, CashDirection.Out, 50_000, BankDeposit);
        // A cancelled ticket holds no cash.
        await Cancel(shop, lines: [new SaleLineRequest(shop.Bread.Barcode, 3)]);

        var expected = Dzd(500_000) + milk.Cash.Tendered + split.Cash.Tendered + refund.Cash.Tendered + repaid.CashCollected!.Value
            + Dzd(200_000) - Dzd(50_000);
        Assert.Equal(Dzd(14_500), milk.Cash.Tendered);
        Assert.Equal(Dzd(20_500), split.Cash.Tendered);
        Assert.True(refund.Cash.Tendered.IsNegative, "A cash refund takes cash out of the drawer.");
        Assert.Equal(Dzd(14_500), repaid.CashCollected);

        // 350,00 short, past a threshold of 200,00: a note, or nothing closes.
        var counted = expected.MinorUnits - 35_000;
        var refusal = await Assert.ThrowsAsync<CashSessionRefusedException>(() => CloseDrawer(shop, counted, varianceAlert: 20_000));
        Assert.Equal(RefusalCodes.CloseNeedsNote, refusal.Code);
        Assert.Equal(CashSessionStatus.Open, (await Session(shop)).Status);

        var closed = await CloseDrawer(shop, counted, note: "  Erreur de rendu probable.  ", varianceAlert: 20_000);

        Assert.Equal((expected, Dzd(counted), Dzd(-35_000), true), (closed.Expected, closed.Counted, closed.Variance, closed.NoteWasNeeded));
        var session = await Session(shop);
        Assert.Equal(CashSessionStatus.Closed, session.Status);
        Assert.Equal((expected, Dzd(counted), Dzd(-35_000)), (session.ExpectedCash!.Value, session.CountedCash!.Value, session.Variance!.Value));
        Assert.Equal((1L, shop.StaffId, Now, "Erreur de rendu probable."), (session.ZReportNumber, session.ClosedBy, session.ClosedAt, session.Notes));
        Assert.Null(session.ClosedAuthorisedBy);
    }

    [Fact]
    public async Task A_count_that_matches_closes_with_no_note_whatever_the_threshold()
    {
        var shop = new Shop(database, drawerOpen: false);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);
        await OpenDrawer(shop, 100_000);
        var sale = await Sell(shop, (shop.Milk, 1));

        var closed = await CloseDrawer(shop, 100_000 + sale.Cash.Tendered.MinorUnits, varianceAlert: 0);

        Assert.Equal((Dzd(0), false), (closed.Variance, closed.NoteWasNeeded));
        Assert.Null((await Session(shop)).Notes);
    }

    [Fact]
    public async Task Someone_who_may_not_close_alone_closes_with_an_authorisation_and_the_row_names_both()
    {
        var shop = new Shop(database, drawerOpen: false);
        await OpenDrawer(shop, 100_000);

        var refusal = await Assert.ThrowsAsync<CashSessionRefusedException>(() => CloseDrawer(shop, 100_000, closerMay: false));
        Assert.Equal(RefusalCodes.CloseNeedsManager, refusal.Code);
        Assert.Equal(CashSessionStatus.Open, (await Session(shop)).Status);

        await CloseDrawer(shop, 100_000, closerMay: false, authorisedBy: shop.StaffId);

        var session = await Session(shop);
        Assert.Equal((shop.StaffId, shop.StaffId), (session.ClosedBy, session.ClosedAuthorisedBy));
    }

    [Fact]
    public async Task Someone_who_closes_alone_is_not_recorded_as_authorised_by_anyone()
    {
        var shop = new Shop(database, drawerOpen: false);
        await OpenDrawer(shop, 100_000);

        await CloseDrawer(shop, 100_000, closerMay: true, authorisedBy: shop.StaffId);

        Assert.Null((await Session(shop)).ClosedAuthorisedBy);
    }

    [Fact]
    public async Task A_note_is_asked_before_the_managers_pin()
    {
        // The till's order (the board): the count explains itself, then a manager validates it.
        var shop = new Shop(database, drawerOpen: false);
        await OpenDrawer(shop, 100_000);

        var refusal = await Assert.ThrowsAsync<CashSessionRefusedException>(() => CloseDrawer(shop, 50_000, closerMay: false, varianceAlert: 20_000));

        Assert.Equal(RefusalCodes.CloseNeedsNote, refusal.Code);
    }

    [Fact]
    public async Task Z_numbers_run_from_one_on_each_till_and_a_closed_till_sells_nothing()
    {
        var first = new Shop(database, drawerOpen: false);
        var other = new Shop(database, drawerOpen: false);
        first.Receive(database, first.Milk, 5, daysAgo: 5);

        await OpenDrawer(first, 0);
        var one = await CloseDrawer(first, 0);
        await Assert.ThrowsAsync<SaleRefusedException>(() => Sell(first, (first.Milk, 1)));
        await Assert.ThrowsAsync<CashSessionRefusedException>(() => CloseDrawer(first, 0)); // nothing open to close
        await OpenDrawer(first, 0);
        var two = await CloseDrawer(first, 0);
        await OpenDrawer(other, 0);
        var elsewhere = await CloseDrawer(other, 0);

        Assert.Equal((1L, 2L, 1L), (one.ZReportNumber, two.ZReportNumber, elsewhere.ZReportNumber));
    }

    // ------------------------------------------------------------------ F-35

    [Fact]
    public async Task Cash_out_is_refused_past_what_the_drawer_should_hold_and_allowed_to_its_last_coin()
    {
        CashReasons();
        var shop = new Shop(database, drawerOpen: false);
        await OpenDrawer(shop, 10_000);
        await Cash(shop, CashDirection.In, 5_000, Float);

        var refusal = await Assert.ThrowsAsync<CashMovementRefusedException>(() => Cash(shop, CashDirection.Out, 15_001, BankDeposit));
        await Cash(shop, CashDirection.Out, 15_000, BankDeposit);

        Assert.Equal(RefusalCodes.DrawerShort, refusal.Code);
        Assert.Empty(refusal.Args); // what the drawer holds is not said
        using var read = Read(shop);
        Assert.Equal(Dzd(15_000), (await read.CashMovements.SingleAsync(m => m.MovementType == CashMovementType.PaidOut)).Amount);
    }

    // ------------------------------------------------------------------ a closed session is final

    [Fact]
    public async Task A_closed_session_is_never_rewritten_deleted_or_replaced_and_an_open_one_still_closes()
    {
        var shop = new Shop(database, drawerOpen: false);
        await OpenDrawer(shop, 100_000);
        await CloseDrawer(shop, 90_000);
        var closed = await Session(shop);

        using var write = Read(shop);
        var edit = await Assert.ThrowsAsync<SqliteException>(() => write.Database.ExecuteSqlAsync(
            $"UPDATE cash_sessions SET counted_cash = 100000, variance = 0 WHERE session_id = {closed.SessionId}"));
        var delete = await Assert.ThrowsAsync<SqliteException>(() => write.Database.ExecuteSqlAsync(
            $"DELETE FROM cash_sessions WHERE session_id = {closed.SessionId}"));
        var replace = await Assert.ThrowsAsync<SqliteException>(() => write.Database.ExecuteSqlAsync(
            $"""
            INSERT OR REPLACE INTO cash_sessions (session_id, store_id, terminal_id, opened_by, opened_at, opening_float, status, created_at, updated_at)
            VALUES ({closed.SessionId}, {shop.StoreId}, {shop.TerminalId}, {shop.StaffId}, '2026-01-01 08:00:00', 0, 'open', '2026-01-01 08:00:00', '2026-01-01 08:00:00')
            """));
        // By the till's Z number, the table's other key a REPLACE could delete through.
        var replaceByNumber = await Assert.ThrowsAsync<SqliteException>(() => write.Database.ExecuteSqlAsync(
            $"""
            INSERT OR REPLACE INTO cash_sessions (session_id, store_id, terminal_id, opened_by, opened_at, opening_float, closed_at, counted_cash, z_report_number, status, created_at, updated_at)
            VALUES ('another', {shop.StoreId}, {shop.TerminalId}, {shop.StaffId}, '2026-01-01 08:00:00', 0, '2026-01-01 09:00:00', 0, 1, 'closed', '2026-01-01 08:00:00', '2026-01-01 08:00:00')
            """));

        Assert.All(new[] { edit, delete, replace, replaceByNumber }, refused => Assert.Contains("a closed cash session is final", refused.Message, StringComparison.Ordinal));
        var after = await Session(shop);
        Assert.Equal((Dzd(90_000), Dzd(-10_000), 1L), (after.CountedCash!.Value, after.Variance!.Value, after.ZReportNumber));
    }
}
