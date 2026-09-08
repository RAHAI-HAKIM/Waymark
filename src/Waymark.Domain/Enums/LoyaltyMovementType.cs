// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'earn', 'redeem', 'adjust', 'expire', 'reverse'.
/// </summary>
public enum LoyaltyMovementType
{
    /// <summary>Stored as <c>earn</c>.</summary>
    Earn,

    /// <summary>Stored as <c>redeem</c>.</summary>
    Redeem,

    /// <summary>Stored as <c>adjust</c>.</summary>
    Adjust,

    /// <summary>Stored as <c>expire</c>.</summary>
    Expire,

    /// <summary>Stored as <c>reverse</c>.</summary>
    Reverse
}
