using Waymark.Domain.Enums;
using Waymark.Domain.Inventory;
using Waymark.Domain.Values;
using Waymark.Generator.Randomness;

namespace Waymark.Generator.Simulation;

/// <summary>
/// Cycle counts (W10 S7, D-046 §30): every few days, before opening, the manager counts one
/// subcategory's lots, in turn.
///
/// <para>
/// Shrinkage happens unseen — a unit stolen, a jar broken, a mis-scan never corrected — and a
/// count is where it is found. So a counted lot is short with a small probability and, more
/// rarely, over by one; the variance is applied to the shelf and posted as a <c>count</c>
/// movement, the one place stock changes without a sale, receipt or write-off. A count is
/// written posted and approved by the manager, since the generator records what happened, not
/// a workflow in progress.
/// </para>
/// </summary>
internal sealed class StockCounter(SimulationContext context)
{
    /// <summary>Minutes before opening that a count starts.</summary>
    public const int MinutesBeforeOpening = 60;

    private readonly RandomStream _counts = context.Random.Stream("count");
    private int _countNumber;

    /// <summary>Whether a count happens on the <paramref name="dayIndex"/>-th trading day (0-based).</summary>
    public bool IsCountDay(int dayIndex) => dayIndex % context.Config.Mess.CountIntervalDays.Value == 0;

    /// <summary>Counts the next subcategory in turn. Returns false if it held nothing to count.</summary>
    public bool Count(DateOnly date)
    {
        var store = context.Store;
        var mess = context.Config.Mess;
        var db = context.Database.Context;
        var number = _countNumber++;
        var (_, categoryId) = store.Subcategories[number % store.Subcategories.Count];
        var subcategory = store.Subcategories[number % store.Subcategories.Count].Name;

        var lots = store.Variants
            .Where(variant => string.Equals(variant.Catalogue.Subcategory, subcategory, StringComparison.Ordinal))
            .SelectMany(variant => context.State.LotsOf(variant.Index)
                .Select((lot, lotIndex) => (Variant: variant, Lot: lot, LotIndex: lotIndex)))
            .Where(entry => entry.Lot.OnHand.IsPositive)
            .ToList();

        if (lots.Count == 0)
        {
            return false;
        }

        var manager = store.Manager.StaffId;
        var started = context.Clock.GetUtcNow();
        var countId = context.Ids.NewId();
        var totalVariance = Money.Zero(store.Currency);

        foreach (var (variant, lot, lotIndex) in lots)
        {
            var expected = lot.OnHand;
            var units = expected.Thousandths / Quantity.Scale;
            var delta = 0L;

            if (Distributions.Bernoulli(_counts.Uniform(number, variant.Index, lotIndex, 0), mess.CountShrinkageChance.Value))
            {
                delta = -Distributions.UniformInt(_counts.Uniform(number, variant.Index, lotIndex, 1), 1, (int)Math.Min(3, units));
            }
            else if (Distributions.Bernoulli(_counts.Uniform(number, variant.Index, lotIndex, 2), mess.CountFoundChance.Value))
            {
                delta = 1;
            }

            var change = variant.SellingUnit.Delta(delta * Quantity.Scale);
            var counted = expected + change;
            var value = lot.UnitCost.Times(change.Thousandths, Quantity.Scale, store.RoundingPolicy);
            totalVariance += value;

            db.StockCountItems.Add(new StockCountItem
            {
                CountItemId = context.Ids.NewId(),
                CountId = countId,
                VariantId = variant.VariantId,
                BatchId = lot.BatchId,
                ExpectedQuantity = expected.Thousandths,
                CountedQuantity = counted.Thousandths,
                VarianceQuantity = change.Thousandths,
                UnitCost = lot.UnitCost,
                VarianceValue = value,
                CountedBy = manager,
                CountedAt = started,
            });

            if (change.IsZero)
            {
                continue;
            }

            lot.Apply(change);
            db.StockMovements.Add(new StockMovement
            {
                MovementId = context.Ids.NewId(),
                StoreId = store.StoreId,
                VariantId = variant.VariantId,
                BatchId = lot.BatchId,
                MovementDate = date,
                MovementType = StockMovementType.Count,
                QuantityChanged = change.Thousandths,
                UnitCode = change.Unit!,
                UnitCost = lot.UnitCost,
                ReferenceType = "stock_count",
                ReferenceId = countId,
                StaffId = manager,
                CreatedAt = started,
            });
        }

        db.StockCounts.Add(new StockCount
        {
            CountId = countId,
            StoreId = store.StoreId,
            CountType = CountType.Cycle,
            ScopeCategoryId = categoryId,
            StartedBy = manager,
            StartedAt = started,
            CompletedAt = started.AddMinutes(20),
            ApprovedBy = manager,
            ApprovedAt = started.AddMinutes(30),
            TotalVarianceValue = totalVariance,
            Status = StockCountStatus.Posted,
            CreatedAt = started,
            UpdatedAt = started.AddMinutes(30),
        });

        return true;
    }
}
