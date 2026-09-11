using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// The operations that are allowed to be operators, because they cannot round.
/// </summary>
public sealed class MoneyExactArithmeticTests
{
    private static Money Dzd(long minorUnits) => new(minorUnits, Currency.Dzd);

    [Fact]
    public void Addition_and_subtraction_are_exact()
    {
        Assert.Equal(Dzd(300), Dzd(100) + Dzd(200));
        Assert.Equal(Dzd(-100), Dzd(100) - Dzd(200));
        Assert.Equal(Dzd(0), Dzd(100) - Dzd(100));
    }

    [Fact]
    public void Multiplying_by_a_whole_number_is_exact()
    {
        Assert.Equal(Dzd(1_500), Dzd(500) * 3);
        Assert.Equal(Dzd(1_500), 3 * Dzd(500));
        Assert.Equal(Dzd(-1_500), Dzd(500) * -3);
        Assert.Equal(Dzd(0), Dzd(500) * 0);
    }

    [Fact]
    public void Negation_round_trips()
    {
        Assert.Equal(Dzd(-250), -Dzd(250));
        Assert.Equal(Dzd(250), -(-Dzd(250)));
    }

    [Fact]
    public void Sign_absolute_value_and_predicates_agree()
    {
        Assert.Equal(-1, Dzd(-1).Sign);
        Assert.Equal(0, Dzd(0).Sign);
        Assert.Equal(1, Dzd(1).Sign);

        Assert.Equal(Dzd(250), Dzd(-250).Abs());
        Assert.True(Dzd(0).IsZero);
        Assert.True(Dzd(-1).IsNegative);
        Assert.True(Dzd(1).IsPositive);
    }

    [Fact]
    public void Overflow_throws_rather_than_wrapping()
    {
        // A silently wrapped total is the exact failure CLAUDE.md §8 exists for:
        // the code runs, the screen looks right, the number is wrong.
        Assert.Throws<OverflowException>(() => { _ = Dzd(long.MaxValue) + Dzd(1); });
        Assert.Throws<OverflowException>(() => { _ = Dzd(long.MinValue) - Dzd(1); });
        Assert.Throws<OverflowException>(() => { _ = Dzd(long.MaxValue) * 2; });
        Assert.Throws<OverflowException>(() => { _ = Dzd(long.MaxValue).Times(2, 1, Rounding.HalfEven); });
    }

    [Fact]
    public void The_intermediate_product_does_not_overflow_before_it_is_divided_back_down()
    {
        // long.MaxValue * 1000 exceeds a long. Computing in Int128 is what makes
        // "unit price times a quantity in thousandths" safe at any scale.
        var largest = Dzd(long.MaxValue);

        Assert.Equal(largest, largest.Times(1_000, 1_000, Rounding.HalfEven));
    }

    [Fact]
    public void Ordering_works_within_a_currency()
    {
        Assert.True(Dzd(100) < Dzd(200));
        Assert.True(Dzd(200) > Dzd(100));
        Assert.True(Dzd(100) <= Dzd(100));
        Assert.True(Dzd(100) >= Dzd(100));
        Assert.Equal(0, Dzd(100).CompareTo(Dzd(100)));
    }

    [Fact]
    public void ToString_is_readable_and_signed_correctly()
    {
        Assert.Equal("12.47 DZD", Dzd(1_247).ToString());
        Assert.Equal("-12.47 DZD", Dzd(-1_247).ToString());
        Assert.Equal("0.00 DZD", Dzd(0).ToString());

        // The sign must survive when the whole part is zero, which is where a
        // naive implementation loses it.
        Assert.Equal("-0.05 DZD", Dzd(-5).ToString());
    }
}
