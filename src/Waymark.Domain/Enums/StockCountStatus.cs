// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'draft', 'counting', 'review', 'posted', 'cancelled'.
/// </summary>
public enum StockCountStatus
{
    /// <summary>Stored as <c>draft</c>.</summary>
    Draft,

    /// <summary>Stored as <c>counting</c>.</summary>
    Counting,

    /// <summary>Stored as <c>review</c>.</summary>
    Review,

    /// <summary>Stored as <c>posted</c>.</summary>
    Posted,

    /// <summary>Stored as <c>cancelled</c>.</summary>
    Cancelled
}
