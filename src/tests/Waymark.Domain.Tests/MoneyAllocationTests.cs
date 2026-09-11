using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Splitting a known total across parts, which must not create variance at all
/// (decisions.md D-032). This is why <c>rounding_variance</c> has no allocation
/// source — a row with one would mean this code is broken.
/// </summary>
public sealed class MoneyAllocationTests
{
    private static Money Dzd(long minorUnits) => new(minorUnits, Currency.Dzd);

    private static long[] Units(IReadOnlyList<Money> parts) =>
        [.. parts.Select(part => part.MinorUnits)];

    [Fact]
    public void An_amount_that_does_not_divide_evenly_still_sums_back_exactly()
    {
        // 10,00 across three lines. Rounding each part independently gives 3,33
        // three times and loses a centime; the leftover is handed out instead.
        var parts = Dzd(1_000).Allocate(3);

        Assert.Equal([334, 333, 333], Units(parts));
        Assert.Equal(Dzd(1_000), parts.Aggregate(Money.Zero(Currency.Dzd), Money.Add));
    }

    [Fact]
    public void The_leftover_goes_to_the_largest_remainders_first()
    {
        var parts = Dzd(1_000).Allocate(7);

        Assert.Equal([143, 143, 143, 143, 143, 143, 142], Units(parts));
    }

    [Fact]
    public void Weighted_splits_follow_the_weights()
    {
        Assert.Equal([25, 25, 50], Units(Dzd(100).Allocate([1, 1, 2])));
        Assert.Equal([75, 25], Units(Dzd(100).Allocate([3, 1])));
        Assert.Equal([100], Units(Dzd(100).Allocate([7])));
    }

    [Fact]
    public void A_zero_weight_line_never_receives_a_leftover_unit()
    {
        // Provable — at least |leftover| entries have a non-zero remainder and
        // therefore sort ahead of every zero-weight entry — but worth pinning,
        // because a line weighted zero picking up a centime would be absurd on
        // a receipt and easy to miss.
        Assert.Equal([0, 4, 3, 3], Units(Dzd(10).Allocate([0, 1, 1, 1])));
        Assert.Equal([0, 50, 50], Units(Dzd(100).Allocate([0, 1, 1])));
    }

    [Fact]
    public void Negative_totals_allocate_away_from_zero_the_same_way()
    {
        // Refunds are split too, and the parts must still sum to the refund.
        var parts = Dzd(-1_000).Allocate(3);

        Assert.Equal([-334, -333, -333], Units(parts));
        Assert.Equal(Dzd(-1_000), parts.Aggregate(Money.Zero(Currency.Dzd), Money.Add));
    }

    [Fact]
    public void The_parts_always_sum_to_the_total()
    {
        // The single claim the allocator exists to make, asserted across shapes
        // rather than examples.
        long[] totals = [0, 1, -1, 7, -7, 100, 999, -999, 100_000, 33_333_333];
        long[][] weightSets =
        [
            [1],
            [1, 1],
            [1, 1, 1],
            [1, 2, 3, 4],
            [0, 1, 0, 1],
            [5, 0, 0],
            [999, 1],
            [7, 7, 7, 7, 7, 7, 7],
            [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1],
        ];

        foreach (var total in totals)
        {
            foreach (var weights in weightSets)
            {
                var parts = Dzd(total).Allocate(weights);

                Assert.Equal(weights.Length, parts.Count);
                Assert.Equal(Dzd(total), parts.Aggregate(Money.Zero(Currency.Dzd), Money.Add));
            }
        }
    }

    [Fact]
    public void No_part_is_more_than_one_unit_from_its_exact_share()
    {
        // Exactness alone would allow a degenerate "give it all to line one"
        // implementation. This is the other half of the claim.
        long[] weights = [3, 1, 1, 5, 2];
        var totalWeight = weights.Sum();

        for (long total = -500; total <= 500; total++)
        {
            var parts = Dzd(total).Allocate(weights);

            for (var i = 0; i < weights.Length; i++)
            {
                var scaledDifference = Math.Abs((parts[i].MinorUnits * totalWeight) - (total * weights[i]));

                Assert.True(
                    scaledDifference < totalWeight,
                    $"part {i} of {total} strayed more than one unit from its share");
            }
        }
    }

    [Fact]
    public void The_same_input_always_produces_the_same_split()
    {
        // A receipt reprinted must show the same numbers, and two tills
        // computing the same basket must agree.
        long[] weights = [1, 1, 1, 1, 1, 1, 1];

        var first = Units(Dzd(1_000).Allocate(weights));
        var second = Units(Dzd(1_000).Allocate(weights));
        var third = Units(Dzd(1_000).Allocate(weights));

        Assert.Equal(first, second);
        Assert.Equal(second, third);
    }

    [Fact]
    public void A_discount_split_across_lines_loses_nothing()
    {
        // The case this was built for: 10% off a basket, apportioned to the
        // lines so the receipt's discount_total matches the sum of the lines.
        long[] lineTotals = [1_999, 4_550, 33, 7, 120_000];
        var basket = Dzd(lineTotals.Sum());

        var discount = basket.Percent(new BasisPoints(1_000), Rounding.HalfEven);
        var perLine = discount.Allocate(lineTotals);

        Assert.Equal(discount, perLine.Aggregate(Money.Zero(Currency.Dzd), Money.Add));
    }

    [Fact]
    public void Splitting_into_nothing_or_by_nothing_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = Dzd(100).Allocate(0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = Dzd(100).Allocate(-1); });
        Assert.Throws<ArgumentException>(() => { _ = Dzd(100).Allocate([]); });
        Assert.Throws<ArgumentException>(() => { _ = Dzd(100).Allocate([0, 0, 0]); });
        Assert.Throws<ArgumentException>(() => { _ = Dzd(100).Allocate([1, -1]); });
        Assert.Throws<ArgumentNullException>(() => { _ = Dzd(100).Allocate(null!); });
    }
}
