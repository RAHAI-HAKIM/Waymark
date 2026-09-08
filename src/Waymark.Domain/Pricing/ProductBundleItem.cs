// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Pricing;

/// <summary>
/// Maps to <c>product_bundle_items</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>ProductBundleItemConfiguration</c>.
/// </para>
/// </summary>
public sealed class ProductBundleItem
{
    /// <summary>Primary key (<c>bundle_item_id</c>).</summary>
    public required string BundleItemId { get; init; }

    public required string BundleId { get; init; }

    public required string VariantId { get; init; }

    public required long Quantity { get; init; }

    public required string ValidFrom { get; init; }

    public string? ValidTo { get; init; }

    public string? Description { get; init; }

    public ProductBundleItemStatus Status { get; init; } = ProductBundleItemStatus.Active;
}
