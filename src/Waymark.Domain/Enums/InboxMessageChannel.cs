// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'C_recommendations', 'D_intents', 'E_control', 'F_parameters'.
/// </summary>
public enum InboxMessageChannel
{
    /// <summary>Stored as <c>C_recommendations</c>.</summary>
    CRecommendations,

    /// <summary>Stored as <c>D_intents</c>.</summary>
    DIntents,

    /// <summary>Stored as <c>E_control</c>.</summary>
    EControl,

    /// <summary>Stored as <c>F_parameters</c>.</summary>
    FParameters
}
