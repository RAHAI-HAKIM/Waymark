// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'pending', 'applied', 'rejected_stale', 'rejected_invalid'.
/// </summary>
public enum IntentStatus
{
    /// <summary>Stored as <c>pending</c>.</summary>
    Pending,

    /// <summary>Stored as <c>applied</c>.</summary>
    Applied,

    /// <summary>Stored as <c>rejected_stale</c>.</summary>
    RejectedStale,

    /// <summary>Stored as <c>rejected_invalid</c>.</summary>
    RejectedInvalid
}
