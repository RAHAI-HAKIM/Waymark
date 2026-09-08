// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'text', 'integer', 'money', 'percent', 'bool', 'date'.
/// </summary>
public enum SystemConfigEntryDataType
{
    /// <summary>Stored as <c>text</c>.</summary>
    Text,

    /// <summary>Stored as <c>integer</c>.</summary>
    Integer,

    /// <summary>Stored as <c>money</c>.</summary>
    Money,

    /// <summary>Stored as <c>percent</c>.</summary>
    Percent,

    /// <summary>Stored as <c>bool</c>.</summary>
    Bool,

    /// <summary>Stored as <c>date</c>.</summary>
    Date
}
