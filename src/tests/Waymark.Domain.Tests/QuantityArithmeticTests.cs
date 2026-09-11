using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// A level plus a change is a level. Every operation here is exact — nothing in
/// quantity arithmetic rounds, which is why none of it takes a policy.
/// </summary>
public sealed class QuantityArithmeticTests
{
    private static Quantity Kg(long thousandths) => Quantity.FromThousandths(thousandths, "kg");

    private static QuantityDelta KgChange(long thousandths) => QuantityDelta.FromThousandths(thousandths, "kg");

    [Fact]
    public void A_level_plus_a_change_is_a_level()
    {
        Quantity closing = Kg(5_000) + KgChange(1_250);

        Assert.Equal(Kg(6_250), closing);
        Assert.Equal(Kg(6_250), KgChange(1_250) + Kg(5_000));
    }

    [Fact]
    public void A_level_less_a_change_is_a_level()
    {
        Assert.Equal(Kg(3_750), Kg(5_000) - KgChange(1_250));
    }

    [Fact]
    public void Two_levels_differ_by_a_change()
    {
        QuantityDelta movement = Kg(6_250) - Kg(5_000);

        Assert.Equal(KgChange(1_250), movement);
        Assert.True(movement.IsIncrease);

        Assert.Equal(KgChange(-1_250), Kg(5_000) - Kg(6_250));
    }

    [Fact]
    public void A_level_can_go_negative_because_a_sale_can_outrun_its_receipt()
    {
        // inventories.quantity is annotated "may be negative". A type that
        // refused this would make the oversold case unrepresentable, and the
        // code would have to lie about it somewhere else.
        var oversold = Kg(2_000) - KgChange(5_000);

        Assert.Equal(Kg(-3_000), oversold);
        Assert.True(oversold.IsNegative);
    }

    [Fact]
    public void Multiplying_by_a_whole_number_is_exact()
    {
        Assert.Equal(Kg(15_000), Kg(5_000) * 3);
        Assert.Equal(Kg(15_000), 3 * Kg(5_000));
        Assert.Equal(KgChange(-3_750), KgChange(-1_250) * 3);
    }

    [Fact]
    public void Totalling_magnitudes_has_to_be_asked_for_by_name()
    {
        // Quantity + Quantity does not exist, so summing the lines of an order
        // reads as a deliberate act rather than as ordinary arithmetic.
        var lines = new[] { Kg(1_500), Kg(2_250), Kg(3_000) };

        Assert.Equal(Kg(6_750), Quantity.Sum("kg", lines));
    }

    [Fact]
    public void An_empty_total_is_zero_in_the_stated_unit()
    {
        // Which is why Sum takes the unit: an order with no lines still has one,
        // and inventing a unit from nowhere is not possible.
        Assert.Equal(Kg(0), Quantity.Sum("kg", []));
        Assert.Equal(KgChange(0), QuantityDelta.Sum("kg", []));
    }

    [Fact]
    public void Overflow_throws_rather_than_wrapping()
    {
        Assert.Throws<OverflowException>(() => { _ = Kg(long.MaxValue) + KgChange(1); });
        Assert.Throws<OverflowException>(() => { _ = Kg(long.MaxValue) * 2; });
        Assert.Throws<OverflowException>(() => { _ = KgChange(long.MinValue) - KgChange(1); });
        Assert.Throws<OverflowException>(() => { _ = Quantity.Sum("kg", [Kg(long.MaxValue), Kg(1)]); });
    }

    [Fact]
    public void Levels_compare_within_a_unit()
    {
        Assert.True(Kg(1_000) < Kg(2_000));
        Assert.True(Kg(2_000) >= Kg(2_000));
        Assert.Equal(0, Kg(1_000).CompareTo(Kg(1_000)));
        Assert.True(KgChange(-1) < KgChange(1));
    }

    [Fact]
    public void Sign_absolute_value_and_predicates_agree()
    {
        Assert.Equal(-1, Kg(-1).Sign);
        Assert.Equal(0, Kg(0).Sign);
        Assert.Equal(1, Kg(1).Sign);

        Assert.Equal(Kg(2_500), Kg(-2_500).Abs());
        Assert.True(Kg(0).IsZero);
        Assert.True(Kg(-1).IsNegative);
        Assert.True(Kg(1).IsPositive);
    }

    [Fact]
    public void ToString_trims_the_fraction_and_names_the_unit()
    {
        Assert.Equal("1.234 kg", Kg(1_234).ToString());
        Assert.Equal("5 kg", Kg(5_000).ToString());
        Assert.Equal("0.5 kg", Kg(500).ToString());
        Assert.Equal("-0.5 kg", Kg(-500).ToString());
        Assert.Equal("-1.5 kg", Kg(-1_500).ToString());
        Assert.Equal("0 kg", Kg(0).ToString());
    }

    [Fact]
    public void A_change_always_shows_its_direction()
    {
        // The sign is the entire reason the type exists, so it is never hidden.
        Assert.Equal("+1.25 kg", KgChange(1_250).ToString());
        Assert.Equal("-1.25 kg", KgChange(-1_250).ToString());
        Assert.Equal("0 kg", KgChange(0).ToString());
    }
}
