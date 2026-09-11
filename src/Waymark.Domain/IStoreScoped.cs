namespace Waymark.Domain;

/// <summary>
/// An entity that belongs to one store.
///
/// <para>
/// Implementing this is what puts an entity behind the global query filter in
/// <c>WaymarkDbContext</c>. It is a marker rather than a convention on the
/// property name, so adding <c>store_id</c> to a table does not silently opt it
/// in and forgetting to add it here cannot be mistaken for a decision — the
/// filter list is something a person wrote down.
/// </para>
/// <para>
/// Cross-tenant leakage is DPIA risk R9, and CLAUDE.md §3.3 requires the filter
/// rather than a <c>where</c> clause a caller might forget.
/// </para>
/// </summary>
public interface IStoreScoped
{
    /// <summary>
    /// The store this row belongs to.
    ///
    /// <para>
    /// Nullable because two tables allow it — <c>promotions</c> with no store is
    /// a promotion for every store, and <c>processing_log</c> with no store is
    /// processing that happened outside one. Those rows are visible to every
    /// store by design. Everywhere else the column is NOT NULL and the filter
    /// is a plain equality.
    /// </para>
    /// </summary>
    string? StoreId { get; }
}
