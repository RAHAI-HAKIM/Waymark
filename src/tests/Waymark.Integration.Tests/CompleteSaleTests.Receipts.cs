using Microsoft.EntityFrameworkCore;
using Waymark.Application.Organisation;
using Waymark.Application.Sales;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.Persistence.Organisation;
using Waymark.Persistence.Sales;
using RefusalCodes = Waymark.Contracts.Pos.RefusalCodes;

namespace Waymark.Integration.Tests;

/// <summary>
/// D1 (D-125): what a receipt prints, read off the ticket's rows, and the refund window it promises.
/// The silent failures: a receipt whose figures are worked out again and differ by a centime from
/// the row; a cancelled ticket printed; a refund window printed on the paper that nobody enforces.
/// </summary>
public sealed partial class CompleteSaleTests
{
    private async Task<ReceiptFacts?> ReceiptOf(Shop shop, string idOrNumber)
    {
        await using var context = Read(shop);
        return await new Receipts(context).FindAsync(idOrNumber);
    }

    private Task<TenantSettings> Window(Shop shop, int? days) =>
        Run(shop, (context, _) => new SetTenantSettingsHandler(new TenantConfigurationStore(context, TabChargesFor.Dzd), TabChargesFor.Dzd, new FixedClock()),
            days is null ? new SetTenantSettings(RefundWindowOff: true) : new SetTenantSettings(RefundWindowDays: days));

    private Task<RefundedSale> RefundPastWindow(Shop shop, string original, bool mayPass) =>
        Run(shop, (context, work) => new RefundSaleHandler(
                new RefundLedger(context), new SalesLedger(context), new Waymark.Persistence.Customers.CustomerLedger(context), new CashSessionLedger(context),
                new TenantConfigurationStore(context, TabChargesFor.Dzd), work, new FixedCalendar(), new FixedClock(),
                new Waymark.Persistence.Reference.ReasonCodes(context), TabChargesFor.Context(context)),
            new RefundSale(shop.TerminalId, shop.StaffId, original, [Back(shop.Milk, 1, 14_300)], Faulty, null, RefundTo.Cash, null, true, null, MayPassWindow: mayPass));

    // ------------------------------------------------------------------ the figures are the rows'

    [Fact]
    public async Task A_receipt_prints_the_rows_figures_and_works_none_out_again()
    {
        Reasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);
        var sale = await Sell(
            shop, null,
            new SaleLineRequest(shop.Milk.Barcode, 2, Discount: new GivenDiscount(DiscountForm.Percent, 1_000, Gesture, shop.StaffId)),
            new SaleLineRequest(shop.Bread.Barcode, 1));

        var receipt = await ReceiptOf(shop, sale.InvoiceNumber);

        Assert.NotNull(receipt);
        using var read = Read(shop);
        var row = await read.Transactions.SingleAsync(t => t.TransactionId == sale.TransactionId);
        Assert.Equal((row.TotalAmount, row.Subtotal, row.TaxTotal, row.DiscountTotal), (receipt.Total, receipt.Subtotal, receipt.TaxTotal, receipt.DiscountTotal));

        // Each line is its rows summed, never priced again: its discount and its total as written.
        var items = await read.TransactionItems.Where(i => i.TransactionId == sale.TransactionId).ToListAsync();
        var milk = receipt.Lines[0];
        Assert.Equal(
            (items.Where(i => i.VariantId == shop.Milk.VariantId).Aggregate(Dzd(0), (sum, i) => sum + i.DiscountAmount),
             items.Where(i => i.VariantId == shop.Milk.VariantId).Aggregate(Dzd(0), (sum, i) => sum + i.LineTotal)),
            (milk.Discount, milk.LineTotal));
        Assert.True(milk.Discount.IsPositive);
        Assert.Equal(receipt.Total, receipt.Lines.Aggregate(Dzd(0), (sum, line) => sum + line.LineTotal));

        // The cash step's difference is the rounding_variance row's, which is the sale's own.
        var variance = await read.RoundingVariances.SingleAsync(v => v.ReferenceId == sale.TransactionId);
        Assert.Equal((variance.Amount, sale.Cash.Variance), (receipt.CashRounding!.Value, receipt.CashRounding.Value));
        var cash = await read.TransactionPayments.SingleAsync(p => p.TransactionId == sale.TransactionId);
        Assert.Equal(new PastPayment(PaymentMethod.Cash, cash.Amount), Assert.Single(receipt.Payments));

        Assert.Equal((shop.StoreId, "Caisse 1", "Caissier", 0L, (ReceiptOriginal?)null),
            (receipt.Store.Name, receipt.TerminalName, receipt.StaffName, receipt.ReprintCount, receipt.Original));
    }

    [Fact]
    public async Task A_refunds_receipt_names_its_sale_and_its_reason_and_its_lines_are_below_zero()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        var sale = await Sell(shop, (shop.Milk, 2));
        var refund = await Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300)]);

        var receipt = await ReceiptOf(shop, refund.InvoiceNumber);

        Assert.NotNull(receipt);
        Assert.Equal((sale.InvoiceNumber, "Caisse 1"), (receipt.Original!.InvoiceNumber, receipt.Original.TerminalName));
        Assert.Equal(Faulty, receipt.Reason); // the fixture's French label is its code
        var line = Assert.Single(receipt.Lines);
        Assert.Equal((-1_000L, Dzd(-14_300)), (line.Quantity.Thousandths, line.LineTotal));
        Assert.Equal(Dzd(-14_300), receipt.Total);

        // The sale, refunded since, still prints as the sale it was.
        Assert.Null((await ReceiptOf(shop, sale.InvoiceNumber))!.Original);
    }

    [Fact]
    public async Task A_cancelled_ticket_prints_nothing()
    {
        VoidReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        var cancelled = await Cancel(shop);

        Assert.Null(await ReceiptOf(shop, cancelled.TransactionId));
    }

    [Fact]
    public async Task The_store_prints_its_own_text_set_blank_cleared_and_never_longer_than_its_limit()
    {
        var shop = new Shop(database);
        Task<string?> Footer(string? text) =>
            Run(shop, (context, _) => new SetReceiptFooterHandler(new StoreSettings(context), new FixedClock()), new SetReceiptFooter(text));

        Assert.Equal("Merci de votre visite !\nOuvert 7j/7", await Footer("  Merci de votre visite !\nOuvert 7j/7 "));
        await using (var context = Read(shop))
        {
            Assert.Equal("Merci de votre visite !\nOuvert 7j/7", (await new Receipts(context).StoreAsync())!.Footer);
        }

        await Assert.ThrowsAsync<ReceiptFooterRefusedException>(() => Footer(new string('x', Store.ReceiptFooterMaxLength + 1)));
        await Assert.ThrowsAsync<ReceiptFooterRefusedException>(() => Footer("Merci\u001Bp"));
        Assert.Null(await Footer("   "));
        await using (var context = Read(shop))
        {
            Assert.Null((await new Receipts(context).StoreAsync())!.Footer);
        }
    }

    // ------------------------------------------------------------------ the window the paper promises

    [Fact]
    public async Task A_sale_past_the_window_is_refunded_only_by_the_owner_and_on_its_last_day_by_anyone()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 30);

        // The calendar's today is 19/09. Sold on the 12th is the 7th day after; on the 11th, the 8th.
        var lastDay = await SellAt(shop, new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero), shop.StaffId, new SaleLineRequest(shop.Milk.Barcode, 1));
        var past = await SellAt(shop, new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.Zero), shop.StaffId, new SaleLineRequest(shop.Milk.Barcode, 1));
        await Window(shop, 7);
        try
        {
            Assert.Equal(Dzd(14_300), (await RefundPastWindow(shop, lastDay.TransactionId, mayPass: false)).Total);

            var refused = await Assert.ThrowsAsync<SaleRefusedException>(() => RefundPastWindow(shop, past.TransactionId, mayPass: false));
            Assert.Equal((RefusalCodes.RefundWindowPassed, "7"), (refused.Code, Assert.Single(refused.Args)));

            // A quote writes nothing and is not refused; the owner, or an owner's PIN, refunds it.
            await Run(shop, (context, work) => new RefundSaleHandler(
                    new RefundLedger(context), new SalesLedger(context), new Waymark.Persistence.Customers.CustomerLedger(context), new CashSessionLedger(context),
                    new TenantConfigurationStore(context, TabChargesFor.Dzd), work, new FixedCalendar(), new FixedClock(),
                    new Waymark.Persistence.Reference.ReasonCodes(context), TabChargesFor.Context(context)),
                new RefundSale(shop.TerminalId, shop.StaffId, past.TransactionId, [Back(shop.Milk, 1, 14_300)], Faulty, null, RefundTo.Cash, null, true, null, Quote: true));
            Assert.Equal(Dzd(14_300), (await RefundPastWindow(shop, past.TransactionId, mayPass: true)).Total);

            // With no window, nothing is past it.
            await Window(shop, null);
            var old = await SellAt(shop, new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), shop.StaffId, new SaleLineRequest(shop.Milk.Barcode, 1));
            Assert.Equal(Dzd(14_300), (await RefundPastWindow(shop, old.TransactionId, mayPass: false)).Total);
        }
        finally
        {
            await Window(shop, null);
        }
    }

    [Fact]
    public async Task A_refund_window_is_a_day_to_a_year_kept_until_switched_off()
    {
        var shop = new Shop(database);
        try
        {
            Assert.Null(TenantSettings.Defaults.RefundWindowDays);
            Assert.Equal(7, (await Window(shop, 7)).RefundWindowDays);
            await using (var context = database.NewContext())
            {
                Assert.Equal(7, (await new TenantConfigurationStore(context, TabChargesFor.Dzd).CurrentAsync()).RefundWindowDays);
            }

            Assert.Equal(7, (await Run(shop, (context, _) => new SetTenantSettingsHandler(new TenantConfigurationStore(context, TabChargesFor.Dzd), TabChargesFor.Dzd, new FixedClock()),
                new SetTenantSettings(ReceiptLanguage: Language.Fr))).RefundWindowDays);
            await Assert.ThrowsAsync<TenantSettingRefusedException>(() => Window(shop, 0));
            await Assert.ThrowsAsync<TenantSettingRefusedException>(() => Window(shop, TenantSettings.MaxRefundWindowDays + 1));
            Assert.Null((await Window(shop, null)).RefundWindowDays);
        }
        finally
        {
            await Window(shop, null);
        }
    }
}
