// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'receipt', 'sale', 'return_in', 'return_out', 'adjustment', 'count', 'write_off', 'expiry', 'transfer'.
/// </summary>
public enum StockMovementType
{
    /// <summary>Stored as <c>receipt</c>.</summary>
    Receipt,

    /// <summary>Stored as <c>sale</c>.</summary>
    Sale,

    /// <summary>Stored as <c>return_in</c>.</summary>
    ReturnIn,

    /// <summary>Stored as <c>return_out</c>.</summary>
    ReturnOut,

    /// <summary>Stored as <c>adjustment</c>.</summary>
    Adjustment,

    /// <summary>Stored as <c>count</c>.</summary>
    Count,

    /// <summary>Stored as <c>write_off</c>.</summary>
    WriteOff,

    /// <summary>Stored as <c>expiry</c>.</summary>
    Expiry,

    /// <summary>Stored as <c>transfer</c>.</summary>
    Transfer
}
