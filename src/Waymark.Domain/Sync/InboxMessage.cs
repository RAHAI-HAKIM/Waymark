// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Sync;

/// <summary>
/// Maps to <c>inbox</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>InboxMessageConfiguration</c>.
/// </para>
/// </summary>
public sealed class InboxMessage
{
    /// <summary>Primary key (<c>inbox_id</c>).</summary>
    public required string InboxId { get; init; }

    public required string MessageId { get; init; }

    public required long CloudSequence { get; init; }

    public required InboxMessageChannel Channel { get; init; }

    public required string MessageType { get; init; }

    public required string PayloadJson { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }

    public DateTimeOffset? AppliedAt { get; init; }

    public InboxMessageStatus Status { get; init; } = InboxMessageStatus.Pending;

    public string? RejectionReason { get; init; }
}
