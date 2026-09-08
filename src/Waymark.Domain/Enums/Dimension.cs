// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'count', 'weight', 'volume', 'length'.
/// </summary>
public enum Dimension
{
    /// <summary>Stored as <c>count</c>.</summary>
    Count,

    /// <summary>Stored as <c>weight</c>.</summary>
    Weight,

    /// <summary>Stored as <c>volume</c>.</summary>
    Volume,

    /// <summary>Stored as <c>length</c>.</summary>
    Length
}
