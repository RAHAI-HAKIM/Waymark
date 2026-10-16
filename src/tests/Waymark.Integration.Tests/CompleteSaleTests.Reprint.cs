using Microsoft.EntityFrameworkCore;
using Waymark.Application.Sales;
using Waymark.Domain.Sales;
using Waymark.Persistence.Sales;

namespace Waymark.Integration.Tests;

/// <summary>
/// D3 (D-127): a reprint, against a real database. The silent failures: a reprint counted twice or
/// not at all; a reprint that rewrites what the ticket says; a cancelled ticket given a paper.
/// </summary>
public sealed partial class CompleteSaleTests
{
    private Task<long> Reprint(Shop shop, string transactionId) =>
        Run(shop, (context, _) => new ReprintTicketHandler(new Reprints(context)), new ReprintTicket(transactionId));

    [Fact]
    public async Task A_reprint_counts_one_more_and_changes_nothing_the_ticket_says()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        var sale = await Sell(shop, (shop.Milk, 2));
        Transaction before;
        using (var read = Read(shop))
        {
            before = await read.Transactions.AsNoTracking().SingleAsync(t => t.TransactionId == sale.TransactionId);
        }

        Assert.Equal(1, await Reprint(shop, sale.TransactionId));
        Assert.Equal(2, await Reprint(shop, sale.TransactionId));

        using var after = Read(shop);
        var row = await after.Transactions.SingleAsync(t => t.TransactionId == sale.TransactionId);
        Assert.Equal(2, row.ReprintCount);
        Assert.Equal(
            (before.Status, before.TotalAmount, before.Subtotal, before.TaxTotal, before.InvoiceNumber, before.OccurredAt, before.UpdatedAt),
            (row.Status, row.TotalAmount, row.Subtotal, row.TaxTotal, row.InvoiceNumber, row.OccurredAt, row.UpdatedAt));

        // The ticket view and the receipt say so; the till and its card's reference come with it.
        var past = (await new PastTickets(after).FindAsync(sale.InvoiceNumber))!;
        Assert.Equal((2L, "Caisse 1"), (past.ReprintCount, past.TerminalName));
        Assert.Equal(2, (await new Receipts(after).FindAsync(sale.InvoiceNumber))!.ReprintCount);
    }

    [Fact]
    public async Task A_cancelled_ticket_or_one_that_is_not_the_stores_is_never_reprinted()
    {
        VoidReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        var cancelled = await Cancel(shop);

        await Assert.ThrowsAsync<ReprintRefusedException>(() => Reprint(shop, cancelled.TransactionId));
        await Assert.ThrowsAsync<ReprintRefusedException>(() => Reprint(shop, "no-such-ticket"));

        var other = new Shop(database);
        other.Receive(database, other.Milk, 10, daysAgo: 5);
        var theirs = await Sell(other, (other.Milk, 1));
        await Assert.ThrowsAsync<ReprintRefusedException>(() => Reprint(shop, theirs.TransactionId));
    }
}
