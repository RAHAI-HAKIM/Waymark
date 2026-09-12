namespace Waymark.Domain.Enums;

/// <summary>
/// Why a personal-data operation happened — the "reason" Loi 25-11 article
/// 41 bis 3 requires the logbook to trace.
///
/// <para>
/// A closed enum validated by the log helper rather than a CHECK constraint on
/// the column. Amending a CHECK means a table rebuild, and a rebuild silently
/// drops triggers, indexes and other CHECKs (decisions.md D-022). The enum
/// converter throws in both directions on a value it does not know, which is
/// the same guarantee without the rebuild (D-045).
/// </para>
/// <para>
/// Adding a member is cheap and needs no migration. Removing one is not: rows
/// carrying it become unreadable, because the converter refuses rather than
/// guessing.
/// </para>
/// </summary>
public enum ProcessingPurpose
{
    /// <summary>A sale at the till. Stored as <c>pos_sale</c>.</summary>
    PosSale,

    /// <summary>Reading a loyalty balance or tier. Stored as <c>loyalty_lookup</c>.</summary>
    LoyaltyLookup,

    /// <summary>Store credit granted, drawn down or reconciled. Stored as <c>credit_management</c>.</summary>
    CreditManagement,

    /// <summary>A staff member acting on a customer's behalf. Stored as <c>customer_service</c>.</summary>
    CustomerService,

    /// <summary>The tier 1 to tier 2 transform. Stored as <c>analytics_pseudonymised</c>.</summary>
    AnalyticsPseudonymised,

    /// <summary>Accounting, tax or another statutory duty. Stored as <c>legal_obligation</c>.</summary>
    LegalObligation,

    /// <summary>Access, rectification, objection or erasure. Stored as <c>data_subject_request</c>.</summary>
    DataSubjectRequest,

    /// <summary>An automated purge under a retention policy. Stored as <c>retention_expiry</c>.</summary>
    RetentionExpiry,
}
