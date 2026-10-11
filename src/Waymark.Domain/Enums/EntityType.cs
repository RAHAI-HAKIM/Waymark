// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'transaction', 'customer', 'staff', 'processing_log', 'consent_event', 'recommendation', 'stock_movement', 'receivable', 'supplier_contact', 'data_subject_request', 'tier2_record'.
/// </summary>
public enum EntityType
{
    /// <summary>Stored as <c>transaction</c>.</summary>
    Transaction,

    /// <summary>Stored as <c>customer</c>.</summary>
    Customer,

    /// <summary>Stored as <c>staff</c>.</summary>
    Staff,

    /// <summary>Stored as <c>processing_log</c>.</summary>
    ProcessingLog,

    /// <summary>Stored as <c>consent_event</c>.</summary>
    ConsentEvent,

    /// <summary>Stored as <c>recommendation</c>.</summary>
    Recommendation,

    /// <summary>Stored as <c>stock_movement</c>.</summary>
    StockMovement,

    /// <summary>Stored as <c>receivable</c> (D-115).</summary>
    Receivable,

    /// <summary>Stored as <c>supplier_contact</c> (D-115).</summary>
    SupplierContact,

    /// <summary>Stored as <c>data_subject_request</c> (D-115).</summary>
    DataSubjectRequest,

    /// <summary>Stored as <c>tier2_record</c> (D-115).</summary>
    Tier2Record
}
