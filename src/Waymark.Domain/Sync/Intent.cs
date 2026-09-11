// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

using Waymark.Domain;

namespace Waymark.Domain.Sync;

/// <summary>
/// Maps to <c>intents</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>IntentConfiguration</c>.
/// </para>
/// </summary>
public sealed class Intent : IStoreScoped
{
    /// <summary>Primary key (<c>intent_id</c>).</summary>
    public required string IntentId { get; init; }

    public required IntentType IntentType { get; init; }

    public required string StoreId { get; init; }

    public required string PayloadJson { get; init; }

    public required string PreconditionsJson { get; init; }

    public required string CreatedAtCloud { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }

    public DateTimeOffset? EvaluatedAt { get; init; }

    public IntentStatus Status { get; init; } = IntentStatus.Pending;

    public string? RejectionReason { get; init; }

    public string? FreshRequestId { get; init; }

    public string? DecidedByCloudUser { get; init; }
}
