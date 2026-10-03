using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Session B9, ✍ Hakim: <see cref="Refunds"/>, a refund linked to its sale (D-098). The silent
/// failures: three refunds of one unit each that give back a centime more than the line was paid; a
/// line brought back twice, the second time from nothing; a refund of a sale put on the tab paid out
/// of the drawer while the customer still owes it; an expired yoghurt put back on the shelf.
/// </summary>
public sealed class RefundsTests
{
    private const string Unit = "unit";
    private const string Kg = "kg";

    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    private static Quantity Units(int count) => Quantity.FromThousandths(count * (long)Quantity.Scale, Unit);

    private static Quantity Grams(long grams) => Quantity.FromThousandths(grams, Kg);

    private static SoldRow Counted(int sold, int returned, long total, long tax = 0) =>
        new(Units(sold), Units(returned), Dzd(total), Dzd(tax), Weighed: false);

    private static SoldRow Weighed(long grams, long returnedGrams, long total, long tax = 0) =>
        new(Grams(grams), Grams(returnedGrams), Dzd(total), Dzd(tax), Weighed: true);

    // ------------------------------------------------------------------ Take

    [Fact]
    public void A_quantity_is_taken_from_the_first_row_first()
    {
        // Two batches of milk on one line: 2 from the first, 3 from the second. Bringing 3 back takes
        // the first row's 2, then 1 of the second.
        var take = Refunds.Take([Counted(2, 0, 20_000), Counted(3, 0, 30_000)], Units(3));

        Assert.Equal(ReturnVerdict.Accepted, take.Verdict);
        Assert.Equal([Units(2), Units(1)], take.PerRow);
    }

    [Fact]
    public void What_earlier_refunds_brought_back_is_not_there_to_take_again()
    {
        // The first row is already all back: the second gives everything.
        var take = Refunds.Take([Counted(2, 2, 20_000), Counted(3, 1, 30_000)], Units(2));

        Assert.Equal(ReturnVerdict.Accepted, take.Verdict);
        Assert.Equal([Units(0), Units(2)], take.PerRow);
    }

    [Fact]
    public void A_row_not_reached_is_listed_with_zero()
    {
        var take = Refunds.Take([Counted(5, 0, 50_000), Counted(3, 0, 30_000)], Units(1));

        Assert.Equal([Units(1), Units(0)], take.PerRow);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nothing_brought_back_is_refused(int count)
    {
        var take = Refunds.Take([Counted(2, 0, 20_000)], Units(count));

        Assert.Equal(ReturnVerdict.NotAboveZero, take.Verdict);
        Assert.Empty(take.PerRow);
    }

    [Fact]
    public void More_than_is_left_is_refused_counting_earlier_refunds()
    {
        // Sold 3, 2 already back: one is left, two is too many.
        Assert.Equal(ReturnVerdict.AboveReturnable, Refunds.Take([Counted(3, 2, 30_000)], Units(2)).Verdict);
        Assert.Equal(ReturnVerdict.Accepted, Refunds.Take([Counted(3, 2, 30_000)], Units(1)).Verdict);
    }

    [Fact]
    public void A_line_already_all_back_refuses_even_one()
    {
        Assert.Equal(ReturnVerdict.AboveReturnable, Refunds.Take([Counted(1, 1, 10_000)], Units(1)).Verdict);
    }

    [Fact]
    public void A_counted_line_comes_back_in_whole_units()
    {
        Assert.Equal(ReturnVerdict.NotWhole, Refunds.Take([Counted(3, 0, 30_000)], Quantity.FromThousandths(1_500, Unit)).Verdict);
    }

    [Fact]
    public void A_weighed_line_comes_back_whole_or_not_at_all()
    {
        var tomatoes = Weighed(1_240, 0, 22_320);

        Assert.Equal(ReturnVerdict.NotWhole, Refunds.Take([tomatoes], Grams(600)).Verdict);
        var whole = Refunds.Take([tomatoes], Grams(1_240));
        Assert.Equal(ReturnVerdict.Accepted, whole.Verdict);
        Assert.Equal([Grams(1_240)], whole.PerRow);
    }

    [Fact]
    public void A_weighed_line_over_two_batches_comes_back_as_everything_left()
    {
        var take = Refunds.Take([Weighed(800, 0, 14_400), Weighed(440, 0, 7_920)], Grams(1_240));

        Assert.Equal(ReturnVerdict.Accepted, take.Verdict);
        Assert.Equal([Grams(800), Grams(440)], take.PerRow);
    }

    [Fact]
    public void Too_much_is_said_before_not_whole()
    {
        // A part of a unit that is also more than is left: the cashier is told it is too much.
        Assert.Equal(ReturnVerdict.AboveReturnable, Refunds.Take([Counted(1, 0, 10_000)], Quantity.FromThousandths(1_500, Unit)).Verdict);
    }

    [Fact]
    public void Another_unit_throws()
    {
        Assert.ThrowsAny<InvalidOperationException>(() => Refunds.Take([Counted(3, 0, 30_000)], Grams(1_000)));
    }

    // ------------------------------------------------------------------ Share

    [Fact]
    public void A_row_split_over_its_units_gives_the_first_units_the_odd_centimes()
    {
        // 1 000 over 3 units: 334, 333, 333 (largest remainder, ties to the first).
        var row = Counted(3, 0, 1_000, tax: 160);

        Assert.Equal(new RefundShare(Dzd(334), Dzd(54)), Refunds.Share(row, Units(1)));
    }

    [Fact]
    public void Partial_refunds_of_a_row_sum_exactly_to_what_it_was_paid()
    {
        // The risky rule: one by one, then all at once, the same centimes and never one more.
        var (total, tax) = (Dzd(1_000), Dzd(160));
        var first = Refunds.Share(Counted(3, 0, 1_000, 160), Units(1));
        var second = Refunds.Share(Counted(3, 1, 1_000, 160), Units(1));
        var third = Refunds.Share(Counted(3, 2, 1_000, 160), Units(1));

        Assert.Equal(total, first.Total + second.Total + third.Total);
        Assert.Equal(tax, first.Tax + second.Tax + third.Tax);
        Assert.Equal(new RefundShare(total, tax), Refunds.Share(Counted(3, 0, 1_000, 160), Units(3)));
    }

    [Fact]
    public void Bringing_back_units_after_others_gives_the_next_parts()
    {
        // 1 000 over 3 is 334, 333, 333: the second and third together are 666.
        Assert.Equal(Dzd(666), Refunds.Share(Counted(3, 1, 1_000), Units(2)).Total);

        // One then two, or two then one: the line sums to 1 000 both ways.
        var twoFirst = Refunds.Share(Counted(3, 0, 1_000), Units(2)).Total;
        var oneAfter = Refunds.Share(Counted(3, 2, 1_000), Units(1)).Total;
        Assert.Equal(Dzd(667), twoFirst);
        Assert.Equal(Dzd(1_000), twoFirst + oneAfter);
    }

    [Fact]
    public void A_discounted_row_gives_back_what_was_paid_not_the_shelf_price()
    {
        // Three at 100,00 with 30,00 off the line: 270,00 paid, 90,00 a unit back.
        Assert.Equal(Dzd(9_000), Refunds.Share(Counted(3, 0, 27_000), Units(1)).Total);
    }

    [Fact]
    public void A_row_paid_nothing_gives_back_nothing()
    {
        Assert.Equal(new RefundShare(Dzd(0), Dzd(0)), Refunds.Share(Counted(2, 0, 0), Units(1)));
    }

    [Fact]
    public void A_weighed_row_gives_back_its_whole_total_and_tax()
    {
        Assert.Equal(new RefundShare(Dzd(22_320), Dzd(3_564)), Refunds.Share(Weighed(1_240, 0, 22_320, 3_564), Grams(1_240)));
    }

    [Fact]
    public void Share_refuses_what_take_would_have_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Refunds.Share(Counted(3, 0, 1_000), Units(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Refunds.Share(Counted(3, 2, 1_000), Units(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Refunds.Share(Counted(3, 0, 1_000), Quantity.FromThousandths(500, Unit)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Refunds.Share(Weighed(1_240, 0, 22_320), Grams(600)));
    }

    // ------------------------------------------------------------------ ToTab

    [Fact]
    public void A_sale_on_the_tab_is_refunded_to_the_tab_first()
    {
        // 500,00 back; the sale put 600,00 on the tab; the customer owes 900,00: all 500,00 off the tab.
        Assert.Equal(Dzd(50_000), Refunds.ToTab(Dzd(50_000), Dzd(60_000), Dzd(0), Dzd(90_000)));
    }

    [Fact]
    public void Never_more_back_on_the_tab_than_the_sale_put_there()
    {
        // 1 000,00 back on a sale that was 600,00 tab and 400,00 cash: 600,00 off the tab, the rest is the caller's.
        Assert.Equal(Dzd(60_000), Refunds.ToTab(Dzd(100_000), Dzd(60_000), Dzd(0), Dzd(90_000)));
    }

    [Fact]
    public void Earlier_refunds_of_the_sale_used_up_part_of_its_tab_share()
    {
        // 600,00 on the tab, 450,00 already taken back off by an earlier refund: 150,00 is left of it.
        Assert.Equal(Dzd(15_000), Refunds.ToTab(Dzd(50_000), Dzd(60_000), Dzd(45_000), Dzd(90_000)));
    }

    [Fact]
    public void Never_more_off_the_tab_than_the_customer_owes_now()
    {
        // The customer has repaid most of it: 200,00 still owed, so 200,00 off the tab.
        Assert.Equal(Dzd(20_000), Refunds.ToTab(Dzd(50_000), Dzd(60_000), Dzd(0), Dzd(20_000)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5_000)] // a credit: the customer paid ahead
    public void Nothing_owed_takes_nothing_off_the_tab(long owed)
    {
        Assert.Equal(Dzd(0), Refunds.ToTab(Dzd(50_000), Dzd(60_000), Dzd(0), Dzd(owed)));
    }

    [Fact]
    public void A_sale_not_on_the_tab_puts_nothing_there()
    {
        Assert.Equal(Dzd(0), Refunds.ToTab(Dzd(50_000), Dzd(0), Dzd(0), Dzd(90_000)));
    }

    [Fact]
    public void To_tab_refuses_a_refund_of_nothing_and_a_negative_tab_figure()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Refunds.ToTab(Dzd(0), Dzd(60_000), Dzd(0), Dzd(90_000)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Refunds.ToTab(Dzd(10_000), Dzd(-1), Dzd(0), Dzd(90_000)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Refunds.ToTab(Dzd(10_000), Dzd(60_000), Dzd(-1), Dzd(90_000)));
    }

    [Fact]
    public void To_tab_refuses_another_currency()
    {
        Assert.ThrowsAny<InvalidOperationException>(() =>
            Refunds.ToTab(Dzd(10_000), Money.FromMinorUnits(60_000, Currency.FromCode("EUR")), Dzd(0), Dzd(90_000)));
    }

    // ------------------------------------------------------------------ ToCredit (B9b)

    [Fact]
    public void Store_credit_paid_comes_back_as_store_credit_first()
    {
        // 500,00 left after the tab; the sale paid 300,00 in store credit: 300,00 of it back as credit.
        Assert.Equal(Dzd(30_000), Refunds.ToCredit(Dzd(50_000), Dzd(30_000), Dzd(0)));
    }

    [Fact]
    public void Never_more_back_as_credit_than_the_rest()
    {
        Assert.Equal(Dzd(10_000), Refunds.ToCredit(Dzd(10_000), Dzd(30_000), Dzd(0)));
    }

    [Fact]
    public void Earlier_refunds_used_up_part_of_the_credit_share()
    {
        Assert.Equal(Dzd(5_000), Refunds.ToCredit(Dzd(50_000), Dzd(30_000), Dzd(25_000)));
        Assert.Equal(Dzd(0), Refunds.ToCredit(Dzd(50_000), Dzd(30_000), Dzd(30_000)));
    }

    [Fact]
    public void No_credit_paid_and_no_rest_give_back_nothing_as_credit()
    {
        Assert.Equal(Dzd(0), Refunds.ToCredit(Dzd(50_000), Dzd(0), Dzd(0)));
        Assert.Equal(Dzd(0), Refunds.ToCredit(Dzd(0), Dzd(30_000), Dzd(0)));
    }

    [Fact]
    public void To_credit_refuses_a_negative_figure()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Refunds.ToCredit(Dzd(-1), Dzd(30_000), Dzd(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Refunds.ToCredit(Dzd(100), Dzd(-1), Dzd(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Refunds.ToCredit(Dzd(100), Dzd(30_000), Dzd(-1)));
    }

    // ------------------------------------------------------------------ StatusAfter

    [Fact]
    public void Everything_back_is_refunded()
    {
        Assert.Equal(TransactionStatus.Refunded, Refunds.StatusAfter([Counted(2, 2, 20_000), Weighed(1_240, 1_240, 22_320)]));
    }

    [Fact]
    public void Something_back_and_something_left_is_partially_refunded()
    {
        Assert.Equal(TransactionStatus.PartiallyRefunded, Refunds.StatusAfter([Counted(2, 2, 20_000), Counted(3, 0, 30_000)]));
        Assert.Equal(TransactionStatus.PartiallyRefunded, Refunds.StatusAfter([Counted(3, 1, 30_000)]));
    }

    [Fact]
    public void Nothing_back_is_still_completed()
    {
        Assert.Equal(TransactionStatus.Completed, Refunds.StatusAfter([Counted(2, 0, 20_000)]));
    }

    // ------------------------------------------------------------------ Restocks

    [Theory]
    [InlineData(true, null, true)]      // no expiry: never expires
    [InlineData(true, "2026-10-01", true)]  // its last day is still sellable
    [InlineData(true, "2026-10-02", true)]
    [InlineData(true, "2026-09-30", false)] // the day after: never back on the shelf
    [InlineData(false, null, false)]    // the cashier said no
    [InlineData(false, "2026-12-31", false)]
    public void Back_on_the_shelf_only_when_asked_and_not_expired(bool asked, string? expires, bool restocked)
    {
        var today = new DateOnly(2026, 10, 1);

        Assert.Equal(restocked, Refunds.Restocks(asked, expires is null ? null : DateOnly.Parse(expires, System.Globalization.CultureInfo.InvariantCulture), today));
    }
}
