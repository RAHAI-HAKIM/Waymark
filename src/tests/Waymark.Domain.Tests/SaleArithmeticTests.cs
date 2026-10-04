using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// A line taken from several batches (D-103). The silent failure: each batch row priced on its own,
/// so the rows came to a centime more or less than the line the customer was shown, and a card part of
/// the till's total was refused as "more than the ticket" (block B review).
/// </summary>
public sealed class SaleArithmeticTests
{
    private static readonly UnitPrecision Kg = UnitPrecision.For("kg", 3);
    private static readonly UnitPrecision Pieces = UnitPrecision.For("piece", 0);
    private static readonly BasisPoints Vat = new(1_900);

    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    [Fact]
    public void A_weighed_line_over_two_batches_is_split_so_the_rows_sum_to_the_line()
    {
        // 0,556 kg at 179,99: 100,07 priced once. 0,300 and 0,256 priced each on its own are 54,00 and 46,08.
        var line = SaleArithmetic.Line(Dzd(17_999), Kg.Quantity(556), Dzd(0), Vat, Rounding.HalfUp).Gross;

        var rows = SaleArithmetic.Split(line, [Kg.Quantity(300), Kg.Quantity(256)]);

        Assert.Equal(Dzd(10_007), line);
        Assert.Equal([Dzd(5_399), Dzd(4_608)], rows);
        Assert.Equal(line, rows[0] + rows[1]);
    }

    [Fact]
    public void A_counted_line_over_two_batches_splits_into_count_times_price_exactly()
    {
        // Nothing was rounded, so there is nothing to spread: 2 and 1 at 143,00 are 286,00 and 143,00.
        var line = SaleArithmetic.Line(Dzd(14_300), Pieces.Whole(3), Dzd(0), Vat, Rounding.HalfUp).Gross;

        Assert.Equal([Dzd(28_600), Dzd(14_300)], SaleArithmetic.Split(line, [Pieces.Whole(2), Pieces.Whole(1)]));
    }

    [Fact]
    public void One_batch_takes_the_whole_line()
    {
        Assert.Equal([Dzd(10_007)], SaleArithmetic.Split(Dzd(10_007), [Kg.Quantity(556)]));
    }

    [Fact]
    public void A_row_takes_its_discount_off_its_share_and_its_tva_out_of_what_is_left()
    {
        var row = SaleArithmetic.Row(Dzd(5_399), Dzd(399), Vat, Rounding.HalfUp);

        Assert.Equal(Dzd(5_399), row.Gross);
        Assert.Equal(Dzd(5_000), row.LineTotal);
        Assert.Equal(row.LineTotal, row.Split.Net + row.Split.Tax);
    }

    [Fact]
    public void A_line_priced_from_its_whole_quantity_is_the_row_of_that_product()
    {
        // Line is Row of quantity × price: the two never disagree.
        var line = SaleArithmetic.Line(Dzd(17_999), Kg.Quantity(556), Dzd(7), Vat, Rounding.HalfUp);

        Assert.Equal(SaleArithmetic.Row(Dzd(10_007), Dzd(7), Vat, Rounding.HalfUp), line);
    }

    [Theory]
    [InlineData(5_399, 4_608, true)]  // the line priced once, split
    [InlineData(5_400, 4_608, false)] // each row its own weight × price: a centime the customer was never shown
    [InlineData(5_400, 4_607, false)] // the right sum, the wrong shares
    public void The_rows_of_a_line_recompute_only_as_the_line_priced_once_and_split(long first, long second, bool holds)
    {
        Assert.Equal(holds, SaleArithmetic.LineRecomputes(
            Dzd(17_999), [(Kg.Quantity(300), Dzd(first)), (Kg.Quantity(256), Dzd(second))], Rounding.HalfUp));
    }

    [Fact]
    public void A_line_on_one_row_recomputes_as_its_quantity_times_its_price()
    {
        Assert.True(SaleArithmetic.LineRecomputes(Dzd(17_999), [(Kg.Quantity(556), Dzd(10_007))], Rounding.HalfUp));
        Assert.False(SaleArithmetic.LineRecomputes(Dzd(17_999), [(Kg.Quantity(556), Dzd(10_008))], Rounding.HalfUp));
        Assert.False(SaleArithmetic.LineRecomputes(Dzd(17_999), [], Rounding.HalfUp));
    }

    [Fact]
    public void The_policy_a_line_was_sold_under_is_the_one_it_recomputes_under()
    {
        // 0,005 kg at 1,00/kg: 0,00 under HalfEven, 0,01 under HalfUp.
        Assert.True(SaleArithmetic.LineRecomputes(Dzd(100), [(Kg.Quantity(5), Dzd(0))], Rounding.HalfEven));
        Assert.False(SaleArithmetic.LineRecomputes(Dzd(100), [(Kg.Quantity(5), Dzd(0))], Rounding.HalfUp));
    }
}
