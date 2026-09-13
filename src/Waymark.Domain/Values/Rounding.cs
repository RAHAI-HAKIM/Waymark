namespace Waymark.Domain.Values;

/// <summary>
/// What to do with a fraction of a minor unit.
///
/// <para>
/// Two values, both the retailer's choice, stored on <c>stores.rounding_policy</c>
/// and stamped on <c>transactions.rounding_policy</c> so a receipt stays
/// recomputable from its own row after the policy changes (decisions.md D-032).
/// </para>
/// <para>
/// <b>Truncation is deliberately absent.</b> It is biased downward on every
/// single line without exception, so the drift accumulates in one direction and
/// never cancels, and it is the hardest of the three to defend to an inspector.
/// Where truncation is semantically correct — "how many whole packs fit" — that
/// is integer arithmetic on a count, not a money policy.
/// </para>
/// <para>
/// Almanac never takes this as a parameter. The engine always uses
/// <see cref="HalfEven"/> and stores at full precision, which is what keeps the
/// retailer's presentation choice out of the statistics.
/// </para>
/// </summary>
public enum Rounding
{
    /// <summary>
    /// Banker's rounding: an exact half goes to the even neighbour. Reduces
    /// directional drift, and is the default everywhere the choice is ours
    /// rather than the retailer's.
    ///
    /// <para>
    /// It does <i>not</i> make the drift zero. That property assumes the
    /// discarded fractions are uniformly distributed, and retail prices cluster
    /// on .00, .50, .90 and .99 — 19% TVA on a price ending in .99 produces a
    /// strongly biased set of half-centimes. This is why the variance ledger
    /// exists under either policy (D-032).
    /// </para>
    /// </summary>
    HalfEven = 0,

    /// <summary>
    /// Commercial rounding: an exact half goes away from zero. What a person
    /// expects when told "0.5 rounds up", and what most fiscal software does.
    /// </summary>
    HalfUp = 1,
}
