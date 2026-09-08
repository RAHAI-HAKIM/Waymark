// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Inventory;

/// <summary>
/// Maps to <c>stock_counts</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>StockCountConfiguration</c>.
/// </para>
/// </summary>
public sealed class StockCount
{
    /// <summary>Primary key (<c>count_id</c>).</summary>
    public required string CountId { get; init; }

    public required string StoreId { get; init; }

    public required CountType CountType { get; init; }

    public string? ScopeCategoryId { get; init; }

    public required string StartedBy { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? ApprovedBy { get; init; }

    public DateTimeOffset? ApprovedAt { get; init; }

    public long? TotalVarianceValue { get; init; }

    public StockCountStatus Status { get; init; } = StockCountStatus.Draft;

    public string? Notes { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
