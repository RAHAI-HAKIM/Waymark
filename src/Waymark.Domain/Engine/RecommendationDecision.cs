// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Engine;

/// <summary>
/// Maps to <c>recommendation_decisions</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>RecommendationDecisionConfiguration</c>.
/// </para>
/// </summary>
public sealed class RecommendationDecision
{
    /// <summary>Primary key (<c>decision_id</c>).</summary>
    public required string DecisionId { get; init; }

    public required string RecommendationId { get; init; }

    public required Decision Decision { get; init; }

    public string? ChosenOptionId { get; init; }

    public string? AdjustedPayloadJson { get; init; }

    public string? SnoozeUntil { get; init; }

    public required Origin Origin { get; init; }

    public string? DecidedBy { get; init; }

    public required DateTimeOffset DecidedAt { get; init; }

    public string? TerminalId { get; init; }

    public DateTimeOffset? AppliedAt { get; init; }

    public string? ResultingEntityType { get; init; }

    public string? ResultingEntityId { get; init; }
}
