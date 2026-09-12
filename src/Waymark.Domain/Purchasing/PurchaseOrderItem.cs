// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Values;

namespace Waymark.Domain.Purchasing;

/// <summary>
/// Maps to <c>purchase_order_items</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>PurchaseOrderItemConfiguration</c>.
/// </para>
/// </summary>
public sealed class PurchaseOrderItem
{
    /// <summary>Part of the primary key (<c>order_id</c>).</summary>
    public required string OrderId { get; init; }

    /// <summary>Part of the primary key (<c>variant_id</c>).</summary>
    public required string VariantId { get; init; }

    public required long QuantityOrdered { get; init; }

    public long QuantityReceived { get; init; }

    public required string PurchaseUnitCode { get; init; }

    public long UnitsPerPurchaseUnit { get; init; } = 1000L;

    public required Money UnitCost { get; init; }

    public Money Discount { get; init; }

    public required Money LineTotal { get; init; }
}
