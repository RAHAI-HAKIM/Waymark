// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'inventory', 'sales_demand', 'supply', 'planning', 'customer'.
/// </summary>
public enum Department
{
    /// <summary>Stored as <c>inventory</c>.</summary>
    Inventory,

    /// <summary>Stored as <c>sales_demand</c>.</summary>
    SalesDemand,

    /// <summary>Stored as <c>supply</c>.</summary>
    Supply,

    /// <summary>Stored as <c>planning</c>.</summary>
    Planning,

    /// <summary>Stored as <c>customer</c>.</summary>
    Customer
}
