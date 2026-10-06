using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Session C1, ✍ Hakim: <see cref="Drawer"/>, what a cash session should hold and what its count says
/// (D-034, D-111). The silent failures: a tab repayment's rounding counted twice, so every evening the
/// drawer is "short" by a few dinars and the owner learns to ignore the figure; a variance with its
/// sign turned, so a shortage reads as an excess; a threshold off by one, so the note is never asked.
/// </summary>
public sealed class DrawerTests
{
    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    private static DrawerMovements Day(
        long opening = 0, long sales = 0, long refunds = 0, long paidIn = 0, long tab = 0, long paidOut = 0, long drops = 0,
        params TenderRounding[] rounding) =>
        new(Dzd(opening), Dzd(sales), Dzd(refunds), Dzd(paidIn), Dzd(tab), Dzd(paidOut), Dzd(drops), rounding);

    private static TenderRounding Ticket(long centimes) => new(VarianceReferenceType.Transaction, Dzd(centimes));

    // ------------------------------------------------------------------ Expected

    [Fact]
    public void A_drawer_nothing_happened_to_holds_its_float()
    {
        Assert.Equal(Dzd(500_000), Drawer.Expected(Day(opening: 500_000)));
    }

    [Fact]
    public void A_session_with_no_float_and_no_movement_expects_zero_not_nothing()
    {
        Assert.Equal(Dzd(0), Drawer.Expected(Day()));
    }

    [Fact]
    public void Each_figure_moves_the_drawer_its_own_way()
    {
        Assert.Equal(Dzd(110_000), Drawer.Expected(Day(opening: 100_000, sales: 10_000)));
        Assert.Equal(Dzd(90_000), Drawer.Expected(Day(opening: 100_000, refunds: 10_000)));
        Assert.Equal(Dzd(110_000), Drawer.Expected(Day(opening: 100_000, paidIn: 10_000)));
        Assert.Equal(Dzd(110_000), Drawer.Expected(Day(opening: 100_000, tab: 10_000)));
        Assert.Equal(Dzd(90_000), Drawer.Expected(Day(opening: 100_000, paidOut: 10_000)));
        Assert.Equal(Dzd(90_000), Drawer.Expected(Day(opening: 100_000, drops: 10_000)));
    }

    [Fact]
    public void A_whole_day_adds_up_as_the_generators_drawer_does()
    {
        // 5 000,00 + 40 118,00 − 620,00 + 2 000,00 + 1 280,00 − 1 350,00 − 3 000,00 + 2,00
        var day = Day(opening: 500_000, sales: 4_011_800, refunds: 62_000, paidIn: 200_000, tab: 128_000, paidOut: 135_000, drops: 300_000, Ticket(200));

        Assert.Equal(Dzd(4_343_000), Drawer.Expected(day));
    }

    [Theory]
    [InlineData(200)]   // 143,00 on the ticket, 145,00 in the drawer
    [InlineData(-200)]  // the step took 2,00 less than the tickets say
    public void A_tickets_tender_rounding_is_cash_the_payment_rows_do_not_show(long rounding)
    {
        Assert.Equal(Dzd(100_000 + 14_300 + rounding), Drawer.Expected(Day(opening: 100_000, sales: 14_300, rounding: Ticket(rounding))));
    }

    [Fact]
    public void A_tab_repayments_rounding_is_never_added_its_paid_in_is_the_rounded_cash_already()
    {
        // 286,00 owed, 285,00 taken (D-108): the paid_in says 285,00 and the rounding row −1,00.
        // Adding the row makes the drawer expect 284,00 for the 285,00 that are in it.
        var day = Day(opening: 100_000, tab: 28_500, rounding: new TenderRounding(VarianceReferenceType.ReceivableMovement, Dzd(-100)));

        Assert.Equal(Dzd(128_500), Drawer.Expected(day));
    }

    [Fact]
    public void Only_a_tickets_rounding_counts_whatever_else_the_rows_point_at()
    {
        var rows = Enum.GetValues<VarianceReferenceType>().Select(reference => new TenderRounding(reference, Dzd(700))).ToArray();

        // One row per kind, 7,00 each: the ticket's alone reaches the drawer.
        Assert.Equal(Dzd(100_700), Drawer.Expected(Day(opening: 100_000, rounding: rows)));
    }

    [Fact]
    public void Several_tickets_roundings_are_summed_with_their_signs()
    {
        Assert.Equal(Dzd(100_100), Drawer.Expected(Day(opening: 100_000, rounding: [Ticket(200), Ticket(-200), Ticket(100)])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void A_figure_below_zero_throws_the_direction_is_its_name(int which)
    {
        var figures = new long[7];
        figures[which] = -1;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Drawer.Expected(Day(figures[0], figures[1], figures[2], figures[3], figures[4], figures[5], figures[6])));
    }

    [Fact]
    public void A_drawer_may_be_expected_below_zero_that_is_what_the_count_is_for()
    {
        // More given out than ever came in: wrong, and exactly what the Z must show rather than hide.
        Assert.Equal(Dzd(-5_000), Drawer.Expected(Day(opening: 10_000, paidOut: 15_000)));
    }

    [Fact]
    public void A_figure_in_another_currency_throws()
    {
        var day = Day(opening: 100_000) with { CashSales = Money.FromMinorUnits(100, Currency.Eur) };

        Assert.Throws<InvalidOperationException>(() => Drawer.Expected(day));
    }

    // ------------------------------------------------------------------ Variance

    [Theory]
    [InlineData(4_835_000, 4_800_000, -35_000)] // 350,00 short
    [InlineData(4_792_000, 4_800_000, 8_000)]   // 80,00 over
    [InlineData(4_800_000, 4_800_000, 0)]
    public void The_variance_is_counted_less_expected_so_a_shortage_is_negative(long expected, long counted, long variance)
    {
        Assert.Equal(Dzd(variance), Drawer.Variance(Dzd(expected), Dzd(counted)));
    }

    [Fact]
    public void An_empty_drawer_is_a_count_of_zero()
    {
        Assert.Equal(Dzd(-100_000), Drawer.Variance(Dzd(100_000), Dzd(0)));
    }

    [Fact]
    public void A_count_below_zero_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Drawer.Variance(Dzd(100_000), Dzd(-1)));
    }

    [Fact]
    public void A_count_in_another_currency_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Drawer.Variance(Dzd(100_000), Money.FromMinorUnits(100_000, Currency.Eur)));
    }

    // ------------------------------------------------------------------ NeedsNote

    [Theory]
    [InlineData(-35_000, true)]  // 350,00 short of a 200,00 threshold
    [InlineData(-12_000, false)]
    [InlineData(-20_000, false)] // exactly the threshold
    [InlineData(-20_001, true)]  // a centime past it
    [InlineData(20_000, false)]
    [InlineData(20_001, true)]   // too much is asked about as too little is
    [InlineData(0, false)]
    public void A_note_is_asked_past_the_threshold_short_or_over(long variance, bool needed)
    {
        Assert.Equal(needed, Drawer.NeedsNote(Dzd(variance), Dzd(20_000)));
    }

    [Fact]
    public void With_no_threshold_no_note_is_ever_asked()
    {
        Assert.False(Drawer.NeedsNote(Dzd(-10_000_000), null));
    }

    [Fact]
    public void A_threshold_of_zero_asks_about_every_variance_and_not_about_none()
    {
        Assert.True(Drawer.NeedsNote(Dzd(-1), Dzd(0)));
        Assert.False(Drawer.NeedsNote(Dzd(0), Dzd(0)));
    }

    [Fact]
    public void A_threshold_below_zero_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Drawer.NeedsNote(Dzd(-100), Dzd(-1)));
    }

    // ------------------------------------------------------------------ Covers (F-35)

    [Theory]
    [InlineData(50_000, 49_999, true)]
    [InlineData(50_000, 50_000, true)]  // the drawer emptied to the last coin
    [InlineData(50_000, 50_001, false)]
    [InlineData(0, 1, false)]
    [InlineData(-5_000, 1, false)]
    public void The_drawer_covers_cash_going_out_up_to_what_it_should_hold(long expected, long goingOut, bool covered)
    {
        Assert.Equal(covered, Drawer.Covers(Dzd(expected), Dzd(goingOut)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Asking_the_drawer_for_nothing_or_less_throws(long goingOut)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Drawer.Covers(Dzd(50_000), Dzd(goingOut)));
    }
}
