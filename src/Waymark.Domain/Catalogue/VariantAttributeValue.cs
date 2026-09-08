// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Catalogue;

/// <summary>
/// Maps to <c>variant_attribute_values</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>VariantAttributeValueConfiguration</c>.
/// </para>
/// </summary>
public sealed class VariantAttributeValue
{
    /// <summary>Part of the primary key (<c>variant_id</c>).</summary>
    public required string VariantId { get; init; }

    /// <summary>Part of the primary key (<c>attribute_code</c>).</summary>
    public required string AttributeCode { get; init; }

    public string? ValueText { get; init; }

    public long? ValueNumber { get; init; }

    public long? ValueBool { get; init; }

    public DateOnly? ValueDate { get; init; }

    public string? OptionCode { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
