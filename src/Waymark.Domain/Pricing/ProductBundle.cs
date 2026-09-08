// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Pricing;

/// <summary>
/// Maps to <c>product_bundles</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>ProductBundleConfiguration</c>.
/// </para>
/// </summary>
public sealed class ProductBundle
{
    /// <summary>Primary key (<c>bundle_id</c>).</summary>
    public required string BundleId { get; init; }

    public string? Barcode { get; init; }

    public required string BundleName { get; init; }

    public required BundleType BundleType { get; init; }

    public ProductBundleStatus Status { get; init; } = ProductBundleStatus.Active;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
