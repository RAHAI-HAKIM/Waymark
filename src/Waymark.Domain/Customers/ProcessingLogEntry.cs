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

    public string? SubjectId { get; init; }

    public required ActorType ActorType { get; init; }

    public string? ActorId { get; init; }

    public required string Purpose { get; init; }

    public string? Recipient { get; init; }

    public required string SourceModule { get; init; }

    public string? StoreId { get; init; }

    public string? TerminalId { get; init; }
}
