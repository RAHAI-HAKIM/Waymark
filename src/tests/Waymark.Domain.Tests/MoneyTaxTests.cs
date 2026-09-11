using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// TVA extracted per line from a TTC price, with the tax derived by subtraction
/// (decisions.md D-033). Displayed retail prices in Algeria are TTC under Law
/// No. 04-02.
/// </summary>
public sealed class MoneyTaxTests
{
    private static Money Dzd(long minorUnits) => new(minorUnits, Currency.Dzd);

    [Fact]
    public void A_round_case_splits_exactly()
    {
        // 119,00 TTC at 19% is 100,00 HT and 19,00 TVA, with nothing left over.
        var split = Dzd(11_900).SplitTaxInclusive(BasisPoints.StandardVat, Rounding.HalfEven);

        Assert.Equal(Dzd(10_000), split.Net);
        Assert.Equal(Dzd(1_900), split.Tax);
        Assert.Equal(Dzd(11_900), split.Gross);
    }

    [Fact]
    public void Net_plus_tax_equals_the_line_total_for_every_amount_rate_and_policy()
    {
        // This is the invariant the whole design of D-033 exists to produce, and
        // it is what makes subtotal + tax_total == total_amount exact once the
        // lines are summed. A test of a few examples would not establish it.
        var rates = new[]
        {
            BasisPoints.Zero,
            BasisPoints.ReducedVat,
            BasisPoints.StandardVat,
            new BasisPoints(1),
            new BasisPoints(BasisPoints.Scale),
        };

        foreach (var rounding in new[] { Rounding.HalfEven, Rounding.HalfUp })
        {
            foreach (var rate in rates)
            {
                for (long ttc = 0; ttc <= 5_000; ttc++)
                {
                    var line = Dzd(ttc);
                    var split = line.SplitTaxInclusive(rate, rounding);

                    Assert.Equal(line, split.Gross);
                    Assert.False(split.Net.IsNegative, $"net went negative at {ttc} under {rate}");
                    Assert.False(split.Tax.IsNegative, $"tax went negative at {ttc} under {rate}");
                    Assert.True(split.Net <= line, $"net exceeded the line total at {ttc} under {rate}");
                }
            }
        }
    }

    [Fact]
    public void A_zero_rate_leaves_the_whole_line_as_net()
    {
        var split = Dzd(4_999).SplitTaxInclusive(BasisPoints.Zero, Rounding.HalfEven);

        Assert.Equal(Dzd(4_999), split.Net);
        Assert.Equal(Dzd(0), split.Tax);
    }

    [Fact]
    public void Refund_lines_split_the_same_way_with_the_signs_carried_through()
    {
        var refund = Dzd(-11_900).SplitTaxInclusive(BasisPoints.StandardVat, Rounding.HalfEven);

        Assert.Equal(Dzd(-10_000), refund.Net);
        Assert.Equal(Dzd(-1_900), refund.Tax);
        Assert.Equal(Dzd(-11_900), refund.Gross);
    }

    [Fact]
    public void Summing_lines_reproduces_the_invoice_totals_exactly()
    {
        // The receipt-level claim: no reconciliation step, no residual, no
        // invoice-level tax variance. That is why rounding_variance has no
        // tax_reconciliation source.
        long[] lineTotals = [1_999, 4_550, 33, 7, 120_000, 88_881, 1, 250];

        var subtotal = Money.Zero(Currency.Dzd);
        var taxTotal = Money.Zero(Currency.Dzd);
        var grandTotal = Money.Zero(Currency.Dzd);

        foreach (var amount in lineTotals)
        {
            var line = Dzd(amount);
            var split = line.SplitTaxInclusive(BasisPoints.StandardVat, Rounding.HalfUp);

            subtotal += split.Net;
            taxTotal += split.Tax;
            grandTotal += line;
        }

        Assert.Equal(grandTotal, subtotal + taxTotal);
    }

    [Fact]
    public void Tax_exclusive_prices_add_the_tax_instead_of_extracting_it()
    {
        // prices.is_tax_inclusive = 0 — wholesale and supplier quoting, which
        // Law 04-02 does not govern because it is not a displayed retail price.
        var split = Dzd(10_000).AddTaxExclusive(BasisPoints.StandardVat, Rounding.HalfEven);

        Assert.Equal(Dzd(10_000), split.Net);
        Assert.Equal(Dzd(1_900), split.Tax);
        Assert.Equal(Dzd(11_900), split.Gross);
    }

    [Fact]
    public void Extracting_then_adding_back_returns_the_line_total()
    {
        for (long ttc = 1; ttc <= 2_000; ttc++)
        {
            var split = Dzd(ttc).SplitTaxInclusive(BasisPoints.StandardVat, Rounding.HalfEven);

            Assert.Equal(Dzd(ttc), split.Net + split.Tax);
        }
    }

    [Fact]
    public void The_tax_is_not_rounded_independently()
    {
        // If the tax were computed as net * rate and rounded on its own, this
        // line would leave a one-centime residual. Deriving it by subtraction is
        // what removes the residual rather than accounting for it.
        // 1,10 TTC at 19% is 0,92 HT. Rounding 0,92 * 19% on its own gives 0,17
        // and the line comes to 1,09 — a centime destroyed on one line of one
        // receipt, which is how a day's takings stop reconciling.
        var line = Dzd(110);
        var split = line.SplitTaxInclusive(BasisPoints.StandardVat, Rounding.HalfEven);

        var independentlyRoundedTax = split.Net.Percent(BasisPoints.StandardVat, Rounding.HalfEven);

        Assert.Equal(line, split.Net + split.Tax);
        Assert.NotEqual(line, split.Net + independentlyRoundedTax);
    }
}
