namespace Waymark.Domain.Enums;

/// <summary>
/// The lawful basis an operation relied on, recorded per log row.
///
/// <para>
/// <b>Deliberately not <see cref="LegalBasis"/>, which has four members.</b>
/// D-045 requires a fifth, <c>vital_interest</c>, and
/// <c>customers.legal_basis</c> carries
/// <c>CHECK (legal_basis IN ('consent','contract','legal_obligation','legitimate_interest'))</c>.
/// Adding the member to the shared enum would let code produce a value that
/// table rejects at write time; widening its CHECK means a rebuild of
/// <c>customers</c>, which D-045 avoids everywhere else for the reasons in
/// D-022. Two enums, each matching the constraint its own column carries — the
/// same convention that already keeps <c>StoreStatus</c>, <c>SupplierStatus</c>
/// and <c>CustomerStatus</c> apart.
/// </para>
/// <para>
/// No CHECK on <c>processing_log.legal_basis</c> either, for the same reason as
/// <see cref="ProcessingPurpose"/>: the converter is the validation.
/// </para>
/// </summary>
public enum ProcessingLegalBasis
{
    /// <summary>Stored as <c>consent</c>.</summary>
    Consent,

    /// <summary>Stored as <c>contract</c>.</summary>
    Contract,

    /// <summary>Stored as <c>legal_obligation</c>.</summary>
    LegalObligation,

    /// <summary>Stored as <c>legitimate_interest</c>.</summary>
    LegitimateInterest,

    /// <summary>
    /// Stored as <c>vital_interest</c>. Not representable in
    /// <see cref="LegalBasis"/>, which is why this enum exists.
    /// </summary>
    VitalInterest,
}
