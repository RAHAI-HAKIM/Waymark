// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Values;

namespace Waymark.Domain.Inventory;

/// <summary>
/// Maps to <c>batch_items</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>BatchItemConfiguration</c>.
/// </para>
/// </summary>
public sealed class BatchItem
{
    /// <summary>Part of the primary key (<c>batch_id</c>).</summary>
    public required string BatchId { get; init; }

    /// <summary>Part of the primary key (<c>variant_id</c>).</summary>
    public required string VariantId { get; init; }

    public required long QuantityReceived { get; init; }

    public required string UnitCode { get; init; }

    public required Money UnitCost { get; init; }

    public string Currency { get; init; } = "DZD";

    public required DateTimeOffset CreatedAt { get; init; }
}
