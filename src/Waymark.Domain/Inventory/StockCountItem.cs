// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Inventory;

/// <summary>
/// Maps to <c>stock_count_items</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>StockCountItemConfiguration</c>.
/// </para>
/// </summary>
public sealed class StockCountItem
{
    /// <summary>Primary key (<c>count_item_id</c>).</summary>
    public required string CountItemId { get; init; }

    public required string CountId { get; init; }

    public required string VariantId { get; init; }

    public required string BatchId { get; init; }

    public required long ExpectedQuantity { get; init; }

    public long? CountedQuantity { get; init; }

    public long? VarianceQuantity { get; init; }

    public long? UnitCost { get; init; }

    public long? VarianceValue { get; init; }

    public string? CountedBy { get; init; }

    public DateTimeOffset? CountedAt { get; init; }

    public bool RecountFlag { get; init; }

    public string? Note { get; init; }
}
