// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'open', 'in_progress', 'fulfilled', 'refused', 'blocked'.
/// </summary>
public enum DataSubjectRequestStatus
{
    /// <summary>Stored as <c>open</c>.</summary>
    Open,

    /// <summary>Stored as <c>in_progress</c>.</summary>
    InProgress,

    /// <summary>Stored as <c>fulfilled</c>.</summary>
    Fulfilled,

    /// <summary>Stored as <c>refused</c>.</summary>
    Refused,

    /// <summary>Stored as <c>blocked</c>.</summary>
    Blocked
}
