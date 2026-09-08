// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'engine', 'cold_start_default', 'manual_override'.
/// </summary>
public enum ParameterRegistryEntrySource
{
    /// <summary>Stored as <c>engine</c>.</summary>
    Engine,

    /// <summary>Stored as <c>cold_start_default</c>.</summary>
    ColdStartDefault,

    /// <summary>Stored as <c>manual_override</c>.</summary>
    ManualOverride
}
