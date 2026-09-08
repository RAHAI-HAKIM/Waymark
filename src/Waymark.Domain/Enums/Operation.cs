// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'collection', 'consultation', 'disclosure', 'transmission', 'modification', 'erasure', 'pseudonymisation', 're_identification'.
/// </summary>
public enum Operation
{
    /// <summary>Stored as <c>collection</c>.</summary>
    Collection,

    /// <summary>Stored as <c>consultation</c>.</summary>
    Consultation,

    /// <summary>Stored as <c>disclosure</c>.</summary>
    Disclosure,

    /// <summary>Stored as <c>transmission</c>.</summary>
    Transmission,

    /// <summary>Stored as <c>modification</c>.</summary>
    Modification,

    /// <summary>Stored as <c>erasure</c>.</summary>
    Erasure,

    /// <summary>Stored as <c>pseudonymisation</c>.</summary>
    Pseudonymisation,

    /// <summary>Stored as <c>re_identification</c>.</summary>
    ReIdentification
}
