using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Domain.Engine;

/// <summary>What one variant still has on the shelf out of a batch, and what it cost.</summary>
/// <param name="VariantId">The variant. Several may share one batch: a batch is one product's delivery.</param>
/// <param name="OnHand">The level in <c>inventories</c>, in the variant's selling unit.</param>
/// <param name="UnitCost">What one unit cost, from <c>batch_items</c>. Cost, never price: what is at risk is the money already spent.</param>
public readonly record struct BatchStockLine(string VariantId, Quantity OnHand, Money UnitCost);

/// <summary>A batch that still has stock, with everything the comparison needs to judge it.</summary>
/// <param name="BatchId">The subject of the recommendation.</param>
/// <param name="ProductId">What it is a delivery of.</param>
/// <param name="ProductName">For the headline; the reasoning itself is keys and figures.</param>
/// <param name="ExpiresOn">Null for a product with no shelf life, which this rule never flags.</param>
/// <param name="Lines">Its stocked variants.</param>
public sealed record BatchStock(
    string BatchId,
    string ProductId,
    string ProductName,
    DateOnly? ExpiresOn,
    IReadOnlyList<BatchStockLine> Lines);

/// <summary>
/// A batch worth saying something about, with the figures that say why.
/// </summary>
/// <param name="Batch">What was judged.</param>
/// <param name="DaysToExpiry">
/// Negative when the date has passed: the batch is expired and still on the shelf, which is
/// the louder of the two cases.
/// </param>
/// <param name="OnHand">
/// Everything the batch still holds. <b>Null when its variants are stocked in more than one
/// unit</b> — there is no single number then, and inventing one is worse than leaving the
/// figure out (D-036).
/// </param>
/// <param name="ValueAtCost">What that stock cost, summed across the variants.</param>
/// <param name="Urgency">How loudly the card asks to be seen.</param>
public sealed record NearExpiryFinding(
    BatchStock Batch,
    int DaysToExpiry,
    Quantity? OnHand,
    Money ValueAtCost,
    Urgency Urgency);

/// <summary>
/// The near-expiry rule (hop 6). <b>A comparison, not a model</b> (CLAUDE.md §5): it reads a
/// date, subtracts it from today, compares the difference with one window, and adds up what
/// is on the shelf. No fitting, no history, no iteration — the window is the engine's, and
/// the store only holds it up against what it has.
///
/// <para>
/// Pure and in Domain, like <see cref="Inventory.BatchAllocation"/>, so the rule can be
/// argued with in a test that has no database. The reading of the data and the writing of
/// the card are somebody else's job.
/// </para>
/// </summary>
public static class NearExpiry
{
    /// <summary>The window, in <c>parameter_registry</c>. One placeholder for every category in Phase 0.5 (D-069).</summary>
    public const string ParameterCode = "near_expiry_window_days";

    /// <summary>
    /// What kind of suggestion this is (<c>recommendations.recommendation_type</c>, D-044).
    /// With the store, the subject type and the subject it is the dedupe key, so re-running
    /// the evaluator finds the card it wrote last time instead of writing a second one.
    /// </summary>
    public const string RecommendationType = "near_expiry";

    /// <summary>
    /// Rounding for anything the engine derives. Always half-even, everywhere, whatever the
    /// store's own policy is (CLAUDE.md §3.1): a store's policy belongs to what it charges,
    /// and an analytical figure that changed with it would not compare across stores.
    /// </summary>
    public const Rounding EngineRounding = Rounding.HalfEven;

    /// <summary>
    /// Judges one batch against the window, as of <paramref name="today"/> in the store.
    /// </summary>
    /// <returns>
    /// The finding, or <c>null</c> when there is nothing to say: no shelf life, nothing left
    /// on the shelf, or an expiry still further off than the window. <b>A batch that is fine
    /// gets no card at all</b> (CLAUDE.md §6) — there is no positive state to return.
    /// </returns>
    public static NearExpiryFinding? Evaluate(BatchStock batch, int windowDays, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentOutOfRangeException.ThrowIfNegative(windowDays);

        if (batch.ExpiresOn is not { } expires)
        {
            return null;
        }

        if (!batch.Lines.Any(line => line.OnHand.IsPositive))
        {
            return null;
        }

        // Whole days, from the store's own date (D-067): a till an hour ahead of UTC must not
        // read a batch as having one day more than the shopkeeper sees on the carton.
        var daysToExpiry = expires.DayNumber - today.DayNumber;

        if (daysToExpiry > windowDays)
        {
            return null;
        }

        return new NearExpiryFinding(
            batch,
            daysToExpiry,
            TotalOnHand(batch.Lines),
            ValueAtCost(batch.Lines),
            daysToExpiry < 0 ? Urgency.Critical : Urgency.Warning);
    }

    /// <summary>
    /// Everything the batch holds, or null when its variants do not share a selling unit.
    /// Pieces and kilogrammes do not add up, and <see cref="Quantity"/> refuses to pretend
    /// they do (D-036). The card then carries one figure fewer rather than a wrong one.
    /// </summary>
    private static Quantity? TotalOnHand(IReadOnlyList<BatchStockLine> lines)
    {
        var units = lines.Select(line => line.OnHand.Unit!).Distinct(StringComparer.Ordinal).ToList();

        return units is [var unit] ? Quantity.Sum(unit, lines.Select(line => line.OnHand)) : null;
    }

    /// <summary>
    /// What the remaining stock cost. Each line is rounded once, by
    /// <see cref="Money.Times"/> under <see cref="EngineRounding"/>; money adds up exactly
    /// whatever the units are, which is why this figure survives a mixed-unit batch that the
    /// quantity does not.
    /// </summary>
    private static Money ValueAtCost(IReadOnlyList<BatchStockLine> lines)
    {
        var total = Money.Zero(lines[0].UnitCost.Currency);

        foreach (var line in lines)
        {
            total += line.UnitCost.Times(line.OnHand.Thousandths, Quantity.Scale, EngineRounding);
        }

        return total;
    }
}
