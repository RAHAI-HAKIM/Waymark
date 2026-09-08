// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'delete', 'unlink', 'archive'.
/// </summary>
public enum ActionOnExpiry
{
    /// <summary>Stored as <c>delete</c>.</summary>
    Delete,

    /// <summary>Stored as <c>unlink</c>.</summary>
    Unlink,

    /// <summary>Stored as <c>archive</c>.</summary>
    Archive
}
