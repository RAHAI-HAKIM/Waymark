using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Enums;
using Waymark.Domain.Inventory;
using Waymark.Domain.Organisation;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Persistence.Sales;

/// <summary>
/// <see cref="ISalesLedger"/> over the store database (D-070). Every query is scoped to the
/// current store by the global filter (CLAUDE.md §3.3); none names a store.
/// </summary>
public sealed class SalesLedger(WaymarkDbContext context) : ISalesLedger
{
    public Task<Store?> CurrentStoreAsync(CancellationToken cancellationToken = default) =>
        context.Stores.FirstOrDefaultAsync(cancellationToken);

    public Task<CashSession?> OpenCashSessionAsync(string terminalId, CancellationToken cancellationToken = default) =>
        context.CashSessions
            .Where(session => session.TerminalId == terminalId && session.Status == CashSessionStatus.Open)
            .OrderByDescending(session => session.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<string?> LastInvoiceNumberAsync(string prefix, CancellationToken cancellationToken = default) =>
        // Zero-padded, so the highest number is also the last in text order.
        context.Transactions
            .Where(transaction => transaction.InvoiceNumber != null && transaction.InvoiceNumber.StartsWith(prefix))
            .MaxAsync(transaction => transaction.InvoiceNumber, cancellationToken);

    public async Task<IReadOnlyList<BatchLevel>> BatchesAsync(
        string variantId, string unitCode, CancellationToken cancellationToken = default)
    {
        // Tracked, not projected: AdjustLevel changes these same rows, and a second line of the
        // same variant in one sale must see the level the first one left.
        var levels = await context.Inventories
            .Where(level => level.VariantId == variantId)
            .ToListAsync(cancellationToken);

        var batchIds = levels.Select(level => level.BatchId).ToList();
        var batches = await context.Batches
            .Where(batch => batchIds.Contains(batch.BatchId))
            .ToDictionaryAsync(batch => batch.BatchId, cancellationToken);
        var costs = await context.BatchItems
            .Where(item => item.VariantId == variantId && batchIds.Contains(item.BatchId))
            .ToDictionaryAsync(item => item.BatchId, item => item.UnitCost, cancellationToken);

        return [.. levels
            .Where(level => batches.ContainsKey(level.BatchId))
            .Select(level =>
            {
                var batch = batches[level.BatchId];
                return new BatchLevel(
                    batch.BatchId,
                    batch.ReceivedDate,
                    batch.ExpirationDate,
                    Quantity.FromThousandths(level.Quantity, unitCode),
                    costs.TryGetValue(batch.BatchId, out var cost) ? cost : null);
            })];
    }

    public void AdjustLevel(string variantId, string batchId, QuantityDelta change, DateTimeOffset at)
    {
        // The row was loaded (and is tracked) by BatchesAsync; the allocation only names batches
        // that came from there.
        var level = context.Inventories.Local.Single(row => row.VariantId == variantId && row.BatchId == batchId);
        var entry = context.Entry(level);
        entry.Property(row => row.Quantity).CurrentValue = level.Quantity + change.Thousandths;
        entry.Property(row => row.UpdatedAt).CurrentValue = at;
    }
}
