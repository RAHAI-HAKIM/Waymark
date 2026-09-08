// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Catalogue;

/// <summary>
/// Maps to <c>attribute_definitions</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>AttributeDefinitionConfiguration</c>.
/// </para>
/// </summary>
public sealed class AttributeDefinition
{
    /// <summary>Primary key (<c>attribute_code</c>).</summary>
    public required string AttributeCode { get; init; }

    public required string LabelAr { get; init; }

    public required string LabelFr { get; init; }

    public string? LabelEn { get; init; }

    public required AttributeDefinitionDataType DataType { get; init; }

    public string? UnitCode { get; init; }

    public required AttributeDefinitionAppliesTo AppliesTo { get; init; }

    public bool IsGroupable { get; init; }

    public bool IsFilterable { get; init; }

    public string? HelpText { get; init; }

    public AttributeDefinitionStatus Status { get; init; } = AttributeDefinitionStatus.Active;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
