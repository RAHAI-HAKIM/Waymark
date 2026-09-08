// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'quiet', 'standard', 'warning', 'critical'.
/// </summary>
public enum Urgency
{
    /// <summary>Stored as <c>quiet</c>.</summary>
    Quiet,

    /// <summary>Stored as <c>standard</c>.</summary>
    Standard,

    /// <summary>Stored as <c>warning</c>.</summary>
    Warning,

    /// <summary>Stored as <c>critical</c>.</summary>
    Critical
}
