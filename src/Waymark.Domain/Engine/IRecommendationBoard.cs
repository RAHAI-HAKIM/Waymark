namespace Waymark.Domain.Engine;

/// <summary>
/// The person asking, and how senior they are. No login until Phase 1 (D-069): the staff
/// member is named on the request, and this is the store's own record of them.
/// </summary>
/// <param name="StaffId">Their row in <c>staff</c>.</param>
/// <param name="StaffName">For the card's "decided by", when there is a screen to show it on.</param>
/// <param name="RoleCode">Their role.</param>
/// <param name="Rank">That role's rank, which is what <see cref="CardAudience"/> compares.</param>
public readonly record struct StaffOnDuty(string StaffId, string StaffName, string RoleCode, long Rank);

/// <summary>
/// A card as the board shows it: the recommendation, what it offers, and the rank it is
/// addressed to. The rank is resolved here rather than by the caller, because
/// <c>minimum_required_role</c> is a code and the comparison is on ranks.
/// </summary>
public sealed record CardOnTheBoard(
    Recommendation Card,
    long RequiredRank,
    IReadOnlyList<RecommendationOption> Options);

/// <summary>
/// What the recommendation board reads, and the one thing it changes (hop 7). Implemented in
/// Persistence; every read is scoped to the current store by the global filter, so nothing
/// here takes a store id (CLAUDE.md §3.3).
/// </summary>
public interface IRecommendationBoard
{
    /// <summary>
    /// The staff member and their rank, or null when this store has no such active person.
    /// <b>Null is a refusal, never an empty audience</b>: a request naming somebody the store
    /// does not employ is answered, not quietly given the lowest rank.
    /// </summary>
    Task<StaffOnDuty?> StaffAsync(string staffId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every card still waiting for a human — pending or delivered — newest first. Unfiltered
    /// by audience: the filtering is <see cref="CardAudience"/>'s, in one place, and a port
    /// that did it too would be the second copy of the rule.
    /// </summary>
    Task<IReadOnlyList<CardOnTheBoard>> LiveCardsAsync(CancellationToken cancellationToken = default);

    /// <summary>One card by id, whatever state it is in, or null when this store has no such card.</summary>
    Task<CardOnTheBoard?> CardAsync(string recommendationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages the card's move to <c>decided</c>, written at commit with the decision row that
    /// explains it — both or neither.
    /// </summary>
    void MarkDecided(string recommendationId);
}
