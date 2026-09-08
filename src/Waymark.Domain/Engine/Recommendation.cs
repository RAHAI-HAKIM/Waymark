// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Engine;

/// <summary>
/// Maps to <c>recommendations</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>RecommendationConfiguration</c>.
/// </para>
/// </summary>
public sealed class Recommendation
{
    /// <summary>Primary key (<c>recommendation_id</c>).</summary>
    public required string RecommendationId { get; init; }

    public required string StoreId { get; init; }

    public required Department Department { get; init; }

    public required Urgency Urgency { get; init; }

    public required ActionType ActionType { get; init; }

    public required RecommendationSubjectType SubjectType { get; init; }

    public required string SubjectId { get; init; }

    public required string Headline { get; init; }

    public required string BecauseJson { get; init; }

    public long? IntervalLow { get; init; }

    public long? IntervalHigh { get; init; }

    public required DateTimeOffset ComputedAt { get; init; }

    public long? ParameterVersion { get; init; }

    public required string Source { get; init; }

    public required string MinimumRequiredRole { get; init; }

    public RecommendationStatus Status { get; init; } = RecommendationStatus.Pending;

    public required DateTimeOffset IssuedAt { get; init; }

    public DateTimeOffset? DeliveredAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
}
