using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Session B5, ✍ Hakim: <see cref="PriceOverride"/>, a price typed at the counter (D-092). The silent
/// failures: a limit rounded up that lets a centime past it; an override to zero that gives goods
/// away without a discount's reason; an override that changes nothing recorded as one; a price below
/// cost charged without a word.
/// </summary>
public sealed class PriceOverrideTests
{
    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    [Fact]
    public void The_band_is_twenty_percent()
    {
        Assert.Equal(new BasisPoints(2_000), PriceOverride.MaximumRise);
    }

    [Theory]
    [InlineData(16_800, OverrideVerdict.Accepted)]    // 140,00 + 20 % is 168,00 exactly
    [InlineData(16_801, OverrideVerdict.AboveBand)]
    [InlineData(15_000, OverrideVerdict.Accepted)]
    [InlineData(12_000, OverrideVerdict.Accepted)]    // down, above cost
    [InlineData(1, OverrideVerdict.AcceptedBelowCost)] // down, below cost
    [InlineData(0, OverrideVerdict.NotAboveZero)]
    [InlineData(-500, OverrideVerdict.NotAboveZero)]
    [InlineData(14_000, OverrideVerdict.Unchanged)]
    public void A_price_is_judged_against_the_price_in_force_and_the_cost(long centimes, OverrideVerdict verdict)
    {
        Assert.Equal(verdict, PriceOverride.Check(Dzd(14_000), Dzd(centimes), Dzd(10_000)).Verdict);
    }

    [Fact]
    public void The_ceiling_is_rounded_down_never_up()
    {
        // 143,33 + 20 % is 171,996: the ceiling is 171,99, and 172,00 is above the band.
        var check = PriceOverride.Check(Dzd(14_333), Dzd(17_199), null);

        Assert.Equal(Dzd(17_199), check.Ceiling);
        Assert.Equal(OverrideVerdict.Accepted, check.Verdict);
        Assert.Equal(OverrideVerdict.AboveBand, PriceOverride.Check(Dzd(14_333), Dzd(17_200), null).Verdict);
    }

    [Fact]
    public void Below_cost_is_said_and_still_charged()
    {
        var check = PriceOverride.Check(Dzd(14_000), Dzd(9_000), Dzd(10_000));

        Assert.Equal(OverrideVerdict.AcceptedBelowCost, check.Verdict);
        Assert.True(check.MayCharge);
    }

    [Fact]
    public void At_cost_is_not_below_it()
    {
        Assert.Equal(OverrideVerdict.Accepted, PriceOverride.Check(Dzd(14_000), Dzd(10_000), Dzd(10_000)).Verdict);
    }

    [Fact]
    public void With_no_cost_known_nothing_is_below_it()
    {
        Assert.Equal(OverrideVerdict.Accepted, PriceOverride.Check(Dzd(14_000), Dzd(1), null).Verdict);
    }

    [Fact]
    public void A_cost_above_the_band_does_not_open_the_band()
    {
        // Bought at 200,00, priced at 140,00: 180,00 is still above the band.
        Assert.Equal(OverrideVerdict.AboveBand, PriceOverride.Check(Dzd(14_000), Dzd(18_000), Dzd(20_000)).Verdict);
    }

    [Fact]
    public void Nothing_that_may_not_be_charged_says_it_may()
    {
        Assert.False(PriceOverride.Check(Dzd(14_000), Dzd(0), null).MayCharge);
        Assert.False(PriceOverride.Check(Dzd(14_000), Dzd(14_000), null).MayCharge);
        Assert.False(PriceOverride.Check(Dzd(14_000), Dzd(20_000), null).MayCharge);
    }

    [Fact]
    public void Another_currency_throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            PriceOverride.Check(Dzd(14_000), Money.FromMinorUnits(100, Currency.Eur), null));
}
