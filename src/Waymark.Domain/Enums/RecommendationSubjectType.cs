// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'variant', 'batch', 'product', 'supplier', 'customer', 'store'.
/// </summary>
public enum RecommendationSubjectType
{
    /// <summary>Stored as <c>variant</c>.</summary>
    Variant,

    /// <summary>Stored as <c>batch</c>.</summary>
    Batch,

    /// <summary>Stored as <c>product</c>.</summary>
    Product,

    /// <summary>Stored as <c>supplier</c>.</summary>
    Supplier,

    /// <summary>Stored as <c>customer</c>.</summary>
    Customer,

    /// <summary>Stored as <c>store</c>.</summary>
    Store
}
