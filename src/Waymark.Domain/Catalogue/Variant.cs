// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Catalogue;

/// <summary>
/// Maps to <c>variants</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>VariantConfiguration</c>.
/// </para>
/// </summary>
public sealed class Variant
{
    /// <summary>Primary key (<c>variant_id</c>).</summary>
    public required string VariantId { get; init; }

    public required string ProductId { get; init; }

    public required string VariantName { get; init; }

    public string? Barcode { get; init; }

    public string? Plu { get; init; }

    public string? Sku { get; init; }

    public BarcodeType BarcodeType { get; init; } = BarcodeType.Standard;

    public required string SellingUnitCode { get; init; }

    public bool IsWeighted { get; init; }

    public long TareWeight { get; init; }

    public string? Image { get; init; }

    public VariantStatus Status { get; init; } = VariantStatus.Active;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
