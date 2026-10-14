using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Persistence.Sales;

/// <summary>
/// <b>Session D1.</b> <see cref="IReceipts"/> over the store database (D-125).
///
/// <para><b>The rules <c>ReceiptsTests</c> hold you to:</b></para>
/// <list type="number">
///   <item><description><b>A numbered sale or refund only</b>: completed, refunded, partially refunded.
///   A cancelled ticket has no number and prints nothing (D-124).</description></item>
///   <item><description><b>Every figure is a row's</b>: the totals the ticket's, a line's discount and
///   total its rows' sums, the rounding the <c>rounding_variance</c> row's. Nothing is recomputed (D-053).</description></item>
///   <item><description><b>Lines as the ticket view has them</b> (D-088): one per product and price, in
///   the order the sale wrote them; a struck line is not one.</description></item>
///   <item><description><b>No customer</b>: neither a name nor a balance reaches a receipt (D-123).</description></item>
/// </list>
/// </summary>
public sealed class Receipts(WaymarkDbContext context) : IReceipts
{
    private static readonly TransactionStatus[] Printed =
        [TransactionStatus.Completed, TransactionStatus.Refunded, TransactionStatus.PartiallyRefunded];

    public async Task<ReceiptStore?> StoreAsync(CancellationToken cancellationToken = default)
    {
        var store = await context.Stores.FirstOrDefaultAsync(cancellationToken);
        return store is null
            ? null
            : new ReceiptStore(store.StoreName, store.Address, store.ContactPhone, store.TaxRegistrationNumber, store.ReceiptFooter, store.Timezone);
    }

    public async Task<ReceiptFacts?> FindAsync(string idOrInvoiceNumber, CancellationToken cancellationToken = default)
    {
        var ticket = await context.Transactions.FirstOrDefaultAsync(
            t => (t.TransactionId == idOrInvoiceNumber || t.InvoiceNumber == idOrInvoiceNumber) && Printed.Contains(t.Status) && t.InvoiceNumber != null,
            cancellationToken);
        if (ticket is null || await StoreAsync(cancellationToken) is not { } store)
        {
            return null;
        }

        var staffName = await context.Staff
            .Where(person => person.StaffId == ticket.StaffId)
            .Select(person => person.StaffName)
            .FirstOrDefaultAsync(cancellationToken);

        var rows = await context.TransactionItems
            .Where(item => item.TransactionId == ticket.TransactionId && item.RemovedAt == null)
            .OrderBy(item => item.LineNumber).ThenBy(item => item.CreatedAt).ThenBy(item => item.TransactionItemId)
            .Join(context.Variants, item => item.VariantId, variant => variant.VariantId, (item, variant) => new { item, variant })
            .Join(context.Products, row => row.variant.ProductId, product => product.ProductId,
                (row, product) => new { row.item, row.variant.VariantName, product.ProductName })
            .ToListAsync(cancellationToken);

        // One line per (variant, price), summed in memory: Money adds with +, which SQL cannot do
        // on a converted column. GroupBy keeps the order in which each line first appears.
        var lines = rows
            .GroupBy(row => (row.item.VariantId, row.item.SellPrice))
            .Select(line =>
            {
                var first = line.First();
                return new ReceiptItem(
                    first.ProductName,
                    first.VariantName,
                    Quantity.FromThousandths(line.Sum(row => row.item.Quantity), first.item.UnitCode),
                    first.item.SellPrice,
                    line.Skip(1).Aggregate(first.item.DiscountAmount, (sum, row) => sum + row.item.DiscountAmount),
                    line.Skip(1).Aggregate(first.item.LineTotal, (sum, row) => sum + row.item.LineTotal),
                    line.Any(row => row.item.QuantitySource != QuantitySource.Count));
            })
            .ToList();

        var payments = await context.TransactionPayments
            .Where(payment => payment.TransactionId == ticket.TransactionId)
            .OrderBy(payment => payment.Sequence)
            .Select(payment => new PastPayment(payment.PaymentMethod, payment.Amount))
            .ToListAsync(cancellationToken);

        // The cash step's difference, as the sale or the refund recorded it (D-034): one row or none.
        var rounding = await context.RoundingVariances
            .Where(variance => variance.ReferenceType == VarianceReferenceType.Transaction && variance.ReferenceId == ticket.TransactionId
                && variance.Source == VarianceSource.CashTender)
            .Select(variance => variance.Amount)
            .ToListAsync(cancellationToken);

        ReceiptOriginal? original = null;
        string? reason = null;
        if (ticket.OriginalTransactionId is { } originalId)
        {
            original = await context.Transactions
                .Where(sale => sale.TransactionId == originalId)
                .Select(sale => new { sale.InvoiceNumber, sale.OccurredAt, sale.TerminalId })
                .FirstOrDefaultAsync(cancellationToken) is { InvoiceNumber: { } number } sold
                ? new ReceiptOriginal(number, sold.OccurredAt, await TerminalNameAsync(sold.TerminalId, cancellationToken))
                : null;

            // A refund gives one reason for all its lines (B9): read through its returns rows.
            var refundRows = rows.Select(row => row.item.TransactionItemId).ToList();
            reason = await context.Returns
                .Where(back => back.RefundTransactionItemId != null && refundRows.Contains(back.RefundTransactionItemId))
                .Join(context.ReasonCodes, back => back.ReasonCode, code => code.ReasonCodeValue, (_, code) => code.LabelFr)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new ReceiptFacts(
            store,
            ticket.TransactionId,
            ticket.InvoiceNumber!,
            ticket.OccurredAt,
            ticket.TerminalId,
            await TerminalNameAsync(ticket.TerminalId, cancellationToken),
            staffName,
            lines,
            ticket.Subtotal,
            ticket.TaxTotal,
            ticket.TotalAmount,
            ticket.DiscountTotal,
            payments,
            rounding.Count == 0 ? null : rounding.Skip(1).Aggregate(rounding[0], (sum, part) => sum + part),
            ticket.ReprintCount,
            original,
            reason);
    }

    /// <summary>The till as the shop named it; its id when it has no row, which a ticket's foreign key does not allow.</summary>
    private async Task<string> TerminalNameAsync(string terminalId, CancellationToken cancellationToken) =>
        await context.Terminals.Where(till => till.TerminalId == terminalId).Select(till => till.TerminalName).FirstOrDefaultAsync(cancellationToken)
        ?? terminalId;
}
