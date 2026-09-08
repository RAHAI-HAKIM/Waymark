// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'accept', 'adjust', 'dismiss', 'snooze'.
/// </summary>
public enum Decision
{
    /// <summary>Stored as <c>accept</c>.</summary>
    Accept,

    /// <summary>Stored as <c>adjust</c>.</summary>
    Adjust,

    /// <summary>Stored as <c>dismiss</c>.</summary>
    Dismiss,

    /// <summary>Stored as <c>snooze</c>.</summary>
    Snooze
}
