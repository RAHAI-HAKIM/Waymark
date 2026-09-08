// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'text', 'number', 'bool', 'date', 'enum'.
/// </summary>
public enum AttributeDefinitionDataType
{
    /// <summary>Stored as <c>text</c>.</summary>
    Text,

    /// <summary>Stored as <c>number</c>.</summary>
    Number,

    /// <summary>Stored as <c>bool</c>.</summary>
    Bool,

    /// <summary>Stored as <c>date</c>.</summary>
    Date,

    /// <summary>Stored as <c>enum</c>.</summary>
    Enum
}
