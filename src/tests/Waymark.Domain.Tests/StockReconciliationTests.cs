using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// <c>closing = opening + sum(movements)</c>, which CLAUDE.md §8 puts in the top
/// three things worth testing. The point of two types is that this identity is
/// checked by the compiler as well as by these tests.
/// </summary>
public sealed class StockReconciliationTests
{
    private const string Kilos = "kg";

    private static Quantity Level(long thousandths) => Quantity.FromThousandths(thousandths, Kilos);

    private static QuantityDelta Move(long thousandths) => QuantityDelta.FromThousandths(thousandths, Kilos);

    private static readonly QuantityDelta[] ADayOfMovements =
    [
        Move(25_000),   // receipt
        Move(-1_250),   // sale
        Move(-3_400),   // sale
        Move(800),      // return in
        Move(-12_000),  // sale
        Move(-2_150),   // write-off, expired
        Move(-6_500),   // sale
    ];

    [Fact]
    public void Replaying_the_movements_reproduces_the_closing_level()
    {
        var opening = Level(4_000);

        var closing = ADayOfMovements.Aggregate(opening, Quantity.Add);

        Assert.Equal(opening + QuantityDelta.Sum(Kilos, ADayOfMovements), closing);
        Assert.Equal(Level(4_500), closing);
    }

    [Fact]
    public void The_difference_between_two_levels_is_the_sum_of_what_happened_between_them()
    {
        // This is the reconciliation a stock count performs, and it holds for
        // any starting level.
        for (long opening = -5_000; opening <= 5_000; opening += 250)
        {
            var start = Level(opening);
            var end = ADayOfMovements.Aggregate(start, Quantity.Add);

            Assert.Equal(QuantityDelta.Sum(Kilos, ADayOfMovements), end - start);
        }
    }

    [Fact]
    public void Applying_the_movements_then_reversing_them_returns_the_opening_level()
    {
        var opening = Level(4_000);

        var closing = ADayOfMovements.Aggregate(opening, Quantity.Add);
        var rewound = ADayOfMovements.Reverse().Aggregate(closing, Quantity.Subtract);

        Assert.Equal(opening, rewound);
    }

    [Fact]
    public void A_replayed_movement_is_visible_as_a_discrepancy()
    {
        // Sync idempotency, stated in quantities: applying the same movement
        // twice must not silently produce a plausible level.
        var opening = Level(4_000);

        var once = ADayOfMovements.Aggregate(opening, Quantity.Add);
        var twice = once + ADayOfMovements[0];

        Assert.NotEqual(once, twice);
        Assert.Equal(ADayOfMovements[0], twice - once);
    }

    [Fact]
    public void A_stock_count_expresses_its_adjustment_as_a_change()
    {
        // The counted level is a level; what has to be posted to get there is a
        // change. Conflating the two is the mistake the types exist to stop.
        var systemSays = Level(4_500);
        var counted = Level(4_200);

        var adjustment = counted - systemSays;

        Assert.Equal(Move(-300), adjustment);
        Assert.True(adjustment.IsDecrease);
        Assert.Equal(counted, systemSays + adjustment);
    }

    [Fact]
    public void An_oversold_line_reconciles_like_any_other()
    {
        var opening = Level(1_000);
        QuantityDelta[] movements = [Move(-1_500), Move(-500), Move(3_000)];

        var afterFirst = opening + movements[0];

        Assert.True(afterFirst.IsNegative);
        Assert.Equal(Level(2_000), movements.Aggregate(opening, Quantity.Add));
    }

    [Fact]
    public void Whole_units_and_fractions_reconcile_the_same_way()
    {
        // A unit sold by the piece goes through identical arithmetic; only the
        // precision it admits differs, and that is UnitPrecision's job.
        var pieces = UnitPrecision.For("piece", decimalPlaces: 0);

        var opening = pieces.Whole(12);
        QuantityDelta[] movements = [pieces.Delta(-3_000), pieces.Delta(-1_000), pieces.Delta(6_000)];

        Assert.Equal(pieces.Whole(14), movements.Aggregate(opening, Quantity.Add));
    }
}
