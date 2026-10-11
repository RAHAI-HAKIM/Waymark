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
using Waymark.StoreServer.Sales;

namespace Waymark.Integration.Tests;

/// <summary>
/// C2 (D-112): a cash session's X and Z, recomputed from its own rows against a real database. The
/// silent failures: takings that no longer come to the tickets; a drawer whose lines no longer come to
/// what was stored at the close; a threshold changed later rewriting which past Zs are flagged; the
/// flags sent to somebody they are about; a refund paid out of a drawer that never held the cash.
/// </summary>
public sealed partial class CompleteSaleTests
{
    /// <summary>The tenant's thresholds as one test wants them, without writing the tenant's own row.</summary>
    private sealed class ReportSettings(int? voidCount = null, long? voidValue = null, long? variance = null) : ITenantConfiguration
    {
        public Task<TenantSettings> CurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(TenantSettings.Defaults with
        {
            VoidAlertCount = voidCount,
            VoidAlertValue = voidValue is { } worth ? Dzd(worth) : null,
            VarianceAlertValue = variance is { } gap ? Dzd(gap) : null,
        });

        public Task StageAsync(TenantSettings settings, string? updatedBy, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<long>> ActiveRanksAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private async Task<ShiftReportData?> ReadX(Shop shop, ITenantConfiguration settings)
    {
        await using var context = Read(shop);
        return await new ShiftReports(new SalesLedger(context), new CashSessionLedger(context), settings).OpenAsync(shop.TerminalId);
    }

    private async Task<ShiftReportData?> ReadZ(Shop shop, long number, ITenantConfiguration settings)
    {
        await using var context = Read(shop);
        return await new ShiftReports(new SalesLedger(context), new CashSessionLedger(context), settings).ClosedAsync(shop.TerminalId, number);
    }

    private Task<ClosedCashSession> CloseWith(Shop shop, long counted, ITenantConfiguration settings, string? note = null) =>
        Run(shop, (context, work) => new CloseCashSessionHandler(new SalesLedger(context), new CashSessionLedger(context), settings, new FixedClock()),
            new CloseCashSession(shop.TerminalId, shop.StaffId, counted, note, true, null));

    /// <summary>What the drawer's lines come to, added up as the report lists them.</summary>
    private static Money DrawerSum(ShiftReportData report) =>
        report.Movements.OpeningFloat + report.Movements.CashSales + report.TenderRounding - report.Movements.CashRefunds
        + report.Movements.PaidIn - report.Movements.PaidOut + report.Movements.TabRepayments - report.Movements.Drops;

    [Fact]
    public async Task A_days_report_reconciles_twice_open_and_closed_and_counts_what_happened()
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

        var milk = await Sell(shop, (shop.Milk, 1));                                                                              // 143,00 cash
        var split = await Paid(shop, [new GivenTender(PaymentMethod.Card, 20_000, "4417")], (shop.Milk, 2), (shop.Bread, 1));     // 406,50: 200,00 card
        var off = await Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 3, Discount: Amount(100, shop.StaffId)));          // 1,00 off a line
        await Refund(shop, milk.TransactionId, [Back(shop.Milk, 1, 14_300)]);                                                      // 143,00 back in cash
        await OnTab(shop, karim.CustomerId, 14_300);                                                                               // on the tab
        await Repay(shop, karim.CustomerId, 14_300);
        await Cash(shop, CashDirection.In, 200_000, Float);
        await Cash(shop, CashDirection.Out, 50_000, BankDeposit);
        await Cancel(shop, lines: [new SaleLineRequest(shop.Bread.Barcode, 3)], paymentOpenedAt: Now, sellerMayVoid: true);        // after "Encaisser"

        var settings = new ReportSettings(voidCount: 5, voidValue: 10_000, variance: 20_000);
        var x = (await ReadX(shop, settings))!;

        // The takings come to the tickets, and cash is the tickets' cash, to the centime.
        Assert.True(x.TakingsReconcile);
        Assert.Equal(milk.Total + split.Total + off.Total + Dzd(14_300), x.Takings.SalesTotal);
        Assert.Equal(Dzd(14_300) + Dzd(20_650) + off.Total, x.Takings.Sales.Single(line => line.Method == PaymentMethod.Cash).Amount);
        Assert.Equal(Dzd(20_000), x.Takings.Sales.Single(line => line.Method == PaymentMethod.Card).Amount);
        Assert.Equal(Dzd(14_300), x.Takings.Sales.Single(line => line.Method == PaymentMethod.OnAccount).Amount);
        Assert.Equal([new TenderLine(PaymentMethod.Cash, 1, Dzd(14_300))], x.Takings.Refunds);
        Assert.Equal(x.Takings.SalesTotal - Dzd(14_300), x.Takings.Net);

        // What happened, counted.
        Assert.Equal((4, 1, 1), (x.Activity.Sold.Count, x.Activity.Refunded.Count, x.Activity.Cancelled.Count));
        Assert.Equal(new Tally(1, Dzd(100)), x.Reductions.LineDiscounts);
        Assert.Equal(new ShiftCashCounts(1, 1, 1), x.CashCounts);

        // The drawer's lines come to what it should hold, the tender rounding its own line.
        Assert.Equal(x.Expected, DrawerSum(x));
        Assert.True(x.DrawerReconciles);
        Assert.False(x.TenderRounding.IsZero);

        // An open session has no variance to flag; its cancel after "Encaisser" and above the value are.
        Assert.Equal([ShiftFlagKind.CancelAfterEncaisser, ShiftFlagKind.CancelAboveValue], x.Flags.Select(flag => flag.Kind));

        var closed = await CloseWith(shop, x.Expected.MinorUnits - 35_000, settings, note: "Erreur de rendu.");
        var z = (await ReadZ(shop, closed.ZReportNumber, settings))!;

        Assert.True(z.TakingsReconcile);
        Assert.True(z.DrawerReconciles);
        Assert.Equal(x.Expected, z.Expected);
        Assert.Equal(z.Expected, DrawerSum(z));
        Assert.Equal(x.Takings.Net, z.Takings.Net);
        Assert.Equal(
            [ShiftFlagKind.CancelAfterEncaisser, ShiftFlagKind.VariancePastThreshold, ShiftFlagKind.CancelAboveValue],
            z.Flags.Select(flag => flag.Kind));
    }

    [Fact]
    public async Task A_z_is_judged_by_the_thresholds_frozen_at_its_close_whatever_they_are_today()
    {
        VoidReasons();
        var shop = new Shop(database, drawerOpen: false);
        shop.Receive(database, shop.Bread, 20, daysAgo: 5);
        await OpenDrawer(shop, 100_000);
        await Cancel(shop, lines: [new SaleLineRequest(shop.Bread.Barcode, 3)]); // worth 361,50

        var atClose = new ReportSettings(voidCount: 5, voidValue: 10_000, variance: 20_000);
        var closed = await CloseWith(shop, 65_000, atClose, note: "Manque."); // 350,00 short

        var session = await Session(shop);
        Assert.Equal((5L, Dzd(10_000), Dzd(20_000)), (session.VoidAlertCount, session.VoidAlertValue!.Value, session.VarianceAlertValue!.Value));

        // Today the owner has raised every threshold out of reach, then switched them all off.
        foreach (var today in new[] { new ReportSettings(voidCount: 99, voidValue: 99_999_900, variance: 99_999_900), new ReportSettings() })
        {
            var z = (await ReadZ(shop, closed.ZReportNumber, today))!;

            Assert.Equal([ShiftFlagKind.VariancePastThreshold, ShiftFlagKind.CancelAboveValue], z.Flags.Select(flag => flag.Kind));
            Assert.Equal(new ReportThresholds(5, Dzd(10_000), Dzd(20_000)), z.Thresholds);
        }
    }

    [Fact]
    public async Task An_open_session_is_judged_by_todays_thresholds()
    {
        VoidReasons();
        var shop = new Shop(database, drawerOpen: false);
        shop.Receive(database, shop.Bread, 20, daysAgo: 5);
        await OpenDrawer(shop, 0);
        await Cancel(shop, lines: [new SaleLineRequest(shop.Bread.Barcode, 3)]);

        Assert.Empty((await ReadX(shop, new ReportSettings()))!.Flags);
        Assert.Equal([ShiftFlagKind.CancelAboveValue], (await ReadX(shop, new ReportSettings(voidValue: 10_000)))!.Flags.Select(flag => flag.Kind));
    }

    [Fact]
    public async Task There_is_no_x_with_the_drawer_shut_and_no_z_of_a_number_the_till_never_gave()
    {
        var shop = new Shop(database, drawerOpen: false);

        Assert.Null(await ReadX(shop, new ReportSettings()));
        Assert.Null(await ReadZ(shop, 1, new ReportSettings()));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")] // StoreServer is Windows-only (D-017), and so is its code.
    [Fact]
    public async Task The_flags_reach_only_who_may_read_them_and_everyone_is_told_how_many()
    {
        VoidReasons();
        var shop = new Shop(database, drawerOpen: false);
        shop.Receive(database, shop.Bread, 20, daysAgo: 5);
        await OpenDrawer(shop, 0);
        await Cancel(shop, lines: [new SaleLineRequest(shop.Bread.Barcode, 3)], paymentOpenedAt: Now, sellerMayVoid: true);
        var report = (await ReadX(shop, new ReportSettings()))!;

        var owner = ShiftReportEndpoints.ToWire(report, id => id is null ? null : "Yacine R.", readsFlags: true, Now);
        var manager = ShiftReportEndpoints.ToWire(report, id => id is null ? null : "Yacine R.", readsFlags: false, Now);

        var flag = Assert.Single(owner.Flags!);
        Assert.Equal((ShiftFlagKinds.CancelAfterEncaisser, "Yacine R.", "361.50"), (flag.Kind, flag.StaffName, flag.Value));
        Assert.Null(manager.Flags);
        Assert.Equal((1, 1), (owner.FlagCount, manager.FlagCount));
        Assert.Equal(ShiftReportKinds.X, owner.Kind);
        Assert.Null(owner.Drawer.Counted); // nobody counted an open drawer
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")] // StoreServer is Windows-only (D-017), and so is its code.
    [Theory]
    [InlineData(2L, null, null, true, 2L)]   // a manager reads alone
    [InlineData(3L, null, null, true, 3L)]
    [InlineData(1L, null, null, false, null)] // a cashier does not
    [InlineData(1L, null, 2L, true, 2L)]      // unless a manager's PIN was cited: the manager reads
    [InlineData(1L, null, 3L, true, 3L)]      // or the owner's: the owner reads, flags and all
    [InlineData(1L, 1L, null, true, 1L)]      // or the shop handed reports to cashiers
    [InlineData(2L, 3L, null, false, null)]   // or kept them for the owner
    [InlineData(null, null, null, false, null)]
    public void A_report_is_read_with_the_sellers_rank_or_the_rank_of_whoever_gave_the_pin(
        long? seller, long? setting, long? approver, bool reads, long? readerRank)
    {
        var rank = ShiftReportEndpoints.ReaderRank(seller, setting, approver, out var may);

        Assert.Equal((reads, readerRank), (may, rank));
    }

    // ------------------------------------------------------------------ F-38, F-35

    [Fact]
    public async Task A_cash_refund_larger_than_the_drawer_should_hold_is_refused_and_nothing_is_written()
    {
        ReturnReasons();
        var shop = new Shop(database, drawerOpen: false);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        await OpenDrawer(shop, 0);

        // Paid by card: the drawer holds nothing, and 143,00 in cash cannot come out of it.
        var sale = await Paid(shop, [new GivenTender(PaymentMethod.Card, 14_300, "4417")], (shop.Milk, 1));
        var refusal = await Assert.ThrowsAsync<SaleRefusedException>(() => Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300)]));

        Assert.Equal(RefusalCodes.DrawerShort, refusal.Code);
        using var read = Read(shop);
        Assert.Equal(1, await read.Transactions.CountAsync());
    }

    [Fact]
    public async Task A_refund_carries_its_sales_rounding_policy_not_the_stores_of_the_day()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        var sale = await Sell(shop, (shop.Milk, 2));

        // The store changes its policy between the sale and its refund.
        using (var write = Read(shop))
        {
            await write.Database.ExecuteSqlAsync($"UPDATE stores SET rounding_policy = 'half_even' WHERE store_id = {shop.StoreId}");
        }

        var refund = await Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300)]);

        using var read = Read(shop);
        Assert.Equal(Rounding.HalfUp, (await read.Transactions.SingleAsync(t => t.TransactionId == refund.TransactionId)).RoundingPolicy);
    }
}
