// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'collection', 'consultation', 'disclosure', 'transmission', 'modification', 'erasure', 'pseudonymisation', 're_identification', 'recording', 'organisation', 'storage', 'extraction', 'use', 'dissemination', 'alignment', 'interconnection', 'locking', 'encryption', 'destruction'.
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
    ReIdentification,

    /// <summary>Stored as <c>recording</c> (D-115).</summary>
    Recording,

    /// <summary>Stored as <c>organisation</c> (D-115).</summary>
    Organisation,

    /// <summary>Stored as <c>storage</c> (D-115).</summary>
    Storage,

    /// <summary>Stored as <c>extraction</c> (D-115).</summary>
    Extraction,

    /// <summary>Stored as <c>use</c> (D-115).</summary>
    Use,

    /// <summary>Stored as <c>dissemination</c> (D-115).</summary>
    Dissemination,

    /// <summary>Stored as <c>alignment</c> (D-115).</summary>
    Alignment,

    /// <summary>Stored as <c>interconnection</c> (D-115).</summary>
    Interconnection,

    /// <summary>Stored as <c>locking</c> (D-115).</summary>
    Locking,

    /// <summary>Stored as <c>encryption</c> (D-115).</summary>
    Encryption,

    /// <summary>Stored as <c>destruction</c> (D-115).</summary>
    Destruction
}
