// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'paid_in', 'paid_out', 'drop', 'float_add', 'float_remove'.
/// </summary>
public enum CashMovementType
{
    /// <summary>Stored as <c>paid_in</c>.</summary>
    PaidIn,

    /// <summary>Stored as <c>paid_out</c>.</summary>
    PaidOut,

    /// <summary>Stored as <c>drop</c>.</summary>
    Drop,

    /// <summary>Stored as <c>float_add</c>.</summary>
    FloatAdd,

    /// <summary>Stored as <c>float_remove</c>.</summary>
    FloatRemove
}
