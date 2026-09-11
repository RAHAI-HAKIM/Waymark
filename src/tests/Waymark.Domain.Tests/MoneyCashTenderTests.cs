using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Cash rounds to what can actually change hands; the invoice does not
/// (decisions.md D-034). The smallest coin in practical circulation in Algeria
/// is five dinars, so the DZD step is 500 minor units.
/// </summary>
public sealed class MoneyCashTenderTests
{
    private static Money Dzd(long minorUnits) => new(minorUnits, Currency.Dzd);

    private static Money Eur(long minorUnits) => new(minorUnits, Currency.Eur);

    [Theory]
    // exact amount, what is tendered, the variance
    [InlineData(124_700, 124_500, -200)]   // 1247,00 -> 1245,00
    [InlineData(124_800, 125_000, +200)]   // 1248,00 -> 1250,00
    [InlineData(124_500, 124_500, 0)]      // already on the step
    [InlineData(124_750, 125_000, +250)]   // an exact half goes away from zero
    [InlineData(124_250, 124_500, +250)]   // and so does this one
    [InlineData(0, 0, 0)]
    [InlineData(-124_750, -125_000, -250)] // refunds round away from zero too
    [InlineData(-124_700, -124_500, +200)]
    public void Cash_rounds_to_the_nearest_step_with_ties_away_from_zero(
        long exact, long tendered, long variance)
    {
        var cash = Dzd(exact).ToCashTender();

        Assert.Equal(Dzd(tendered), cash.Tendered);
        Assert.Equal(Dzd(variance), cash.Variance);
    }

    [Fact]
    public void The_variance_is_always_the_tendered_amount_minus_the_exact_one()
    {
        // Sign matters: negative means the customer handed over less than the
        // invoice says, which is money the shop did not receive.
        for (long exact = -3_000; exact <= 3_000; exact++)
        {
            var cash = Dzd(exact).ToCashTender();

            Assert.Equal(cash.Tendered - Dzd(exact), cash.Variance);
        }
    }

    [Fact]
    public void The_variance_is_never_as_large_as_the_step()
    {
        // Half a step is the bound for nearest rounding. A bug that rounded in
        // one direction only would pass every equality test above and fail here.
        var halfStep = Currency.Dzd.CashRoundingStep / 2;

        for (long exact = -3_000; exact <= 3_000; exact++)
        {
            var variance = Dzd(exact).ToCashTender().Variance;

            Assert.True(
                Math.Abs(variance.MinorUnits) <= halfStep,
                $"{exact} moved by {variance}, more than half a step");
        }
    }

    [Fact]
    public void Rounding_does_not_drift_in_one_direction()
    {
        // The test that catches "always toward the store", which was rejected:
        // it takes up to 4,99 DZD from every cash customer on every sale, so its
        // drift grows with turnover. Nearest rounding leaves only the small
        // residual of the tie rule — bounded by the step, not by the takings.
        var drift = 0L;

        for (long exact = 0; exact < 5_000; exact++)
        {
            drift += Dzd(exact).ToCashTender().Variance.MinorUnits;
        }

        Assert.True(
            Math.Abs(drift) <= Currency.Dzd.CashRoundingStep * 10,
            $"cash rounding drifted by {drift} minor units over 5000 consecutive amounts");
    }

    [Fact]
    public void The_tendered_amount_is_always_a_whole_number_of_steps()
    {
        for (long exact = -3_000; exact <= 3_000; exact++)
        {
            var tendered = Dzd(exact).ToCashTender().Tendered;

            Assert.Equal(0, tendered.MinorUnits % Currency.Dzd.CashRoundingStep);
        }
    }

    [Fact]
    public void A_currency_that_does_not_round_cash_is_left_alone()
    {
        var cash = Eur(1_247).ToCashTender();

        Assert.Equal(Eur(1_247), cash.Tendered);
        Assert.Equal(Eur(0), cash.Variance);
        Assert.False(cash.HasVariance);
        Assert.False(Currency.Eur.RoundsCash);
        Assert.True(Currency.Dzd.RoundsCash);
    }

    [Fact]
    public void The_step_is_a_property_of_the_currency_not_a_constant()
    {
        // The number 500 must never appear in a handler. If this ever needs
        // changing, it changes in one place.
        Assert.Equal(500, Currency.Dzd.CashRoundingStep);
        Assert.Equal(1, Currency.Eur.CashRoundingStep);
        Assert.Equal(1, Currency.Usd.CashRoundingStep);
    }

    [Fact]
    public void HasVariance_reports_whether_rounding_moved_anything()
    {
        Assert.True(Dzd(124_700).ToCashTender().HasVariance);
        Assert.False(Dzd(124_500).ToCashTender().HasVariance);
    }
}
