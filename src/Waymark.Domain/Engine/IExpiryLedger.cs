namespace Waymark.Domain.Engine;

/// <summary>
/// A parameter the engine fitted (or a cold-start placeholder standing in for one), with the
/// version that produced it. The version is stamped on every card the parameter decided, so a
/// card can always be traced back to the number it was judged against.
/// </summary>
public readonly record struct EngineParameter(long Value, long Version);

/// <summary>A card already on the board for a subject, and which card it is.</summary>
public readonly record struct LiveRecommendation(string RecommendationId, string SubjectId);

/// <summary>
/// What the expiry evaluator reads, and the one thing it changes that it did not write
/// (hop 6). Implemented in Persistence; every read is scoped to the current store by the
/// global filter, so nothing here takes a store id (CLAUDE.md §3.3).
/// </summary>
public interface IExpiryLedger
{
    /// <summary>
    /// The current near-expiry window, in days. Null when the registry holds none: the
    /// evaluator then refuses rather than assuming a number, because a window nobody chose
    /// would flag either everything or nothing and look deliberate either way (D-037).
    /// </summary>
    Task<EngineParameter?> NearExpiryWindowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every active batch in this store that still has stock, with what each of its variants
    /// has left and what it cost. Batches with no expiry date are included; the rule drops
    /// them, and a port that pre-filtered would hide half of what it judged.
    /// </summary>
    Task<IReadOnlyList<BatchStock>> BatchesWithStockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The cards of <paramref name="recommendationType"/> still waiting for a human — pending
    /// or delivered. What a re-run matches on, so it updates the board rather than doubling it.
    /// </summary>
    Task<IReadOnlyList<LiveRecommendation>> LiveRecommendationsAsync(
        string recommendationType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Which role a card is addressed to: <b>the lowest rung above the shop floor</b>.
    ///
    /// <para>
    /// D-069 puts it as "a manager may decide an inventory card, a cashier is refused", but
    /// <c>manager</c> is a role code one store happens to use and another does not — a card
    /// naming a role its store has never heard of is a foreign key failure at best and an
    /// invisible card at worst. So it is read off <c>roles.rank</c>, which is what the check
    /// in hop 7 compares anyway: the lowest rank strictly above the lowest one. In a shop with
    /// cashier, manager and owner that is the manager; in a shop with only a cashier and an
    /// owner it is the owner, who is the manager there.
    /// </para>
    /// </summary>
    /// <returns>The role code, or null when the store has no roles at all.</returns>
    Task<string?> DecidingRoleAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a card's move to <c>superseded</c>, written at commit with the card that
    /// replaces it. A decided card is never touched: the human's answer stands.
    /// </summary>
    void Supersede(string recommendationId);
}
