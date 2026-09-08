// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'open', 'parked', 'completed', 'voided', 'refunded', 'partially_refunded'.
/// </summary>
public enum TransactionStatus
{
    /// <summary>Stored as <c>open</c>.</summary>
    Open,

    /// <summary>Stored as <c>parked</c>.</summary>
    Parked,

    /// <summary>Stored as <c>completed</c>.</summary>
    Completed,

    /// <summary>Stored as <c>voided</c>.</summary>
    Voided,

    /// <summary>Stored as <c>refunded</c>.</summary>
    Refunded,

    /// <summary>Stored as <c>partially_refunded</c>.</summary>
    PartiallyRefunded
}
