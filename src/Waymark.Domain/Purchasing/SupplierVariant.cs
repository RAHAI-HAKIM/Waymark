// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Purchasing;

/// <summary>
/// Maps to <c>supplier_variant</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>SupplierVariantConfiguration</c>.
/// </para>
/// </summary>
public sealed class SupplierVariant
{
    /// <summary>Part of the primary key (<c>supplier_id</c>).</summary>
    public required string SupplierId { get; init; }

    /// <summary>Part of the primary key (<c>variant_id</c>).</summary>
    public required string VariantId { get; init; }

    public required string PurchaseUnitCode { get; init; }

    public long UnitsPerPurchaseUnit { get; init; } = 1000L;

    public long MinimumOrderQuantity { get; init; }

    public long? StatedLeadTimeDays { get; init; }

    public required long PurchasePrice { get; init; }

    public string Currency { get; init; } = "DZD";

    public long? NetDays { get; init; }

    public DateTimeOffset? LastPriceAt { get; init; }

    public bool IsActive { get; init; } = true;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
