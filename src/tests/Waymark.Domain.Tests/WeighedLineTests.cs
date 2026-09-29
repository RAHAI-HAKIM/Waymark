using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Session B3, ✍ Hakim: <see cref="WeighedLine"/>, a line sold by weight (D-090, O-26). The silent
/// failures: a label's 100,00 DA charged as 100,08; a weight rounded to the gram in a shop that
/// sells to ten grams; the policy ignored on a tie; a label's price split over two batches that
/// sums to a centime less; and a row that recomputes the wrong way round.
/// </summary>
public sealed class WeighedLineTests
{
    private static readonly UnitPrecision Kg = UnitPrecision.For("kg", 3);

    /// <summary>A unit weighed to ten grams: two decimals.</summary>
    private static readonly UnitPrecision KgTo10g = UnitPrecision.For("kg", 2);

    private static readonly Money Tomatoes = Dzd(18_000); // 180,00 DA/kg

    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    // ------------------------------------------------------------------ by weight

    [Fact]
    public void A_weight_is_exact_and_the_line_is_weight_times_price_rounded_once()
    {
        // 0,556 kg × 180,00 = 100,08.
        var line = WeighedLine.ByWeight(Tomatoes, Kg.Quantity(556), Kg, BasisPoints.ReducedVat, Rounding.HalfUp);

        Assert.Equal(Dzd(10_008), line.Gross);
        Assert.Equal(Dzd(10_008), line.LineTotal);
        Assert.Equal(Dzd(10_008).SplitTaxInclusive(BasisPoints.ReducedVat, Rounding.HalfUp), line.Split);
    }

    [Theory]
    [InlineData(Rounding.HalfUp, 1)]
    [InlineData(Rounding.HalfEven, 0)]
    public void A_weight_on_a_half_centime_rounds_by_the_store_policy(Rounding policy, long centimes)
    {
        // 0,005 kg at 1,00/kg is half a centime.
        var line = WeighedLine.ByWeight(Dzd(100), Kg.Quantity(5), Kg, BasisPoints.ReducedVat, policy);

        Assert.Equal(Dzd(centimes), line.LineTotal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-250)]
    public void A_weight_that_is_not_above_zero_is_refused(long thousandths) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WeighedLine.ByWeight(Tomatoes, Quantity.FromThousandths(thousandths, "kg"), Kg, BasisPoints.ReducedVat, Rounding.HalfUp));

    [Fact]
    public void A_weight_off_the_units_step_is_refused()
    {
        // 0,555 kg in a unit weighed to ten grams.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WeighedLine.ByWeight(Tomatoes, Quantity.FromThousandths(555, "kg"), KgTo10g, BasisPoints.ReducedVat, Rounding.HalfUp));
    }

    // ------------------------------------------------------------------ by declared total (O-26)

    [Fact]
    public void A_labels_price_is_exact_and_the_weight_is_worked_back_from_it()
    {
        // O-26's example: 100,00 DA at 180,00/kg is 0,5555… kg, so 0,556 kg, and the line stays 100,00.
        var line = WeighedLine.ByDeclaredTotal(Tomatoes, Dzd(10_000), Kg, BasisPoints.ReducedVat, Rounding.HalfUp);

        Assert.NotNull(line);
        Assert.Equal(Kg.Quantity(556), line.Value.Quantity);
        Assert.Equal(Dzd(10_000), line.Value.Amounts.LineTotal);
        Assert.Equal(Dzd(10_000), line.Value.Amounts.Gross);
        Assert.Equal(Dzd(10_000).SplitTaxInclusive(BasisPoints.ReducedVat, Rounding.HalfUp), line.Value.Amounts.Split);
    }

    [Theory]
    [InlineData(Rounding.HalfUp, 130)]
    [InlineData(Rounding.HalfEven, 120)]
    public void The_worked_back_weight_rounds_to_the_units_step_by_the_store_policy(Rounding policy, long thousandths)
    {
        // 12,50 DA at 100,00/kg is 0,125 kg: twelve and a half steps of ten grams, a tie.
        var line = WeighedLine.ByDeclaredTotal(Dzd(10_000), Dzd(1_250), KgTo10g, BasisPoints.ReducedVat, policy);

        Assert.NotNull(line);
        Assert.Equal(KgTo10g.Quantity(thousandths), line.Value.Quantity);
        Assert.Equal(Dzd(1_250), line.Value.Amounts.LineTotal);
    }

    [Fact]
    public void A_price_worth_less_than_half_a_step_sells_nothing()
    {
        // 0,05 DA at 180,00/kg is 0,28 g: nothing to take off the shelf.
        Assert.Null(WeighedLine.ByDeclaredTotal(Tomatoes, Dzd(5), Kg, BasisPoints.ReducedVat, Rounding.HalfUp));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10_000)]
    public void A_declared_price_that_is_not_above_zero_is_refused(long centimes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WeighedLine.ByDeclaredTotal(Tomatoes, Dzd(centimes), Kg, BasisPoints.ReducedVat, Rounding.HalfUp));

    [Fact]
    public void A_sell_price_of_zero_throws_rather_than_dividing() =>
        Assert.Throws<DivideByZeroException>(() =>
            WeighedLine.ByDeclaredTotal(Dzd(0), Dzd(10_000), Kg, BasisPoints.ReducedVat, Rounding.HalfUp));

    // ------------------------------------------------------------------ over batches

    [Fact]
    public void A_labels_price_over_two_batches_sums_back_to_the_label()
    {
        // 0,300 + 0,256 kg: 53,957… and 46,043… DA, floors 53,95 + 46,04, the last centime to the
        // larger remainder.
        var parts = WeighedLine.SplitDeclared(Dzd(10_000), [Kg.Quantity(300), Kg.Quantity(256)]);

        Assert.Equal([Dzd(5_396), Dzd(4_604)], parts);
    }

    [Fact]
    public void The_shares_keep_the_order_the_batches_gave()
    {
        var parts = WeighedLine.SplitDeclared(Dzd(10_000), [Kg.Quantity(256), Kg.Quantity(300)]);

        Assert.Equal([Dzd(4_604), Dzd(5_396)], parts);
    }

    [Fact]
    public void A_label_split_three_ways_still_sums_to_the_label()
    {
        // 100,00 DA over three equal batches is 33,333… each: rounded part by part that is 99,99,
        // a centime lost. Allocate gives the leftover centime to the first.
        var parts = WeighedLine.SplitDeclared(Dzd(10_000), [Kg.Quantity(100), Kg.Quantity(100), Kg.Quantity(100)]);

        Assert.Equal([Dzd(3_334), Dzd(3_333), Dzd(3_333)], parts);
    }

    [Fact]
    public void One_batch_takes_the_whole_label()
    {
        var parts = WeighedLine.SplitDeclared(Dzd(10_000), [Kg.Quantity(556)]);

        Assert.Equal([Dzd(10_000)], parts);
    }

    // ------------------------------------------------------------------ the row recomputes (D-053)

    [Theory]
    [InlineData(28_600, true)]
    [InlineData(28_601, false)]
    public void A_counted_row_is_count_times_price(long lineTotal, bool holds)
    {
        var pieces = UnitPrecision.For("piece", 0);

        Assert.Equal(holds, WeighedLine.Recomputes(
            QuantitySource.Count, Dzd(14_300), pieces.Whole(2), Dzd(0), Dzd(lineTotal), pieces, Rounding.HalfUp));
    }

    [Fact]
    public void A_counted_row_takes_its_discount_off()
    {
        var pieces = UnitPrecision.For("piece", 0);

        Assert.True(WeighedLine.Recomputes(
            QuantitySource.Count, Dzd(14_300), pieces.Whole(2), Dzd(600), Dzd(28_000), pieces, Rounding.HalfUp));
    }

    [Theory]
    [InlineData(QuantitySource.TypedWeight, 10_008, true)]
    [InlineData(QuantitySource.LabelWeight, 10_008, true)]
    [InlineData(QuantitySource.TypedWeight, 10_000, false)] // O-26's row without its source: it does not recompute
    public void A_weighed_row_is_weight_times_price_exactly(QuantitySource source, long lineTotal, bool holds) =>
        Assert.Equal(holds, WeighedLine.Recomputes(
            source, Tomatoes, Kg.Quantity(556), Dzd(0), Dzd(lineTotal), Kg, Rounding.HalfUp));

    [Theory]
    [InlineData(556, true)]  // 0,5555… rounded
    [InlineData(555, true)]  // within a step: a share Allocate rounded again can land here
    [InlineData(557, false)] // 1,4 steps off: not this label's weight
    [InlineData(554, false)]
    public void A_label_price_row_is_its_total_divided_by_its_price_within_one_step(long thousandths, bool holds) =>
        Assert.Equal(holds, WeighedLine.Recomputes(
            QuantitySource.LabelPrice, Tomatoes, Kg.Quantity(thousandths), Dzd(0), Dzd(10_000), Kg, Rounding.HalfUp));

    [Theory]
    [InlineData(Rounding.HalfEven, true)]
    [InlineData(Rounding.HalfUp, false)]
    public void A_row_recomputes_under_the_policy_it_was_sold_under(Rounding policy, bool holds) =>
        // 0,005 kg at 1,00/kg recorded as 0,00: right under HalfEven, a centime short under HalfUp.
        Assert.Equal(holds, WeighedLine.Recomputes(
            QuantitySource.TypedWeight, Dzd(100), Kg.Quantity(5), Dzd(0), Dzd(0), Kg, policy));
}
