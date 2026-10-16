namespace Waymark.Domain.Sales;

/// <summary>
/// A ticket printed again (D3, D-123, D-127): a count on its row, <c>transactions.reprint_count</c>,
/// no table. The paper itself says when and by whom. Store scoping is the global filter.
/// </summary>
public interface IReprints
{
    /// <summary>
    /// Stages one more reprint on a numbered sale or refund, written when the executor commits (D-050).
    /// The count after it; null when this store has no such ticket.
    /// </summary>
    Task<long?> StageReprintAsync(string transactionId, CancellationToken cancellationToken = default);
}
