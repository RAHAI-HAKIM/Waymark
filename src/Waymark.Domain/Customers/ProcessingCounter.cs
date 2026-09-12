using Waymark.Domain.Enums;

namespace Waymark.Domain.Customers;

/// <summary>
/// Maps to <c>processing_counters</c>. What is left of a day's processing log
/// once the statutory period has passed.
///
/// <para>
/// The logbook required by Loi 25-11 article 41 bis 3 grows by roughly 1 500
/// rows a day in a busy store, and consultation is named in the article so it
/// cannot be sampled or suppressed. Growth is bounded at retention instead of at
/// write: full rows are kept for as long as the law requires, then rolled up
/// into one counter per <c>(store, day, operation, purpose)</c> and the detail
/// dropped. Volume evidence survives forever, detail survives as long as it must,
/// and the table stops growing (decisions.md D-045).
/// </para>
/// <para>
/// Not append-only, unlike the log it summarises: a roll-up is rerun by
/// replacing the day's rows, which is what makes it safe to repeat after an
/// interrupted purge.
/// </para>
/// </summary>
public sealed class ProcessingCounter : IStoreScoped
{
    /// <summary>Primary key (<c>counter_id</c>). A ULID from <c>IIdGenerator</c>.</summary>
    public required string CounterId { get; init; }

    /// <summary>
    /// Null where the operation belonged to no store, the same shape
    /// <c>processing_log.store_id</c> carries.
    /// </summary>
    public string? StoreId { get; init; }

    /// <summary>The day being summarised, <c>YYYY-MM-DD</c>.</summary>
    public required DateOnly Day { get; init; }

    public required Operation Operation { get; init; }

    public required ProcessingPurpose Purpose { get; init; }

    /// <summary>How many log rows this replaces. Never zero — a roll-up with nothing to count writes no row.</summary>
    public required long EventCount { get; init; }

    /// <summary>When the detail was dropped. Evidence that the purge ran.</summary>
    public required DateTimeOffset RolledUpAt { get; init; }
}
