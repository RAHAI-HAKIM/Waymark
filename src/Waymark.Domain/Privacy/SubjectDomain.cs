namespace Waymark.Domain.Privacy;

/// <summary>
/// Which population a subject belongs to — the domain-separation namespace a
/// pseudonym is computed under (decisions.md D-039, condition 3).
///
/// <para>
/// <b>This is not cosmetic.</b> Without it, a staff member whose id happened to
/// equal a customer's would share that customer's pseudonym, and the cloud would
/// silently merge two people. The prefix costs nothing now and cannot be added
/// later: changing the input to the hash changes every pseudonym ever computed,
/// which for a non-rotating scheme means a new epoch for the whole tenant.
/// </para>
/// <para>
/// The prefixes themselves live in <c>Waymark.Pseudonymisation</c> with the rest
/// of the scheme. This enum only says which populations exist, so an unlisted
/// one cannot be pseudonymised by accident.
/// </para>
/// </summary>
public enum SubjectDomain
{
    /// <summary>A customer. The case the scheme was designed for.</summary>
    Customer,

    /// <summary>
    /// A staff member. <c>processing_log</c> names staff subjects this way too,
    /// so the log holds no direct identifier for anybody (D-045).
    /// </summary>
    Staff,

    /// <summary>
    /// A supplier. Named by D-039 as one of the populations the prefix exists to
    /// keep apart, and a supplier's contact is a natural person under the Law.
    /// </summary>
    Supplier,
}
