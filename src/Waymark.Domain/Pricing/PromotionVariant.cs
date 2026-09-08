// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Pricing;

/// <summary>
/// Maps to <c>promotion_variant</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>PromotionVariantConfiguration</c>.
/// </para>
/// </summary>
public sealed class PromotionVariant
{
    /// <summary>Part of the primary key (<c>promotion_id</c>).</summary>
    public required string PromotionId { get; init; }

    /// <summary>Part of the primary key (<c>variant_id</c>).</summary>
    public required string VariantId { get; init; }

    /// <summary>Part of the primary key (<c>valid_from</c>).</summary>
    public required string ValidFrom { get; init; }

    public string? ValidTo { get; init; }

    public required PromotionVariantValueType ValueType { get; init; }

    public required long PromotionValue { get; init; }

    public long MinQuantity { get; init; }

    public long? MaxRedemptions { get; init; }

    public long RedemptionCount { get; init; }

    public long Priority { get; init; } = 100L;

    public bool IsStackable { get; init; }

    public string? Description { get; init; }

    public PromotionVariantStatus Status { get; init; } = PromotionVariantStatus.Active;
}
