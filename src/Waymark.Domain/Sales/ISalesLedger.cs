using Waymark.Domain.Inventory;
using Waymark.Domain.Organisation;
using Waymark.Domain.Values;

namespace Waymark.Domain.Sales;

/// <summary>
/// What a sale reads from the store database, and the one level it changes (D-070).
/// Implemented in Persistence; every read is scoped to the current store by the global
/// filter, so nothing here takes a store id.
/// </summary>
public interface ISalesLedger
{
    /// <summary>This store's row: its code for invoice numbers, and its rounding policy.</summary>
    Task<Store?> CurrentStoreAsync(CancellationToken cancellationToken = default);

    /// <summary>The terminal's open cash session, if it has one.</summary>
    Task<CashSession?> OpenCashSessionAsync(string terminalId, CancellationToken cancellationToken = default);

    /// <summary>The highest invoice number starting with <paramref name="prefix"/>, if any.</summary>
    Task<string?> LastInvoiceNumberAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>The variant's batches in this store, with their levels, in <paramref name="unitCode"/>.</summary>
    Task<IReadOnlyList<BatchLevel>> BatchesAsync(string variantId, string unitCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a change to a batch's level (<c>inventories.quantity</c>), written at commit
    /// with the stock movement that explains it.
    /// </summary>
    void AdjustLevel(string variantId, string batchId, QuantityDelta change, DateTimeOffset at);
}
