// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

using Waymark.Domain;

namespace Waymark.Domain.Customers;

/// <summary>
/// Maps to <c>processing_log</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>ProcessingLogEntryConfiguration</c>.
/// </para>
/// </summary>
public sealed class ProcessingLogEntry : IStoreScoped
{
    /// <summary>Primary key (<c>log_id</c>).</summary>
    public required string LogId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required Operation Operation { get; init; }

    public required ProcessingLogEntrySubjectType SubjectType { get; init; }

    /// <summary>
    /// A pseudonym, always — never a direct identifier, for any
    /// <c>subject_type</c> including staff, each under its own
    /// domain-separation prefix (D-039 condition 3). Nothing needs purging at
    /// erasure because nothing identifying was ever written, and destroying the
    /// tenant key unlinks the whole log at once.
    ///
    /// <para>
    /// Still nullable, and that is not a gap: a retention sweep or a system
    /// operation has no subject. What guarantees the column never holds a name
    /// is the type at the boundary — <c>IProcessingLog.Record</c> takes a
    /// <see cref="Waymark.Domain.Privacy.Pseudonym"/>, which cannot be built
    /// outside <c>Waymark.Pseudonymisation</c> — not the nullability.
    /// </para>
    /// </summary>
    public string? SubjectId { get; init; }

    public required ActorType ActorType { get; init; }

    public string? ActorId { get; init; }

    /// <summary>
    /// Why the operation happened. A closed enum validated by the converter
    /// rather than a CHECK on the column, so adding a purpose is not a rebuild
    /// (decisions.md D-045).
    /// </summary>
    public required ProcessingPurpose Purpose { get; init; }

    /// <summary>
    /// What made the operation lawful — added by D-045.
    ///
    /// <para>
    /// Its own enum rather than <see cref="LegalBasis"/>, which cannot express
    /// <c>vital_interest</c> without widening a CHECK on <c>customers</c>.
    /// </para>
    /// </summary>
    public required ProcessingLegalBasis LegalBasis { get; init; }

    public string? Recipient { get; init; }

    public required string SourceModule { get; init; }

    public string? StoreId { get; init; }

    public string? TerminalId { get; init; }
}
