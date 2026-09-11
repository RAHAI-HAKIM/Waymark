using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// The one primitive that can create or destroy a minor unit, and the two
/// policies over it (decisions.md D-031, D-032).
/// </summary>
public sealed class MoneyRoundingTests
{
    private static Money Dzd(long minorUnits) => new(minorUnits, Currency.Dzd);

    [Theory]
    // value, numerator, denominator, HalfEven, HalfUp
    [InlineData(24, 1, 10, 2, 2)]    // 2.4  — below the half, both round down
    [InlineData(26, 1, 10, 3, 3)]    // 2.6  — above the half, both round up
    [InlineData(25, 1, 10, 2, 3)]    // 2.5  — the policies part company
    [InlineData(35, 1, 10, 4, 4)]    // 3.5  — half-even goes up, to the even 4
    [InlineData(15, 1, 10, 2, 2)]    // 1.5  — half-even goes up, to the even 2
    [InlineData(5, 1, 2, 2, 3)]      // 2.5
    [InlineData(-25, 1, 10, -2, -3)] // -2.5 — away from zero, not "up"
    [InlineData(-35, 1, 10, -4, -4)]
    [InlineData(0, 1, 3, 0, 0)]
    public void Halves_are_the_only_place_the_policies_differ(
        long value, long numerator, long denominator, long halfEven, long halfUp)
    {
        Assert.Equal(Dzd(halfEven), Dzd(value).Times(numerator, denominator, Rounding.HalfEven));
        Assert.Equal(Dzd(halfUp), Dzd(value).Times(numerator, denominator, Rounding.HalfUp));
    }

    [Fact]
    public void Away_from_zero_means_away_from_zero_on_both_sides()
    {
        // The mistake this catches: implementing HalfUp as "add 0.5 and floor",
        // which rounds -2.5 to -2 instead of -3 and quietly favours the customer
        // on every refund.
        Assert.Equal(Dzd(3), Dzd(5).Times(1, 2, Rounding.HalfUp));
        Assert.Equal(Dzd(-3), Dzd(-5).Times(1, 2, Rounding.HalfUp));
    }

    [Fact]
    public void Half_even_rounds_to_the_even_neighbour_in_both_directions()
    {
        Assert.Equal(Dzd(2), Dzd(5).Times(1, 2, Rounding.HalfEven));   // 2.5 -> 2
        Assert.Equal(Dzd(4), Dzd(7).Times(1, 2, Rounding.HalfEven));   // 3.5 -> 4
        Assert.Equal(Dzd(-2), Dzd(-5).Times(1, 2, Rounding.HalfEven));
        Assert.Equal(Dzd(-4), Dzd(-7).Times(1, 2, Rounding.HalfEven));
    }

    [Fact]
    public void The_two_policies_agree_everywhere_that_is_not_an_exact_half()
    {
        // Stated as a property rather than a list, because "differs only at the
        // half" is the actual claim and a handful of examples does not make it.
        var differences = 0;

        for (long value = -2_000; value <= 2_000; value++)
        {
            for (long denominator = 1; denominator <= 40; denominator++)
            {
                var even = Dzd(value).Times(1, denominator, Rounding.HalfEven);
                var up = Dzd(value).Times(1, denominator, Rounding.HalfUp);

                var isExactHalf = Math.Abs(value % denominator) * 2 == denominator;

                if (isExactHalf)
                {
                    differences += even == up ? 0 : 1;
                }
                else
                {
                    Assert.Equal(even, up);
                }
            }
        }

        Assert.True(differences > 0, "The sweep never hit an exact half, so it proved nothing.");
    }

    [Fact]
    public void A_rounded_result_is_never_more_than_half_a_unit_from_the_exact_value()
    {
        foreach (var rounding in new[] { Rounding.HalfEven, Rounding.HalfUp })
        {
            for (long value = -999; value <= 999; value++)
            {
                for (long denominator = 1; denominator <= 17; denominator++)
                {
                    var rounded = Dzd(value).Times(7, denominator, rounding).MinorUnits;
                    var exactTimesDenominator = value * 7L;

                    // |rounded - exact| <= 1/2, multiplied through by the denominator.
                    var error = Math.Abs((rounded * denominator) - exactTimesDenominator) * 2;

                    Assert.True(
                        error <= denominator,
                        $"{value} * 7/{denominator} under {rounding} landed further than half a unit away.");
                }
            }
        }
    }

    [Fact]
    public void Percent_is_the_same_primitive_and_follows_the_same_policy()
    {
        Assert.Equal(Dzd(1_900), Dzd(10_000).Percent(BasisPoints.StandardVat, Rounding.HalfEven));

        // 150 * 19% = 28.5 exactly, which is where the policies separate.
        Assert.Equal(Dzd(28), Dzd(150).Percent(BasisPoints.StandardVat, Rounding.HalfEven));
        Assert.Equal(Dzd(29), Dzd(150).Percent(BasisPoints.StandardVat, Rounding.HalfUp));

        Assert.Equal(Dzd(0), Dzd(12_345).Percent(BasisPoints.Zero, Rounding.HalfEven));
    }

    [Fact]
    public void Dividing_by_zero_throws_and_never_returns_zero()
    {
        // A zero standing in for "could not compute" is the silent kind of wrong
        // number — a 0% margin because revenue was zero looks right on screen
        // and is not (decisions.md D-037).
        Assert.Throws<DivideByZeroException>(() => { _ = Dzd(100).DivideBy(0, Rounding.HalfEven); });
        Assert.Throws<DivideByZeroException>(() => { _ = Dzd(100).Times(1, 0, Rounding.HalfEven); });
    }

    [Fact]
    public void The_caller_who_expects_a_zero_divisor_has_to_say_so_and_handle_the_null()
    {
        Assert.Null(Dzd(100).TryDivideBy(0, Rounding.HalfEven));
        Assert.Equal(Dzd(25), Dzd(100).TryDivideBy(4, Rounding.HalfEven));
    }

    [Fact]
    public void A_negative_denominator_flips_the_sign_without_disturbing_the_rounding()
    {
        Assert.Equal(Dzd(-2), Dzd(5).Times(1, -2, Rounding.HalfEven));
        Assert.Equal(Dzd(-3), Dzd(5).Times(1, -2, Rounding.HalfUp));
    }

    [Fact]
    public void Rounding_happens_once_at_the_end_not_at_each_step()
    {
        // 3 * (1/3) is 1, not 3 * round(1/3) = 0. This is why Times takes a
        // rational rather than a pre-divided factor.
        Assert.Equal(Dzd(100), Dzd(100).Times(3, 3, Rounding.HalfEven));
        Assert.Equal(Dzd(1), Dzd(1).Times(3, 3, Rounding.HalfEven));
    }
}
