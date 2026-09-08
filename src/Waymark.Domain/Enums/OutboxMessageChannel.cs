// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'A_statistics', 'B_operational', 'D_decisions'.
/// </summary>
public enum OutboxMessageChannel
{
    /// <summary>Stored as <c>A_statistics</c>.</summary>
    AStatistics,

    /// <summary>Stored as <c>B_operational</c>.</summary>
    BOperational,

    /// <summary>Stored as <c>D_decisions</c>.</summary>
    DDecisions
}
