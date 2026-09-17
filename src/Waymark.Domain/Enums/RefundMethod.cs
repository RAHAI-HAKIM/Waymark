// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'cash', 'card', 'store_credit', 'exchange', 'on_account'.
/// </summary>
public enum RefundMethod
{
    /// <summary>Stored as <c>cash</c>.</summary>
    Cash,

    /// <summary>Stored as <c>card</c>.</summary>
    Card,

    /// <summary>Stored as <c>store_credit</c>.</summary>
    StoreCredit,

    /// <summary>Stored as <c>exchange</c>.</summary>
    Exchange,

    /// <summary>Stored as <c>on_account</c>. Credits the customer's tab rather than paying out (F-16).</summary>
    OnAccount
}
