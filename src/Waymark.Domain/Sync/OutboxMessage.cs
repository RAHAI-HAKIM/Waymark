// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Sync;

/// <summary>
/// Maps to <c>outbox</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>OutboxMessageConfiguration</c>.
/// </para>
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Primary key (<c>outbox_id</c>).</summary>
    public required string OutboxId { get; init; }

    public required long SequenceNumber { get; init; }

    public required OutboxMessageChannel Channel { get; init; }

    public required string MessageType { get; init; }

    public string? EntityType { get; init; }

    public string? EntityId { get; init; }

    public required string PayloadJson { get; init; }

    public bool IsPriority { get; init; }

    public long Attempts { get; init; }

    public DateTimeOffset? LastAttemptAt { get; init; }

    public string? LastError { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
