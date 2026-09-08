// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'manual', 'recommendation', 'reorder_rule'.
/// </summary>
public enum PurchaseOrderSource
{
    /// <summary>Stored as <c>manual</c>.</summary>
    Manual,

    /// <summary>Stored as <c>recommendation</c>.</summary>
    Recommendation,

    /// <summary>Stored as <c>reorder_rule</c>.</summary>
    ReorderRule
}
