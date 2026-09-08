// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Customers;

/// <summary>
/// Maps to <c>erasure_ledger</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>ErasureLedgerEntryConfiguration</c>.
/// </para>
/// </summary>
public sealed class ErasureLedgerEntry
{
    /// <summary>Primary key (<c>erasure_id</c>).</summary>
    public required string ErasureId { get; init; }

    public required ErasureLedgerEntrySubjectType SubjectType { get; init; }

    public required string SubjectId { get; init; }

    public string? RequestId { get; init; }

    public required DateTimeOffset RequestedAt { get; init; }

    public DateTimeOffset? ExecutedAt { get; init; }

    public string? ExecutedBy { get; init; }

    public string? ScopeJson { get; init; }

    public ErasureLedgerEntryStatus Status { get; init; } = ErasureLedgerEntryStatus.Pending;

    public string? BlockedReason { get; init; }

    public DateTimeOffset? CloudConfirmedAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
