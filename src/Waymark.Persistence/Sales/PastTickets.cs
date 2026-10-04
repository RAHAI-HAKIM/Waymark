using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Persistence.Sales;

/// <summary>
/// <b>Session B1.</b> <see cref="IPastTickets"/> over the store database (D-088).
///
/// <para><b>The rules <c>PastTicketsTests</c> hold you to:</b></para>
/// <list type="number">
///   <item><description><b>Finished sales only</b>: completed, voided, refunded, partially refunded.
///   Open and parked transactions are neither listed nor found.</description></item>
///   <item><description><b>The window is half-open</b>: <c>since</c> included, <c>until</c> excluded, so
///   a sale at midnight belongs to one day and never to two. <c>occurred_at</c> is stored as UTC text
///   in one sortable format (<c>WaymarkConverters.Timestamp</c>), so a comparison in the query is a
///   comparison of instants; check it is translated to SQL rather than done in memory.</description></item>
///   <item><description><b>Newest first</b> in the list. One till, or every till when
///   <c>terminalId</c> is null.</description></item>
///   <item><description><b>Found by id or by invoice number</b>, the same method.</description></item>
///   <item><description><b>One line per product and price.</b> The sale writes a row per batch
///   (D-070): rows of the same variant at the same <c>sell_price</c> are one line, their quantities,
///   tax and line totals summed. A different price is another line. Lines keep the order in which
///   the sale first wrote them (<c>created_at</c>, then <c>transaction_item_id</c>).
///   <c>PastTicketSummary.LineCount</c> counts the merged lines.</description></item>
///   <item><description><b>The figures are the row's</b>: subtotal, tax total and total come from the
///   <c>transactions</c> row, never summed again from the lines.</description></item>
///   <item><description><b>Names are today's catalogue</b>, the product's and the variant's; the
///   staff name is null when that person is not this store's any more. Payments by
///   <c>sequence</c>.</description></item>
///   <item><description><b>Never name the store.</b> <c>transactions</c> is filtered by the context,
///   and its items and payments through their parent (D-062).</description></item>
/// </list>
/// </summary>
public sealed class PastTickets(WaymarkDbContext context) : IPastTickets
{
    /// <summary>The store's context, filtered to the current store. Read it in the two methods below.</summary>
    private readonly WaymarkDbContext _context = context;

    /// <summary>What a ticket is: a sale that finished. Open and parked ones are not.</summary>
    private static readonly TransactionStatus[] Finished =
        [TransactionStatus.Completed, TransactionStatus.Voided, TransactionStatus.Refunded, TransactionStatus.PartiallyRefunded];

    public async Task<IReadOnlyList<PastTicketSummary>> ListAsync(
        string? terminalId, DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken = default)
    {
        // The window and the status in SQL: occurred_at is sortable UTC text, so >= and < compare
        // instants. A null terminal means every till, so the condition is added only when there is one.
        // A ticket cancelled before it was paid (B8) has no number and was never a ticket: not listed.
        var query = _context.Transactions
            .Where(sale => sale.OccurredAt >= since && sale.OccurredAt < until && Finished.Contains(sale.Status) && sale.InvoiceNumber != null);
        if (terminalId is not null)
        {
            query = query.Where(sale => sale.TerminalId == terminalId);
        }

        var sales = await query.OrderByDescending(sale => sale.OccurredAt).ToListAsync(cancellationToken);

        // The line count, after the merge: one query for the items of every sale listed, then the
        // distinct (variant, price) pairs per sale, counted in memory. Money cannot be grouped in
        // SQL (it is an INTEGER behind a converter), and a day's items are few.
        var ids = sales.Select(sale => sale.TransactionId).ToList();
        // A struck line (B8) was never part of the ticket the customer paid: not counted.
        var items = await _context.TransactionItems
            .Where(item => ids.Contains(item.TransactionId) && item.RemovedAt == null)
            .Select(item => new { item.TransactionId, item.VariantId, item.SellPrice })
            .ToListAsync(cancellationToken);
        var lineCounts = items
            .GroupBy(item => item.TransactionId)
            .ToDictionary(sale => sale.Key, sale => sale.Select(item => (item.VariantId, item.SellPrice)).Distinct().Count());

        return [.. sales.Select(sale => new PastTicketSummary(
            sale.TransactionId,
            sale.InvoiceNumber,
            sale.OccurredAt,
            sale.TerminalId,
            sale.Status,
            lineCounts.GetValueOrDefault(sale.TransactionId),
            // The total the ticket came to: the row's, as recorded (D-053).
            sale.TotalAmount))];
    }

    public async Task<PastTicket?> FindAsync(string idOrInvoiceNumber, CancellationToken cancellationToken = default)
    {
        var sale = await _context.Transactions.FirstOrDefaultAsync(
            t => (t.TransactionId == idOrInvoiceNumber || t.InvoiceNumber == idOrInvoiceNumber) && Finished.Contains(t.Status) && t.InvoiceNumber != null,
            cancellationToken);
        if (sale is null)
        {
            return null;
        }

        // A seller who is no longer this store's leaves the ticket without a name, never without a ticket.
        var staffName = await _context.Staff
            .Where(person => person.StaffId == sale.StaffId)
            .Select(person => person.StaffName)
            .FirstOrDefaultAsync(cancellationToken);

        // Every row the sale wrote, in the order it wrote them, with today's names. The merge is done
        // in memory: Money sums with +, which SQL cannot do on a converted column.
        var rows = await _context.TransactionItems
            .Where(item => item.TransactionId == sale.TransactionId && item.RemovedAt == null)
            .OrderBy(item => item.LineNumber).ThenBy(item => item.CreatedAt).ThenBy(item => item.TransactionItemId)
            .Join(_context.Variants, item => item.VariantId, variant => variant.VariantId, (item, variant) => new { item, variant })
            .Join(_context.Products, row => row.variant.ProductId, product => product.ProductId,
                (row, product) => new { row.item, row.variant.VariantName, product.ProductName })
            .ToListAsync(cancellationToken);

        // What refunds have brought back of each row so far (B9, D-098).
        var rowIds = rows.Select(row => row.item.TransactionItemId).ToList();
        var returned = (await _context.Returns
                .Where(back => rowIds.Contains(back.TransactionItemId))
                .Select(back => new { back.TransactionItemId, back.QuantityReturned })
                .ToListAsync(cancellationToken))
            .GroupBy(back => back.TransactionItemId)
            .ToDictionary(back => back.Key, back => back.Sum(row => row.QuantityReturned), StringComparer.Ordinal);

        // One line per (variant, price). GroupBy keeps the order in which each key first appears,
        // so the lines stay in the order the sale first wrote them.
        var lines = rows
            .GroupBy(row => (row.item.VariantId, row.item.SellPrice))
            .Select(line =>
            {
                var first = line.First();
                return new PastTicketLine(
                    first.item.VariantId,
                    first.ProductName,
                    first.VariantName,
                    Quantity.FromThousandths(line.Sum(row => row.item.Quantity), first.item.UnitCode),
                    first.item.SellPrice,
                    line.Skip(1).Aggregate(first.item.TaxAmount, (sum, row) => sum + row.item.TaxAmount),
                    line.Skip(1).Aggregate(first.item.LineTotal, (sum, row) => sum + row.item.LineTotal),
                    Quantity.FromThousandths(line.Sum(row => returned.GetValueOrDefault(row.item.TransactionItemId)), first.item.UnitCode),
                    line.Any(row => row.item.QuantitySource != QuantitySource.Count));
            })
            .ToList();

        var payments = await _context.TransactionPayments
            .Where(payment => payment.TransactionId == sale.TransactionId)
            .OrderBy(payment => payment.Sequence)
            .Select(payment => new PastPayment(payment.PaymentMethod, payment.Amount))
            .ToListAsync(cancellationToken);

        // A refund (B9) says which sale it refunds; the sale's number is read through the same filter.
        var originalNumber = sale.OriginalTransactionId is { } original
            ? await _context.Transactions.Where(t => t.TransactionId == original).Select(t => t.InvoiceNumber).FirstOrDefaultAsync(cancellationToken)
            : null;

        // The figures are the row's, never summed again from the lines (D-053).
        return new PastTicket(
            sale.TransactionId, sale.InvoiceNumber, sale.OccurredAt, sale.TerminalId, sale.StaffId, staffName, sale.Status,
            lines, sale.Subtotal, sale.TaxTotal, sale.TotalAmount, payments, sale.OriginalTransactionId, originalNumber, sale.CustomerId is not null);
    }
}
