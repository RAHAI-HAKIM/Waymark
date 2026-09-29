using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Session B4, ✍ Hakim: <see cref="Discounts"/>, a discount given at the counter (D-091). The silent
/// failures: a centime created by rounding a ticket discount line by line; a discount bigger than
/// its line, making a negative line; a split that loses a centime; the store's policy ignored on a
/// half; a discount of nothing recorded as one.
/// </summary>
public sealed class DiscountsTests
{
    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    private static Discount.Percent Percent(int basisPoints) => new(new BasisPoints(basisPoints));

    private static Discount.Amount Amount(long centimes) => new(Dzd(centimes));

    // ------------------------------------------------------------------ on a line

    [Fact]
    public void A_percent_of_a_line_is_its_gross_times_the_rate()
    {
        // The board's line: 10 % of 420,00 is 42,00.
        Assert.Equal(Dzd(4_200), Discounts.OnLine(Dzd(42_000), Percent(1_000), Rounding.HalfUp));
    }

    [Theory]
    [InlineData(Rounding.HalfUp, 1)]
    [InlineData(Rounding.HalfEven, 0)]
    public void A_percent_on_half_a_centime_rounds_by_the_store_policy(Rounding policy, long centimes)
    {
        // 10 % of 0,05 is half a centime.
        Assert.Equal(Dzd(centimes), Discounts.OnLine(Dzd(5), Percent(1_000), policy));
    }

    [Fact]
    public void An_amount_is_taken_as_given()
    {
        Assert.Equal(Dzd(5_000), Discounts.OnLine(Dzd(42_000), Amount(5_000), Rounding.HalfUp));
    }

    [Fact]
    public void An_amount_above_the_line_takes_the_line_and_no_more()
    {
        Assert.Equal(Dzd(42_000), Discounts.OnLine(Dzd(42_000), Amount(50_000), Rounding.HalfUp));
    }

    [Fact]
    public void A_hundred_percent_takes_the_whole_line()
    {
        Assert.Equal(Dzd(42_000), Discounts.OnLine(Dzd(42_000), Percent(10_000), Rounding.HalfUp));
    }

    // A negative rate and one above 100 % cannot even be built: BasisPoints refuses them itself, so
    // they are not this class's to refuse. Zero can be built, and is no discount.
    [Fact]
    public void A_percent_of_zero_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Discounts.OnLine(Dzd(42_000), Percent(0), Rounding.HalfUp));

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void An_amount_not_above_zero_is_refused(long centimes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Discounts.OnLine(Dzd(42_000), Amount(centimes), Rounding.HalfUp));

    // ------------------------------------------------------------------ on the ticket

    [Fact]
    public void A_ticket_percent_is_worked_out_once_on_the_total_not_line_by_line()
    {
        // Three lines of 0,05: 10 % of 0,15 is 0,015, so 0,02 half up. Line by line it would be
        // three halves of a centime rounded up, 0,03: a centime nobody gave.
        var shares = Discounts.OnTicket([Dzd(5), Dzd(5), Dzd(5)], Percent(1_000), Rounding.HalfUp);

        Assert.Equal(Dzd(2), shares.Aggregate(Dzd(0), (sum, share) => sum + share));
    }

    [Fact]
    public void A_ticket_discount_is_split_by_line_total_and_sums_back_exactly()
    {
        // 100,00 DA over 300,00 + 200,00 + 100,00: 50,00 + 33,33… + 16,66…, the last centime to
        // the larger remainder.
        var shares = Discounts.OnTicket([Dzd(30_000), Dzd(20_000), Dzd(10_000)], Amount(10_000), Rounding.HalfUp);

        Assert.Equal([Dzd(5_000), Dzd(3_333), Dzd(1_667)], shares);
    }

    [Fact]
    public void A_ticket_amount_above_the_total_takes_the_total()
    {
        var shares = Discounts.OnTicket([Dzd(30_000), Dzd(10_000)], Amount(90_000), Rounding.HalfUp);

        Assert.Equal([Dzd(30_000), Dzd(10_000)], shares);
    }

    [Fact]
    public void A_line_at_zero_takes_no_share()
    {
        var shares = Discounts.OnTicket([Dzd(20_000), Dzd(0), Dzd(20_000)], Percent(1_000), Rounding.HalfUp);

        Assert.Equal([Dzd(2_000), Dzd(0), Dzd(2_000)], shares);
    }

    [Fact]
    public void A_ticket_that_comes_to_zero_takes_nothing()
    {
        var shares = Discounts.OnTicket([Dzd(0), Dzd(0)], Percent(1_000), Rounding.HalfUp);

        Assert.Equal([Dzd(0), Dzd(0)], shares);
    }

    [Fact]
    public void A_ticket_percent_outside_zero_to_a_hundred_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Discounts.OnTicket([Dzd(100)], Percent(0), Rounding.HalfUp));

    // ------------------------------------------------------------------ over batch rows

    [Fact]
    public void A_lines_discount_is_spread_over_its_rows_and_sums_back_exactly()
    {
        // 10,00 over three rows of equal gross: 3,34 + 3,33 + 3,33.
        var parts = Discounts.Spread(Dzd(1_000), [Dzd(14_300), Dzd(14_300), Dzd(14_300)]);

        Assert.Equal([Dzd(334), Dzd(333), Dzd(333)], parts);
    }

    [Fact]
    public void Rows_of_different_gross_take_their_share_of_it_not_an_equal_one()
    {
        // 10,00 over 143,00 and 286,00: 3,33 and 6,67, never 5,00 each.
        Assert.Equal([Dzd(333), Dzd(667)], Discounts.Spread(Dzd(1_000), [Dzd(14_300), Dzd(28_600)]));
    }

    [Fact]
    public void No_discount_is_zero_on_every_row()
    {
        Assert.Equal([Dzd(0), Dzd(0)], Discounts.Spread(Dzd(0), [Dzd(14_300), Dzd(28_600)]));
    }
}
