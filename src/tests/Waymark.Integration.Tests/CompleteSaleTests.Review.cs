using Microsoft.EntityFrameworkCore;
using Waymark.Application.Customers;
using Waymark.Application.Sales;
using Waymark.Application.Statistics;
using Waymark.Contracts.Pos;
using Waymark.Domain.Customers;
using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
using Waymark.Persistence.Sales;

namespace Waymark.Integration.Tests;

/// <summary>
/// What the block B review changed in a sale, a cancel and a repayment, against a real database
/// (D-103 to D-108). The silent failures: a ticket whose lines read back in another order than they
/// were rung up; a basket that says "cash" whatever was paid; a line struck after the customer paid,
/// with nobody's authorisation; a ticket struck empty that leaves no trace; a ticket that can be
/// neither paid nor cancelled because an approval was forgotten; a tab repaid to a centime no coin pays.
/// </summary>
public sealed partial class CompleteSaleTests
{
    // ------------------------------------------------------------------ D-104: a line's place on its ticket

    [Fact]
    public async Task Each_row_carries_its_lines_place_and_a_past_ticket_reads_in_the_order_it_was_rung_up()
    {
        // Ids do not sort within a millisecond, so a ticket read back by id came in any order. Eight
        // tickets rung up bread then milk: by luck alone all eight read that way once in 256.
        var shop = new Shop(database);
        shop.Receive(database, shop.Bread, 100, daysAgo: 5);
        shop.Receive(database, shop.Milk, 1, daysAgo: 9);   // the older batch gives one unit...
        shop.Receive(database, shop.Milk, 100, daysAgo: 5); // ...and the newer the rest: two rows, one line

        for (var ticket = 0; ticket < 8; ticket++)
        {
            var sale = await Sell(shop, (shop.Bread, 1), (shop.Milk, 2));

            using var read = Read(shop);
            var rows = await read.TransactionItems.Where(item => item.TransactionId == sale.TransactionId).ToListAsync();
            Assert.All(rows.Where(row => row.VariantId == shop.Bread.VariantId), row => Assert.Equal(1, row.LineNumber));
            Assert.All(rows.Where(row => row.VariantId == shop.Milk.VariantId), row => Assert.Equal(2, row.LineNumber));

            var listed = await new PastTickets(read).FindAsync(sale.TransactionId);
            Assert.Equal([shop.Bread.VariantId, shop.Milk.VariantId], listed!.Lines.Select(line => line.VariantId));
        }
    }

    [Fact]
    public async Task A_line_from_two_batches_is_two_rows_sharing_one_place()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 1, daysAgo: 9);
        shop.Receive(database, shop.Milk, 100, daysAgo: 5);

        var sale = await Sell(shop, (shop.Milk, 3));

        var rows = await ItemsOf(shop, sale);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(1, row.LineNumber));
    }

    [Fact]
    public async Task A_struck_line_takes_its_place_after_the_lines_sold()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);

        var sale = await Execute(shop, new CompleteSale(
            shop.TerminalId, shop.StaffId, [new SaleLineRequest(shop.Milk.Barcode, 1)],
            Removed: [new RemovedLine(shop.Bread.Barcode, 1, null, Now.AddMinutes(-3))]));

        using var read = Read(shop);
        var rows = await read.TransactionItems.Where(item => item.TransactionId == sale.TransactionId).ToListAsync();
        Assert.Equal(1, rows.Single(row => row.RemovedAt == null).LineNumber);
        Assert.Equal(2, rows.Single(row => row.RemovedAt != null).LineNumber);
    }

    [Fact]
    public async Task A_refunds_rows_take_their_place_in_the_order_the_lines_were_brought_back()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);
        var sale = await Sell(shop, (shop.Milk, 2), (shop.Bread, 1));

        var refund = await Refund(shop, sale.TransactionId, [Back(shop.Bread, 1, 12_050), Back(shop.Milk, 1, 14_300)]);

        using var read = Read(shop);
        var rows = await read.TransactionItems.Where(item => item.TransactionId == refund.TransactionId).ToListAsync();
        Assert.Equal(1, rows.Single(row => row.VariantId == shop.Bread.VariantId).LineNumber);
        Assert.Equal(2, rows.Single(row => row.VariantId == shop.Milk.VariantId).LineNumber);
    }

    // ------------------------------------------------------------------ B-3: the basket's payment class

    [Fact]
    public async Task The_basket_says_how_the_ticket_was_paid_never_cash_for_everything()
    {
        Reasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 20, daysAgo: 5);

        await Sell(shop, (shop.Milk, 1));                                                              // cash
        await Paid(shop, [new GivenTender(PaymentMethod.Card, 14_300, null)], (shop.Milk, 1));         // the whole ticket by card
        await Paid(shop, [new GivenTender(PaymentMethod.MobileWallet, 10_000, null)], (shop.Milk, 1)); // a part, the rest cash
        await Sell(shop, Percent(10_000, shop.StaffId), new SaleLineRequest(shop.Milk.Barcode, 1));    // given away: no payment row

        var cloud = new StubCloud(database, shop);
        await cloud.DrainAsync();
        Assert.Equal(["cash", "card", "mixed", "none"], cloud.Received.Select(basket => basket.PaymentClass));
    }

    // ------------------------------------------------------------------ D-106: a line struck after "Encaisser"

    private static readonly DateTimeOffset PaymentOpened = Now.AddMinutes(-5);

    private static CompleteSale MilkWithBreadStruck(Shop shop, DateTimeOffset struckAt, bool sellerMayVoid = false, string? authorisedBy = null) => new(
        shop.TerminalId, shop.StaffId, [new SaleLineRequest(shop.Milk.Barcode, 1)],
        Removed: [new RemovedLine(shop.Bread.Barcode, 1, null, struckAt, authorisedBy)],
        PaymentOpenedAt: PaymentOpened, SellerMayVoid: sellerMayVoid);

    [Fact]
    public async Task A_cashiers_strike_after_encaisser_is_refused_without_a_manager_and_nothing_is_written()
    {
        // The cancel's theft, one line at a time: take the cash, close the panel, strike four of five lines.
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);

        var refusal = await Assert.ThrowsAsync<SaleRefusedException>(
            () => Execute(shop, MilkWithBreadStruck(shop, struckAt: PaymentOpened.AddMinutes(2))));

        Assert.Equal(RefusalCodes.StrikeNeedsPin, refusal.Code);
        using var read = Read(shop);
        Assert.False(await read.Transactions.AnyAsync());
        Assert.False(await read.TransactionItems.AnyAsync(item => item.VariantId == shop.Milk.VariantId));
    }

    [Fact]
    public async Task A_cashiers_strike_after_encaisser_is_recorded_with_the_manager_who_let_it_be()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);

        var sale = await Execute(shop, MilkWithBreadStruck(shop, struckAt: PaymentOpened.AddMinutes(2), authorisedBy: shop.StaffId));

        using var read = Read(shop);
        var struck = await read.TransactionItems.SingleAsync(item => item.TransactionId == sale.TransactionId && item.RemovedAt != null);
        Assert.Equal((shop.StaffId, shop.StaffId), (struck.RemovedBy, struck.RemovedAuthorisedBy));

        // Not a void: the sale's own row says nothing of when payment opened (the CHECK forbids it).
        Assert.Null((await read.Transactions.SingleAsync(row => row.TransactionId == sale.TransactionId)).PaymentOpenedAt);
    }

    [Fact]
    public async Task A_line_struck_before_encaisser_or_by_someone_who_may_cancel_alone_names_nobody()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);

        var before = await Execute(shop, MilkWithBreadStruck(shop, struckAt: PaymentOpened.AddMinutes(-1)));
        var byManager = await Execute(shop, MilkWithBreadStruck(shop, struckAt: PaymentOpened.AddMinutes(2), sellerMayVoid: true));

        using var read = Read(shop);
        foreach (var sale in new[] { before, byManager })
        {
            var struck = await read.TransactionItems.SingleAsync(item => item.TransactionId == sale.TransactionId && item.RemovedAt != null);
            Assert.Null(struck.RemovedAuthorisedBy);
        }
    }

    [Fact]
    public async Task A_ticket_whose_every_line_was_struck_is_recorded_as_a_cancel_worth_nothing()
    {
        // Rung up, struck, let go. The server refused a cancel with no line left and the till dropped
        // the ticket: nothing said what had been on it.
        VoidReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        var struckAt = Now.AddMinutes(-3);

        var cancelled = await Cancel(shop, lines: [], removed: [new RemovedLine(shop.Milk.Barcode, 2, null, struckAt)]);

        Assert.Equal(Dzd(0), cancelled.Total);
        using var read = Read(shop);
        var row = await read.Transactions.SingleAsync(sale => sale.TransactionId == cancelled.TransactionId);
        Assert.Equal((TransactionStatus.Voided, Dzd(0), (string?)null), (row.Status, row.TotalAmount, row.InvoiceNumber));
        var struck = await read.TransactionItems.SingleAsync(item => item.TransactionId == cancelled.TransactionId);
        Assert.Equal((shop.Milk.VariantId, struckAt, Dzd(28_600), 1), (struck.VariantId, struck.RemovedAt!.Value, struck.LineTotal, struck.LineNumber));
        Assert.False(await read.StockMovements.AnyAsync());
        Assert.False(await read.Outbox.AnyAsync(message => message.PayloadJson.Contains(shop.StoreId)));
    }

    [Fact]
    public async Task A_sale_with_no_line_left_is_still_refused()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        await Assert.ThrowsAsync<SaleRefusedException>(() => Execute(shop, new CompleteSale(
            shop.TerminalId, shop.StaffId, [], Removed: [new RemovedLine(shop.Milk.Barcode, 1, null, Now)])));
    }

    // ------------------------------------------------------------------ D-105: an approval the server no longer holds

    [Fact]
    public async Task A_sale_citing_an_approval_the_server_no_longer_holds_says_so_and_not_that_the_reason_is_wrong()
    {
        Reasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        var refusal = await Assert.ThrowsAsync<SaleRefusedException>(
            () => Sell(shop, Percent(1_000, by: string.Empty), new SaleLineRequest(shop.Milk.Barcode, 1)));

        Assert.Equal(RefusalCodes.ApprovalExpired, refusal.Code);
    }

    [Fact]
    public async Task A_cancel_is_never_refused_for_an_approval_and_records_the_ticket_at_the_price_in_force()
    {
        // The ticket put on hold with a discount, whose approval is gone, could be neither paid nor cancelled.
        VoidReasons();
        Reasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        var cancelled = await Cancel(
            shop,
            lines: [new SaleLineRequest(shop.Milk.Barcode, 1, Discount: Percent(1_000, by: string.Empty))],
            ticketDiscount: Amount(500, by: string.Empty));

        Assert.Equal(Dzd(14_300), cancelled.Total);
        using var read = Read(shop);
        var row = await read.Transactions.SingleAsync(sale => sale.TransactionId == cancelled.TransactionId);
        Assert.Equal((Dzd(0), (string?)null), (row.DiscountTotal, row.DiscountReasonCode));
    }

    // ------------------------------------------------------------------ D-107: a refusal a cashier meets has its code

    [Fact]
    public async Task A_product_never_received_is_refused_by_code_with_its_name()
    {
        var shop = new Shop(database);

        var refusal = await Assert.ThrowsAsync<SaleRefusedException>(() => Sell(shop, (shop.Milk, 1)));

        Assert.Equal(RefusalCodes.NeverReceived, refusal.Code);
        Assert.Single(refusal.Args);
    }

    [Fact]
    public async Task Parts_above_the_ticket_are_refused_by_code_with_the_total_as_exact_text()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        var refusal = await Assert.ThrowsAsync<SaleRefusedException>(
            () => Paid(shop, [new GivenTender(PaymentMethod.Card, 14_301, null)], (shop.Milk, 1)));

        Assert.Equal((RefusalCodes.PartsAboveTotal, "143.00"), (refusal.Code, Assert.Single(refusal.Args)));
    }

    // ------------------------------------------------------------------ D-108: a tab repaid in cash steps

    private async Task<(Shop Shop, CustomerSummary Karim)> OwingAsync(long centimes)
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 50, daysAgo: 5);
        await ConfigureAsync(shop);
        var karim = await Create(shop, "Karim", NewPhone());
        await Limit(shop, karim.CustomerId, LimitChange.Set, 500_000);
        await OnTab(shop, karim.CustomerId, centimes);
        return (shop, karim);
    }

    [Fact]
    public async Task The_whole_due_repaid_clears_the_tab_exactly_and_the_drawer_takes_the_rounded_cash()
    {
        // 143,00 owed: no coin pays the 3,00. The tab is cleared to the centime, the drawer takes
        // 145,00, and the 2,00 is rounding_variance's, never the drawer's own variance (D-034).
        var (shop, karim) = await OwingAsync(14_300);

        var tab = await Repay(shop, karim.CustomerId, 14_300);

        Assert.Equal((Dzd(0), Dzd(14_500)), (tab.Balance, tab.CashCollected));
        using var read = Read(shop);
        var repayment = await read.ReceivableMovements.SingleAsync(row => row.CustomerId == karim.CustomerId && row.MovementType == ReceivableMovementType.Payment);
        var paidIn = await read.CashMovements.SingleAsync(row => row.MovementId == repayment.CashMovementId);
        var variance = await read.RoundingVariances.SingleAsync(row => row.ReferenceId == repayment.MovementId);
        Assert.Equal((Dzd(-14_300), Dzd(14_500)), (repayment.Amount, paidIn.Amount));
        Assert.Equal(
            (VarianceReferenceType.ReceivableMovement, VarianceSource.CashTender, Dzd(200)),
            (variance.ReferenceType, variance.Source, variance.Amount));
    }

    [Fact]
    public async Task The_whole_due_named_by_what_the_customer_hands_over_clears_the_tab_too()
    {
        var (shop, karim) = await OwingAsync(14_300);

        var tab = await Repay(shop, karim.CustomerId, 14_500);

        Assert.Equal((Dzd(0), Dzd(14_500)), (tab.Balance, tab.CashCollected));
    }

    [Fact]
    public async Task A_part_of_the_due_no_coin_pays_is_refused_by_code_and_moves_nothing()
    {
        var (shop, karim) = await OwingAsync(14_300);

        var refusal = await Assert.ThrowsAsync<CustomerRefusedException>(() => Repay(shop, karim.CustomerId, 10_100));

        Assert.Equal((RefusalCodes.RepayNotOnStep, "5.00"), (refusal.Code, Assert.Single(refusal.Args)));
        using var read = Read(shop);
        Assert.False(await read.CashMovements.AnyAsync());
        Assert.False(await read.ReceivableMovements.AnyAsync(row => row.MovementType == ReceivableMovementType.Payment));
    }

    [Fact]
    public async Task A_part_on_the_cash_step_is_cleared_and_taken_as_typed_with_no_variance()
    {
        var (shop, karim) = await OwingAsync(14_300);

        var tab = await Repay(shop, karim.CustomerId, 10_000);

        Assert.Equal((Dzd(4_300), Dzd(10_000)), (tab.Balance, tab.CashCollected));
        using var read = Read(shop);
        Assert.False(await read.RoundingVariances.AnyAsync(row => row.ReferenceType == VarianceReferenceType.ReceivableMovement));
    }

    [Fact]
    public async Task A_due_the_step_rounds_to_nothing_is_cleared_with_no_cash_at_all()
    {
        // 2,00 owed: no coin pays it, and the nearest step is nothing. The tab is cleared, the drawer
        // takes no cash, and no paid-in of 0,00 is written (the column refuses one).
        var (shop, karim) = await OwingAsync(200);

        var tab = await Repay(shop, karim.CustomerId, 200);

        Assert.Equal((Dzd(0), Dzd(0)), (tab.Balance, tab.CashCollected));
        using var read = Read(shop);
        var repayment = await read.ReceivableMovements.SingleAsync(row => row.CustomerId == karim.CustomerId && row.MovementType == ReceivableMovementType.Payment);
        Assert.Equal((Dzd(-200), (string?)null), (repayment.Amount, repayment.CashMovementId));
        Assert.False(await read.CashMovements.AnyAsync(movement => movement.MovementType == CashMovementType.PaidIn));
        Assert.Equal(Dzd(-200), (await read.RoundingVariances.SingleAsync(row => row.ReferenceId == repayment.MovementId)).Amount);
    }
}
