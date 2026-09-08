// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'discount', 'price_override', 'adjustment', 'void', 'return', 'no_sale', 'cash_movement', 'write_off'.
/// </summary>
public enum ReasonCodeAppliesTo
{
    /// <summary>Stored as <c>discount</c>.</summary>
    Discount,

    /// <summary>Stored as <c>price_override</c>.</summary>
    PriceOverride,

    /// <summary>Stored as <c>adjustment</c>.</summary>
    Adjustment,

    /// <summary>Stored as <c>void</c>.</summary>
    Void,

    /// <summary>Stored as <c>return</c>.</summary>
    Return,

    /// <summary>Stored as <c>no_sale</c>.</summary>
    NoSale,

    /// <summary>Stored as <c>cash_movement</c>.</summary>
    CashMovement,

    /// <summary>Stored as <c>write_off</c>.</summary>
    WriteOff
}
