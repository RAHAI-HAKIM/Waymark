// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'fixed', 'mix_and_match'.
/// </summary>
public enum BundleType
{
    /// <summary>Stored as <c>fixed</c>.</summary>
    Fixed,

    /// <summary>Stored as <c>mix_and_match</c>.</summary>
    MixAndMatch
}
