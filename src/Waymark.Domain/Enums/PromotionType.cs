// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'discount', 'bogo', 'bundle', 'markdown'.
/// </summary>
public enum PromotionType
{
    /// <summary>Stored as <c>discount</c>.</summary>
    Discount,

    /// <summary>Stored as <c>bogo</c>.</summary>
    Bogo,

    /// <summary>Stored as <c>bundle</c>.</summary>
    Bundle,

    /// <summary>Stored as <c>markdown</c>.</summary>
    Markdown
}
