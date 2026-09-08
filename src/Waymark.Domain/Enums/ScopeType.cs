// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'global', 'store', 'category', 'variant', 'supplier'.
/// </summary>
public enum ScopeType
{
    /// <summary>Stored as <c>global</c>.</summary>
    Global,

    /// <summary>Stored as <c>store</c>.</summary>
    Store,

    /// <summary>Stored as <c>category</c>.</summary>
    Category,

    /// <summary>Stored as <c>variant</c>.</summary>
    Variant,

    /// <summary>Stored as <c>supplier</c>.</summary>
    Supplier
}
