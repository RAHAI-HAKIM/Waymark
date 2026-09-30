// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'processing', 'marketing', 'staff'.
/// </summary>
public enum NoticeType
{
    /// <summary>Stored as <c>processing</c>.</summary>
    Processing,

    /// <summary>Stored as <c>marketing</c>.</summary>
    Marketing,

    /// <summary>Stored as <c>staff</c>.</summary>
    Staff,

    /// <summary>
    /// Stored as <c>information</c>: what a customer is told when their data is collected (Art. 32),
    /// whatever the legal basis; the tab's is a contract (DPIA P6), so it asks no consent (B7).
    /// </summary>
    Information
}
