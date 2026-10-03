using Microsoft.EntityFrameworkCore;
using Waymark.Application.Customers;
using Waymark.Application.Sales;
using Waymark.Domain;
using Waymark.Domain.Enums;
using Waymark.Domain.Ledgers;
using Waymark.Domain.Privacy;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.Persistence.Customers;
using Waymark.Persistence.Organisation;
using Waymark.Persistence.Sales;

namespace Waymark.Integration.Tests;

/// <summary>
/// B9 (D-098): a refund linked to its sale, against a real database. The silent failures: three
/// refunds of one unit that give back a centime more than the line was paid; a line refunded twice
/// over; a refund that spends no number, or the sale's own; stock that comes back without a movement
/// to say why, or an expired yoghurt back on the shelf; a tab sale refunded out of the drawer while
/// the customer still owes it; store credit issued with <c>customers.credit</c> left behind.
/// </summary>
/// <remarks>
/// Every refund goes through <see cref="Refunds"/>, Hakim's piece: the tests that write one are red
/// until it is written. The refusals made before it is asked are green now.
/// </remarks>
public sealed partial class CompleteSaleTests
{
    private const string Faulty = "retour-defectueux";

    private const string ChangedMind = "retour-avis";

    /// <summary>The shop's return reasons: one plain, one that asks for a note.</summary>
    private void ReturnReasons()
    {
        using var context = database.NewContext();
        foreach (var (code, note) in new[] { (Faulty, false), (ChangedMind, true) })
        {
            if (!context.ReasonCodes.Any(reason => reason.ReasonCodeValue == code))
            {
                context.ReasonCodes.Add(new ReasonCode
                {
                    ReasonCodeValue = code, AppliesTo = ReasonCodeAppliesTo.Return, LabelAr = code, LabelFr = code, RequiresNote = note, CreatedAt = Now,
                });
            }
        }

        context.SaveChanges();
    }

    private static ReturnedLine Back(Product product, int units, long unitPrice, bool restock = true) =>
        new(product.VariantId, unitPrice, units * (long)Quantity.Scale, restock);

    private Task<RefundedSale> Refund(
        Shop shop, string original, IReadOnlyList<ReturnedLine> lines, RefundTo to = RefundTo.Cash, string reason = Faulty, string? note = null,
        string? customerId = null, bool sellerMay = true, string? authorisedBy = null, bool quote = false) =>
        Run(shop, (context, work) => new RefundSaleHandler(
                new RefundLedger(context), new SalesLedger(context), new CustomerLedger(context), new TenantConfigurationStore(context, TabChargesFor.Dzd),
                work, new FixedCalendar(), new FixedClock(), new Waymark.Persistence.Reference.ReasonCodes(context), TabChargesFor.Context(context)),
            new RefundSale(shop.TerminalId, shop.StaffId, original, lines, reason, note, to, customerId, sellerMay, authorisedBy, quote));

    // ------------------------------------------------------------------ Hakim: the refund written

    [Fact]
    public async Task A_refund_is_its_own_numbered_ticket_linked_to_the_sale_and_gives_back_what_was_paid()
    {
        ReturnReasons();
        var shop = new Shop(database);
        var batch = shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);
        var sale = await Sell(shop, (shop.Milk, 2), (shop.Bread, 1));
        int outboxBefore;
        using (var count = Read(shop))
        {
            outboxBefore = await count.Outbox.CountAsync();
        }

        var refund = await Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300)]);

        Assert.Equal(Dzd(14_300), refund.Total);
        Assert.Equal(NumberOf(sale.InvoiceNumber) + 1, NumberOf(refund.InvoiceNumber)); // the sales' own sequence
        using var read = Read(shop);
        var row = await read.Transactions.SingleAsync(t => t.TransactionId == refund.TransactionId);
        Assert.Equal((TransactionStatus.Completed, sale.TransactionId, Dzd(-14_300)), (row.Status, row.OriginalTransactionId, row.TotalAmount));
        Assert.Equal(row.TotalAmount, row.Subtotal + row.TaxTotal);

        var item = await read.TransactionItems.SingleAsync(i => i.TransactionId == refund.TransactionId);
        Assert.Equal((-1_000L, batch, Dzd(-14_300)), (item.Quantity, item.BatchId, item.LineTotal));
        var original = await read.TransactionItems.SingleAsync(i => i.TransactionId == sale.TransactionId && i.VariantId == shop.Milk.VariantId);
        var back = await read.Returns.SingleAsync(r => r.RefundTransactionItemId == item.TransactionItemId);
        Assert.Equal((original.TransactionItemId, 1_000L, Dzd(14_300), RefundMethod.Cash, true, Faulty),
            (back.TransactionItemId, back.QuantityReturned, back.RefundAmount, back.RefundMethod, back.RestockFlag, back.ReasonCode));

        var movement = await read.StockMovements.SingleAsync(m => m.MovementType == StockMovementType.ReturnIn);
        Assert.Equal((batch, 1_000L, "return", back.ReturnId), (movement.BatchId, movement.QuantityChanged, movement.ReferenceType, movement.ReferenceId));
        Assert.Equal(9 * Quantity.Scale, (await read.Inventories.SingleAsync(i => i.BatchId == batch)).Quantity); // 10 − 2 + 1

        Assert.Equal(Dzd(-14_300), (await read.TransactionPayments.SingleAsync(p => p.TransactionId == refund.TransactionId && p.PaymentMethod == PaymentMethod.Cash)).Amount);
        Assert.Equal(TransactionStatus.PartiallyRefunded, (await read.Transactions.SingleAsync(t => t.TransactionId == sale.TransactionId)).Status);
        Assert.Equal(outboxBefore, await read.Outbox.CountAsync()); // nothing sent (D-043)
    }

    [Fact]
    public async Task Partial_refunds_of_a_discounted_line_sum_exactly_to_what_it_was_paid()
    {
        Reasons();
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        // 3 × 143,00 less 1,00: 428,00 paid, which no unit price divides.
        var sale = await Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 3, Discount: Amount(100, shop.StaffId)));
        var first = await Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300)]);
        var second = await Refund(shop, sale.InvoiceNumber, [Back(shop.Milk, 1, 14_300)]); // by its number, too
        var third = await Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300)]);

        Assert.Equal(Dzd(42_800), first.Total + second.Total + third.Total);
        using var read = Read(shop);
        var paid = await read.TransactionItems.SingleAsync(i => i.TransactionId == sale.TransactionId);
        var refunds = await read.TransactionItems.Where(i => i.Quantity < 0).ToListAsync();
        Assert.Equal(-paid.TaxAmount, refunds.Aggregate(Dzd(0), (sum, row) => sum + row.TaxAmount)); // the TVA comes back exactly too
        Assert.Equal(paid.DiscountAmount, refunds.Aggregate(Dzd(0), (sum, row) => sum + row.DiscountAmount));
        Assert.All(refunds, row => Assert.Equal(row.LineTotal, row.SellPrice.Times(row.Quantity, Quantity.Scale, Rounding.HalfUp) + row.DiscountAmount));
        Assert.Equal(TransactionStatus.Refunded, (await read.Transactions.SingleAsync(t => t.TransactionId == sale.TransactionId)).Status);

        // Everything is back: a fourth unit is refused, and spends no number.
        read.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SaleRefusedException>(() => Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300)]));
        var after = await Sell(shop, (shop.Milk, 1));
        Assert.Equal(NumberOf(third.InvoiceNumber) + 1, NumberOf(after.InvoiceNumber));
    }

    [Fact]
    public async Task More_than_is_left_is_refused_and_nothing_is_written()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        var sale = await Sell(shop, (shop.Milk, 2));

        await Assert.ThrowsAsync<SaleRefusedException>(() => Refund(shop, sale.TransactionId, [Back(shop.Milk, 3, 14_300)]));
        await Assert.ThrowsAsync<SaleRefusedException>(() => Refund(shop, sale.TransactionId, [Back(shop.Bread, 1, 12_050)])); // not on the ticket
        await Assert.ThrowsAsync<SaleRefusedException>(() => Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 99_900)])); // not at that price

        using var read = Read(shop);
        Assert.False(await read.Transactions.AnyAsync(t => t.OriginalTransactionId != null));
        Assert.False(await read.Returns.AnyAsync());
        Assert.Equal(TransactionStatus.Completed, (await read.Transactions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Nothing_goes_back_on_the_shelf_when_the_cashier_says_no()
    {
        ReturnReasons();
        var shop = new Shop(database);
        var batch = shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        var sale = await Sell(shop, (shop.Milk, 2));

        await Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300, restock: false)]);

        using var read = Read(shop);
        Assert.False((await read.Returns.SingleAsync()).RestockFlag);
        Assert.False(await read.StockMovements.AnyAsync(m => m.MovementType == StockMovementType.ReturnIn));
        Assert.Equal(8 * Quantity.Scale, (await read.Inventories.SingleAsync(i => i.BatchId == batch)).Quantity);
    }

    [Fact]
    public async Task An_expired_batch_is_never_put_back_on_the_shelf()
    {
        ReturnReasons();
        var shop = new Shop(database);
        var batch = shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        var sale = await Sell(shop, (shop.Milk, 2));
        using (var context = database.NewContext())
        {
            // It expired yesterday, after it was sold.
            await context.Database.ExecuteSqlAsync($"UPDATE batches SET expiration_date = '2026-09-18' WHERE batch_id = {batch}");
        }

        await Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300, restock: true)]);

        using var read = Read(shop);
        Assert.False((await read.Returns.SingleAsync()).RestockFlag);
        Assert.False(await read.StockMovements.AnyAsync(m => m.MovementType == StockMovementType.ReturnIn));
    }

    [Fact]
    public async Task A_cash_refund_rounds_once_to_the_cash_step()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);
        var sale = await Sell(shop, (shop.Bread, 1));

        var refund = await Refund(shop, sale.TransactionId, [Back(shop.Bread, 1, 12_050)]);

        // 120,50 back: 120,00 out of the drawer, the 0,50 to rounding_variance, never to the drawer's (D-034).
        Assert.Equal(new CashTender(Dzd(-12_000), Dzd(50)), refund.Cash);
        using var read = Read(shop);
        Assert.Equal(Dzd(-12_050), (await read.TransactionPayments.SingleAsync(p => p.TransactionId == refund.TransactionId)).Amount);
        Assert.Equal(Dzd(50), (await read.RoundingVariances.SingleAsync(v => v.ReferenceId == refund.TransactionId)).Amount);
    }

    [Fact]
    public async Task A_quote_says_what_the_refund_comes_to_and_writes_nothing()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);
        var sale = await Sell(shop, (shop.Bread, 1));

        var quote = await Refund(shop, sale.TransactionId, [Back(shop.Bread, 1, 12_050)], sellerMay: false, quote: true);

        Assert.Equal((string.Empty, Dzd(12_050), Dzd(-12_000)), (quote.TransactionId, quote.Total, quote.Cash.Tendered));
        using var read = Read(shop);
        Assert.False(await read.Transactions.AnyAsync(t => t.OriginalTransactionId != null));
        Assert.Equal(TransactionStatus.Completed, (await read.Transactions.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_sale_on_the_tab_is_refunded_to_the_tab_not_out_of_the_drawer()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        await ConfigureAsync(shop);
        var customer = await Create(shop, "Samira", NewPhone());
        await Limit(shop, customer.CustomerId, LimitChange.Set, 100_000);
        var sale = await OnTab(shop, customer.CustomerId, 14_300);

        var refund = await Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300)]);

        Assert.Equal((Dzd(14_300), Dzd(0)), (refund.ToTab, refund.Rest));
        using var read = Read(shop);
        var payment = await read.TransactionPayments.SingleAsync(p => p.TransactionId == refund.TransactionId);
        Assert.Equal((PaymentMethod.OnAccount, Dzd(-14_300)), (payment.PaymentMethod, payment.Amount));
        var charge = await read.ReceivableMovements.SingleAsync(m => m.PaymentId == payment.PaymentId);
        Assert.Equal((ReceivableMovementType.Charge, Dzd(-14_300)), (charge.MovementType, charge.Amount));
        Assert.Equal(0, (await read.ReceivableMovements.Where(m => m.CustomerId == customer.CustomerId).ToListAsync()).Sum(m => m.Amount.MinorUnits));
        Assert.Equal(RefundMethod.OnAccount, (await read.Returns.SingleAsync()).RefundMethod);
    }

    [Fact]
    public async Task Store_credit_goes_to_the_tickets_customer_and_customers_credit_follows_the_ledger()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        await ConfigureAsync(shop);
        var customer = await Create(shop, "Yacine", NewPhone());
        var sale = await Execute(shop, new CompleteSale(shop.TerminalId, shop.StaffId, [new SaleLineRequest(shop.Milk.Barcode, 2)], CustomerId: customer.CustomerId));

        var first = await Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300)], RefundTo.StoreCredit);
        var second = await Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300)], RefundTo.StoreCredit);

        Assert.Equal((Dzd(14_300), Dzd(28_600)), (first.CreditBalance!.Value, second.CreditBalance!.Value));
        using var read = Read(shop);
        var issued = await read.CreditMovements.Where(m => m.CustomerId == customer.CustomerId).ToListAsync();
        Assert.All(issued, m => Assert.Equal((CreditMovementType.Issue, Dzd(14_300)), (m.MovementType, m.Amount)));
        Assert.Equal([Dzd(14_300), Dzd(28_600)], issued.Select(m => m.BalanceAfter).Order());
        Assert.Equal(Dzd(28_600), (await read.Customers.SingleAsync(c => c.CustomerId == customer.CustomerId)).Credit); // in step
        Assert.Equal(Dzd(-14_300), (await read.TransactionPayments.SingleAsync(p => p.TransactionId == first.TransactionId)).Amount);
        Assert.Equal(PaymentMethod.StoreCredit, (await read.TransactionPayments.SingleAsync(p => p.TransactionId == first.TransactionId)).PaymentMethod);
        Assert.True(await read.ProcessingLog.AnyAsync(entry => entry.SubjectId == PseudonymOf(customer.CustomerId) && entry.Operation == Operation.Collection));
    }

    // ------------------------------------------------------------------ refused before the rule is asked

    [Fact]
    public async Task Store_credit_with_no_customer_or_the_module_off_is_refused()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        await ConfigureAsync(shop);
        var anonymous = await Sell(shop, (shop.Milk, 1));

        // Quoted in cash: store credit may be offered (the module is on), but the ticket has nobody yet (B9a).
        var quote = await Refund(shop, anonymous.TransactionId, [Back(shop.Milk, 1, 14_300)], quote: true);
        Assert.Equal((true, false), (quote.MayCredit, quote.CustomerOnTicket));

        await Assert.ThrowsAsync<SaleRefusedException>(() => Refund(shop, anonymous.TransactionId, [Back(shop.Milk, 1, 14_300)], RefundTo.StoreCredit));

        var customer = await Create(shop, "Nadia", NewPhone());
        var named = await Execute(shop, new CompleteSale(shop.TerminalId, shop.StaffId, [new SaleLineRequest(shop.Milk.Barcode, 1)], CustomerId: customer.CustomerId));
        await ConfigureAsync(shop, module: false);
        await Assert.ThrowsAsync<SaleRefusedException>(() => Refund(shop, named.TransactionId, [Back(shop.Milk, 1, 14_300)], RefundTo.StoreCredit));

        using var read = Read(shop);
        Assert.False(await read.CreditMovements.AnyAsync(m => m.CustomerId == customer.CustomerId));
        Assert.False(await read.Transactions.AnyAsync(t => t.OriginalTransactionId != null));
    }

    [Fact]
    public async Task A_seller_below_the_shops_rank_is_refused_without_an_authorisation()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        var sale = await Sell(shop, (shop.Milk, 1));

        await Assert.ThrowsAsync<SaleRefusedException>(() => Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300)], sellerMay: false));

        using var read = Read(shop);
        Assert.False(await read.Transactions.AnyAsync(t => t.OriginalTransactionId != null));
    }

    [Fact]
    public async Task An_authorised_refund_names_who_authorised_it()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        var sale = await Sell(shop, (shop.Milk, 1));

        await Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300)], sellerMay: false, authorisedBy: shop.StaffId);

        using var read = Read(shop);
        Assert.Equal(shop.StaffId, (await read.Returns.SingleAsync()).ApprovedBy);
    }

    [Fact]
    public async Task A_refund_a_cancelled_ticket_or_a_reason_that_is_not_a_return_is_refused()
    {
        ReturnReasons();
        VoidReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        var sale = await Sell(shop, (shop.Milk, 2));

        await Assert.ThrowsAsync<SaleRefusedException>(() => Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300)], reason: Mistake)); // a void reason
        await Assert.ThrowsAsync<SaleRefusedException>(() => Refund(shop, sale.TransactionId, [Back(shop.Milk, 1, 14_300)], reason: ChangedMind)); // no note
        await Assert.ThrowsAsync<SaleRefusedException>(() => Refund(shop, "no-such-ticket", [Back(shop.Milk, 1, 14_300)]));

        // A cancelled ticket (B8) has no number and was never paid: there is nothing to refund.
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);
        var cancelled = await Cancel(shop);
        await Assert.ThrowsAsync<SaleRefusedException>(() => Refund(shop, cancelled.TransactionId, [Back(shop.Milk, 1, 14_300)]));

        using (var context = database.NewContext(storeId: shop.StoreId))
        {
            // A refund already written, as the generator writes one: a refund is never refunded itself.
            context.Transactions.Add(new Transaction
            {
                TransactionId = $"refund-{Guid.NewGuid():N}", StoreId = shop.StoreId, TerminalId = shop.TerminalId, StaffId = shop.StaffId,
                InvoiceNumber = $"{shop.StoreCode}-2026-9{Random.Shared.Next(10_000, 99_999)}", OccurredAt = Now, RoundingPolicy = Rounding.HalfUp,
                OriginalTransactionId = sale.TransactionId, Status = TransactionStatus.Completed, CreatedAt = Now, UpdatedAt = Now,
            });
            await context.SaveChangesAsync();
        }

        using var read = Read(shop);
        var written = await read.Transactions.SingleAsync(t => t.OriginalTransactionId == sale.TransactionId);
        // Refused for being a refund, not for some later reason that happens to apply.
        var refused = await Assert.ThrowsAsync<SaleRefusedException>(() => Refund(shop, written.TransactionId, [Back(shop.Milk, 1, 14_300)]));
        Assert.StartsWith("A refund is not refunded", refused.Message, StringComparison.Ordinal);
        Assert.False(await read.Returns.AnyAsync());
    }

    // ------------------------------------------------------------------ the ticket view

    [Fact]
    public async Task The_ticket_view_shows_what_came_back_and_a_refund_names_its_sale()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        var sale = await Sell(shop, (shop.Milk, 3));
        var refund = await Refund(shop, sale.TransactionId, [Back(shop.Milk, 2, 14_300)]);

        await using var read = Read(shop);
        var tickets = new PastTickets(read);
        var paid = await tickets.FindAsync(sale.TransactionId);
        Assert.Equal((TransactionStatus.PartiallyRefunded, Quantity.FromThousandths(2_000, shop.Unit)), (paid!.Status, Assert.Single(paid.Lines).Returned!.Value));
        var back = await tickets.FindAsync(refund.InvoiceNumber);
        Assert.Equal((sale.TransactionId, sale.InvoiceNumber), (back!.OriginalTransactionId, back.OriginalInvoiceNumber));
        Assert.Equal(Quantity.FromThousandths(-2_000, shop.Unit), Assert.Single(back.Lines).Quantity);
    }
}
