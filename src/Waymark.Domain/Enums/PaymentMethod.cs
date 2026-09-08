// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'cash', 'card', 'mobile_wallet', 'store_credit', 'on_account'.
/// </summary>
public enum PaymentMethod
{
    /// <summary>Stored as <c>cash</c>.</summary>
    Cash,

    /// <summary>Stored as <c>card</c>.</summary>
    Card,

    /// <summary>Stored as <c>mobile_wallet</c>.</summary>
    MobileWallet,

    /// <summary>Stored as <c>store_credit</c>.</summary>
    StoreCredit,

    /// <summary>Stored as <c>on_account</c>.</summary>
    OnAccount
}
