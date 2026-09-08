// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'active', 'inactive', 'retired'.
/// </summary>
public enum TerminalStatus
{
    /// <summary>Stored as <c>active</c>.</summary>
    Active,

    /// <summary>Stored as <c>inactive</c>.</summary>
    Inactive,

    /// <summary>Stored as <c>retired</c>.</summary>
    Retired
}
