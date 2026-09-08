// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'pending', 'delivered', 'decided', 'expired', 'superseded'.
/// </summary>
public enum RecommendationStatus
{
    /// <summary>Stored as <c>pending</c>.</summary>
    Pending,

    /// <summary>Stored as <c>delivered</c>.</summary>
    Delivered,

    /// <summary>Stored as <c>decided</c>.</summary>
    Decided,

    /// <summary>Stored as <c>expired</c>.</summary>
    Expired,

    /// <summary>Stored as <c>superseded</c>.</summary>
    Superseded
}
