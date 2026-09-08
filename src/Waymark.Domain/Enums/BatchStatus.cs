// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'active', 'depleted', 'written_off', 'quarantined'.
/// </summary>
public enum BatchStatus
{
    /// <summary>Stored as <c>active</c>.</summary>
    Active,

    /// <summary>Stored as <c>depleted</c>.</summary>
    Depleted,

    /// <summary>Stored as <c>written_off</c>.</summary>
    WrittenOff,

    /// <summary>Stored as <c>quarantined</c>.</summary>
    Quarantined
}
