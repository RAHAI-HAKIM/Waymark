namespace Waymark.Domain;

/// <summary>
/// Which store this process is acting as.
///
/// <para>
/// Declared in Domain and implemented outside it, like every other port here:
/// Domain states that something must answer the question, and does not care
/// what does. At Basic tier the answer comes from the one store row on the
/// till; later it comes from the session.
/// </para>
/// <para>
/// <b>Unset means nothing is visible.</b> A tenancy filter that opens up when
/// it is not configured is worse than one that returns nothing, because the
/// first failure is silent and the second is obvious in the first minute. So
/// <see cref="StoreId"/> being null does not disable the filter — it makes it
/// match only rows that belong to no store.
/// </para>
/// </summary>
public interface ICurrentStore
{
    /// <summary>The store's ULID, or null when none has been established.</summary>
    string? StoreId { get; }
}

/// <summary>
/// A fixed answer, for a host that already knows its store and for tests.
/// </summary>
public sealed class FixedCurrentStore(string? storeId) : ICurrentStore
{
    public string? StoreId { get; } = storeId;
}
