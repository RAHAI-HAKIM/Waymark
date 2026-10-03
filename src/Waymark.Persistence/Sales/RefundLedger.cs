using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Persistence.Sales;

/// <summary>
/// <see cref="IRefundLedger"/> over the store database (B9, D-098). Never names the store:
/// <c>transactions</c> and <c>returns</c> are filtered by the context, items and payments through their
/// parent (D-062), and <c>batches</c> carry their store.
/// </summary>
public sealed class RefundLedger(WaymarkDbContext context) : IRefundLedger
{
    public async Task<SaleToRefund?> SaleAsync(string idOrInvoiceNumber, CancellationToken cancellationToken = default)
    {
        // A sale with a number: a cancelled ticket (B8) has none and was never paid.
        var sale = await context.Transactions.AsNoTracking().FirstOrDefaultAsync(
            t => (t.TransactionId == idOrInvoiceNumber || t.InvoiceNumber == idOrInvoiceNumber) && t.InvoiceNumber != null,
            cancellationToken);
        if (sale is null)
        {
            return null;
        }

        // The rows the customer paid for, in the order the sale wrote them; a struck line (B8) was not one.
        var items = await context.TransactionItems.AsNoTracking()
            .Where(item => item.TransactionId == sale.TransactionId && item.RemovedAt == null)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.TransactionItemId)
            .ToListAsync(cancellationToken);
        var ids = items.Select(item => item.TransactionItemId).ToList();

        var returned = (await context.Returns.AsNoTracking()
                .Where(row => ids.Contains(row.TransactionItemId))
                .Select(row => new { row.TransactionItemId, row.QuantityReturned })
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.TransactionItemId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.QuantityReturned), StringComparer.Ordinal);

        var batchIds = items.Select(item => item.BatchId).OfType<string>().Distinct().ToList();
        var expiries = await context.Batches.AsNoTracking()
            .Where(batch => batchIds.Contains(batch.BatchId))
            .Select(batch => new { batch.BatchId, batch.ExpirationDate })
            .ToDictionaryAsync(batch => batch.BatchId, batch => batch.ExpirationDate, StringComparer.Ordinal, cancellationToken);

        var currency = Currency.FromCode(sale.Currency);
        var zero = Money.Zero(currency);

        // The tab: what the sale put on it, and what its refunds took back off (negative rows, so negated).
        var paid = await context.TransactionPayments.AsNoTracking()
            .Where(payment => payment.TransactionId == sale.TransactionId && payment.PaymentMethod == PaymentMethod.OnAccount)
            .Select(payment => payment.Amount)
            .ToListAsync(cancellationToken);
        var refunds = context.Transactions.Where(t => t.OriginalTransactionId == sale.TransactionId).Select(t => t.TransactionId);
        var takenBack = await context.TransactionPayments.AsNoTracking()
            .Where(payment => refunds.Contains(payment.TransactionId) && payment.PaymentMethod == PaymentMethod.OnAccount)
            .Select(payment => payment.Amount)
            .ToListAsync(cancellationToken);

        // Store credit (B9b): what the sale spent of it, and what its refunds gave back as credit.
        var creditPaid = await context.TransactionPayments.AsNoTracking()
            .Where(payment => payment.TransactionId == sale.TransactionId && payment.PaymentMethod == PaymentMethod.StoreCredit)
            .Select(payment => payment.Amount)
            .ToListAsync(cancellationToken);
        var creditBack = await context.TransactionPayments.AsNoTracking()
            .Where(payment => refunds.Contains(payment.TransactionId) && payment.PaymentMethod == PaymentMethod.StoreCredit)
            .Select(payment => payment.Amount)
            .ToListAsync(cancellationToken);

        return new SaleToRefund(
            sale,
            [.. items.Select(item => new SoldItem(
                item,
                Quantity.FromThousandths(returned.GetValueOrDefault(item.TransactionItemId), item.UnitCode),
                item.BatchId is { } batch ? expiries.GetValueOrDefault(batch) : null))],
            paid.Aggregate(zero, (sum, amount) => sum + amount),
            -takenBack.Aggregate(zero, (sum, amount) => sum + amount),
            creditPaid.Aggregate(zero, (sum, amount) => sum + amount),
            -creditBack.Aggregate(zero, (sum, amount) => sum + amount));
    }

    public async Task StageStatusAsync(string transactionId, TransactionStatus status, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var sale = await context.Transactions.FirstAsync(t => t.TransactionId == transactionId, cancellationToken);

        // Init-only properties: the change goes through the tracked entry, as StoreSettings does.
        var entry = context.Entry(sale);
        entry.Property(row => row.Status).CurrentValue = status;
        entry.Property(row => row.UpdatedAt).CurrentValue = at;
    }
}
