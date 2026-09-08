// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'consent', 'contract', 'legal_obligation', 'legitimate_interest'.
/// </summary>
public enum LegalBasis
{
    /// <summary>Stored as <c>consent</c>.</summary>
    Consent,

    /// <summary>Stored as <c>contract</c>.</summary>
    Contract,

    /// <summary>Stored as <c>legal_obligation</c>.</summary>
    LegalObligation,

    /// <summary>Stored as <c>legitimate_interest</c>.</summary>
    LegitimateInterest
}
