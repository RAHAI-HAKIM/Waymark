// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Pricing;

/// <summary>
/// Maps to <c>prices</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>PriceConfiguration</c>.
/// </para>
/// </summary>
public sealed class Price
{
    /// <summary>Part of the primary key (<c>variant_id</c>).</summary>
    public required string VariantId { get; init; }

    /// <summary>Part of the primary key (<c>valid_from</c>).</summary>
    public required string ValidFrom { get; init; }

    /// <summary>Part of the primary key (<c>store_id</c>).</summary>
    public required string StoreId { get; init; }

    /// <summary>Part of the primary key (<c>price_type</c>).</summary>
    public PriceType PriceType { get; init; } = PriceType.Retail;

    public required long PriceValue { get; init; }

    public string Currency { get; init; } = "DZD";

    public bool IsTaxInclusive { get; init; } = true;

    public string? ValidTo { get; init; }

    public string? CreatedBy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
