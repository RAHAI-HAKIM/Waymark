using Microsoft.EntityFrameworkCore;
using Waymark.Application.Organisation;
using Waymark.Application.Sales;
using Waymark.Application.Statistics;
using Waymark.Domain.Enums;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;
using Waymark.Persistence.Organisation;
using Waymark.Persistence.Sales;

namespace Waymark.Integration.Tests;

/// <summary>
/// B8 (D-097): a ticket cancelled at the till, and the lines struck on a paid one, against a real
/// database. The silent failures: a cancel that leaves nothing behind; a cancelled ticket that moves
/// stock, takes an invoice number or reaches the cloud; a cashier cancelling a ticket the customer was
/// paying, with nobody's authorisation; a struck line counted in what the customer paid.
/// </summary>
/// <remarks>
/// The cancels go through <see cref="Voids.NeedsAuthorisation"/>, Hakim's piece: those tests are red
/// until it is written. The struck lines of a paid sale do not ask it.
/// </remarks>
public sealed partial class CompleteSaleTests
{
    private const string Mistake = "erreur-saisie";

    private void VoidReasons()
    {
        using var context = database.NewContext();
        if (!context.ReasonCodes.Any(reason => reason.ReasonCodeValue == Mistake))
        {
            context.ReasonCodes.Add(new ReasonCode
            {
                ReasonCodeValue = Mistake, AppliesTo = ReasonCodeAppliesTo.Void, LabelAr = "خطأ في الإدخال", LabelFr = "Erreur de saisie", CreatedAt = Now,
            });
            context.SaveChanges();
        }
    }

    private async Task<VoidedTicket> Cancel(
        Shop shop, bool sellerMayVoid = false, DateTimeOffset? paymentOpenedAt = null, string? authorisedBy = null, string reason = Mistake,
        IReadOnlyList<RemovedLine>? removed = null)
    {
        await using var context = database.NewContext(storeId: shop.StoreId);
        var ids = new Waymark.Application.IdGenerator.UlidGenerator();
        var unitOfWork = new Waymark.Persistence.WaymarkUnitOfWork(context);
        var executor = new Waymark.Application.Commands.CommandExecutor(
            unitOfWork, ids, new Waymark.Persistence.Privacy.ProcessingLogWriter(context, ids, new Domain.FixedCurrentStore(shop.StoreId), new FixedClock()));
        var calendar = new FixedCalendar();
        var sales = new CompleteSaleHandler(
            new Waymark.Persistence.Catalogue.ProductLookup(context, calendar), new SalesLedger(context), unitOfWork, new Waymark.Persistence.Sync.OutboxSequence(context),
            new NullTier2Writer(), calendar, new FixedClock(), new Waymark.Persistence.Reference.ReasonCodes(context), TabChargesFor.Context(context));
        var handler = new VoidTicketHandler(sales, new Waymark.Persistence.Reference.ReasonCodes(context));

        return await executor.ExecuteAsync(handler, new VoidTicket(
            shop.TerminalId, shop.StaffId, [new SaleLineRequest(shop.Milk.Barcode, 2), new SaleLineRequest(shop.Bread.Barcode, 1)], null,
            reason, sellerMayVoid, paymentOpenedAt, authorisedBy, removed));
    }

    // ------------------------------------------------------------------ Hakim: a cancelled ticket

    [Fact]
    public async Task A_cancelled_ticket_is_recorded_priced_as_its_sale_and_moves_nothing()
    {
        VoidReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);

        int outboxBefore;
        using (var count = Read(shop))
        {
            outboxBefore = await count.Outbox.CountAsync();
        }

        var cancelled = await Cancel(shop);

        Assert.Equal(Dzd(40_650), cancelled.Total); // 2 × 143,00 + 120,50, as the sale would have been
        using var read = Read(shop);
        var row = await read.Transactions.SingleAsync(sale => sale.TransactionId == cancelled.TransactionId);
        Assert.Equal((TransactionStatus.Voided, (string?)null, Mistake, shop.StaffId), (row.Status, row.InvoiceNumber, row.VoidReasonCode, row.VoidedBy));
        Assert.All(await read.TransactionItems.Where(item => item.TransactionId == cancelled.TransactionId).ToListAsync(), item => Assert.Null(item.BatchId));
        Assert.False(await read.StockMovements.AnyAsync());
        Assert.False(await read.TransactionPayments.AnyAsync());
        Assert.Equal(outboxBefore, await read.Outbox.CountAsync()); // nothing sent: nothing was sold
        Assert.All(await read.Inventories.ToListAsync(), level => Assert.Equal(10 * Domain.Values.Quantity.Scale, level.Quantity));
    }

    [Fact]
    public async Task A_cancel_spends_no_invoice_number_and_is_not_listed_among_the_tickets()
    {
        VoidReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);

        var before = await Sell(shop, (shop.Milk, 1));
        var cancelled = await Cancel(shop);
        var after = await Sell(shop, (shop.Milk, 1));

        Assert.Equal(NumberOf(before.InvoiceNumber) + 1, NumberOf(after.InvoiceNumber));
        await using var read = Read(shop);
        var listed = await new PastTickets(read).ListAsync(null, Now.AddDays(-1), Now.AddDays(1));
        Assert.DoesNotContain(listed, ticket => ticket.TransactionId == cancelled.TransactionId);
        Assert.Equal(2, listed.Count);
    }

    [Fact]
    public async Task A_cashiers_cancel_after_encaisser_is_refused_without_a_manager_and_recorded_with_one()
    {
        VoidReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);
        var opened = Now.AddMinutes(-2);

        await Assert.ThrowsAsync<SaleRefusedException>(() => Cancel(shop, paymentOpenedAt: opened));
        using (var read = Read(shop))
        {
            Assert.False(await read.Transactions.AnyAsync());
        }

        var cancelled = await Cancel(shop, paymentOpenedAt: opened, authorisedBy: shop.StaffId);
        using (var read = Read(shop))
        {
            var row = await read.Transactions.SingleAsync(sale => sale.TransactionId == cancelled.TransactionId);
            Assert.Equal((opened, shop.StaffId), (row.PaymentOpenedAt!.Value, row.VoidAuthorisedBy));
        }
    }

    [Fact]
    public async Task A_manager_cancels_after_encaisser_with_a_reason_only()
    {
        VoidReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);

        var cancelled = await Cancel(shop, sellerMayVoid: true, paymentOpenedAt: Now.AddMinutes(-1));

        using var read = Read(shop);
        Assert.Null((await read.Transactions.SingleAsync(sale => sale.TransactionId == cancelled.TransactionId)).VoidAuthorisedBy);
    }

    [Fact]
    public async Task A_cancel_with_no_accepted_reason_is_refused_and_nothing_is_written()
    {
        VoidReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);

        await Assert.ThrowsAsync<SaleRefusedException>(() => Cancel(shop, reason: "no-such-reason"));

        using var read = Read(shop);
        Assert.False(await read.Transactions.AnyAsync());
    }

    // ------------------------------------------------------------------ struck lines on a paid sale

    [Fact]
    public async Task A_struck_line_is_recorded_with_who_and_when_and_counts_in_no_total_and_no_stock()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);
        var struck = Now.AddMinutes(-3);

        var sale = await Execute(shop, new CompleteSale(
            shop.TerminalId, shop.StaffId, [new SaleLineRequest(shop.Milk.Barcode, 1)],
            Removed: [new RemovedLine(shop.Bread.Barcode, 1, null, struck)]));

        Assert.Equal(Dzd(14_300), sale.Total); // the milk alone
        using var read = Read(shop);
        var removed = await read.TransactionItems.SingleAsync(item => item.TransactionId == sale.TransactionId && item.RemovedAt != null);
        Assert.Equal((shop.Bread.VariantId, struck, shop.StaffId, (string?)null, Dzd(12_050)),
            (removed.VariantId, removed.RemovedAt!.Value, removed.RemovedBy, removed.BatchId, removed.LineTotal));
        Assert.DoesNotContain(await read.StockMovements.ToListAsync(), movement => movement.VariantId == shop.Bread.VariantId);
        Assert.Equal(Dzd(14_300), (await read.Transactions.SingleAsync(row => row.TransactionId == sale.TransactionId)).TotalAmount);

        var ticket = await new PastTickets(read).FindAsync(sale.TransactionId);
        Assert.Equal([shop.Milk.VariantId], ticket!.Lines.Select(line => line.VariantId)); // what the customer paid for
    }

    // ------------------------------------------------------------------ the owner's thresholds

    [Fact]
    public async Task The_cancel_thresholds_read_back_as_set_and_off_removes_them()
    {
        var shop = new Shop(database);
        await Run(shop, (context, _) => new SetTenantSettingsHandler(new TenantConfigurationStore(context, TabChargesFor.Dzd), TabChargesFor.Dzd, new FixedClock()),
            new SetTenantSettings(VoidAlertCount: 5, VoidAlertValue: 500_000));
        await using (var context = database.NewContext())
        {
            var set = await new TenantConfigurationStore(context, TabChargesFor.Dzd).CurrentAsync();
            Assert.Equal((5, Dzd(500_000)), (set.VoidAlertCount, set.VoidAlertValue!.Value));
        }

        await Run(shop, (context, _) => new SetTenantSettingsHandler(new TenantConfigurationStore(context, TabChargesFor.Dzd), TabChargesFor.Dzd, new FixedClock()),
            new SetTenantSettings(VoidCountOff: true, VoidValueOff: true));
        await using (var context = database.NewContext())
        {
            var off = await new TenantConfigurationStore(context, TabChargesFor.Dzd).CurrentAsync();
            Assert.Equal(((int?)null, (Domain.Values.Money?)null), (off.VoidAlertCount, off.VoidAlertValue));
        }
    }
}
