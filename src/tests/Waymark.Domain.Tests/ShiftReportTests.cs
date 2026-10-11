using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Session C2: <see cref="ShiftReport"/>, a cash session's X and Z (D-112). The silent
/// failures: a refund netted into the sales so the day reads smaller and no refund is seen; cash
/// reported as the drawer took it, so the takings no longer match the tickets; a struck line counted
/// in a total; an override's gift worked out row by row and a centime off the line; a cancel flagged by
/// a second copy of the rule that has drifted from the one the server asked.
/// </summary>
public sealed class ShiftReportTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    private static ReportTicket Sale(string id, long total, string staff = "nabil", string? ticketDiscount = null, Rounding policy = Rounding.HalfUp) =>
        new(id, TicketKind.Sale, staff, Morning, Dzd(total), false, ticketDiscount, policy);

    private static ReportTicket Refund(string id, long givenBack, string staff = "nabil") =>
        new(id, TicketKind.Refund, staff, Morning, Dzd(-givenBack), false, null, Rounding.HalfUp);

    private static ReportTicket Cancel(string id, long worth, string staff = "nabil", bool opened = false, int minute = 0) =>
        new(id, TicketKind.Cancel, staff, Morning.AddMinutes(minute), Dzd(worth), opened, null, Rounding.HalfUp);

    private static ReportPart Part(string ticket, PaymentMethod method, long amount) => new(ticket, method, Dzd(amount));

    private static ReportRow Row(
        string ticket, int line, long total, long discount = 0, string? reason = null, bool struck = false, long units = 1, long? listPrice = null) =>
        new(ticket, line, struck, units * 1000, Dzd(total), Dzd(discount), reason, listPrice is { } list ? Dzd(list) : null);

    // ------------------------------------------------------------------ TakingsOf

    [Fact]
    public void A_session_with_nothing_in_it_took_nothing_in_the_ledgers_currency()
    {
        var takings = ShiftReport.TakingsOf(Currency.Dzd, [], []);

        Assert.Empty(takings.Sales);
        Assert.Empty(takings.Refunds);
        Assert.Equal((Dzd(0), Dzd(0), Dzd(0)), (takings.SalesTotal, takings.RefundsTotal, takings.Net));
    }

    [Fact]
    public void Each_tender_used_has_its_line_with_its_parts_and_their_sum_in_the_enums_order()
    {
        // t1 in cash; t2 split, card then cash; t3 on the tab. Given out of order on purpose.
        var tickets = new[] { Sale("t1", 14_300), Sale("t2", 40_650), Sale("t3", 9_000) };
        var parts = new[]
        {
            Part("t3", PaymentMethod.OnAccount, 9_000),
            Part("t2", PaymentMethod.Card, 20_000),
            Part("t1", PaymentMethod.Cash, 14_300),
            Part("t2", PaymentMethod.Cash, 20_650),
        };

        var takings = ShiftReport.TakingsOf(Currency.Dzd, tickets, parts);

        Assert.Equal(
            [
                new TenderLine(PaymentMethod.Cash, 2, Dzd(34_950)),
                new TenderLine(PaymentMethod.Card, 1, Dzd(20_000)),
                new TenderLine(PaymentMethod.OnAccount, 1, Dzd(9_000)),
            ],
            takings.Sales);
        Assert.Equal(Dzd(63_950), takings.SalesTotal);
    }

    [Fact]
    public void Cash_is_what_the_tickets_say_never_what_the_drawer_took()
    {
        // 143,00 on the ticket, 145,00 in the drawer: the 2,00 is the drawer's line (D-034), not a taking.
        var takings = ShiftReport.TakingsOf(Currency.Dzd, [Sale("t1", 14_300)], [Part("t1", PaymentMethod.Cash, 14_300)]);

        Assert.Equal(Dzd(14_300), takings.Sales.Single().Amount);
    }

    [Fact]
    public void A_refund_is_its_own_line_shown_as_what_it_gave_back_and_never_netted_into_the_sales()
    {
        var tickets = new[] { Sale("t1", 28_600), Refund("r1", 14_300), Refund("r2", 5_000) };
        var parts = new[]
        {
            Part("t1", PaymentMethod.Cash, 28_600),
            Part("r1", PaymentMethod.Cash, -14_300),
            Part("r2", PaymentMethod.StoreCredit, -5_000),
        };

        var takings = ShiftReport.TakingsOf(Currency.Dzd, tickets, parts);

        Assert.Equal([new TenderLine(PaymentMethod.Cash, 1, Dzd(28_600))], takings.Sales);
        Assert.Equal([new TenderLine(PaymentMethod.Cash, 1, Dzd(14_300)), new TenderLine(PaymentMethod.StoreCredit, 1, Dzd(5_000))], takings.Refunds);
        Assert.Equal((Dzd(28_600), Dzd(19_300), Dzd(9_300)), (takings.SalesTotal, takings.RefundsTotal, takings.Net));
    }

    [Fact]
    public void A_day_of_refunds_alone_nets_below_zero_and_says_so()
    {
        var takings = ShiftReport.TakingsOf(Currency.Dzd, [Refund("r1", 14_300)], [Part("r1", PaymentMethod.Cash, -14_300)]);

        Assert.Equal(Dzd(-14_300), takings.Net);
    }

    [Fact]
    public void A_payment_on_a_cancelled_ticket_throws_nothing_was_sold_on_it()
    {
        Assert.Throws<ArgumentException>(() =>
            ShiftReport.TakingsOf(Currency.Dzd, [Cancel("c1", 14_300)], [Part("c1", PaymentMethod.Cash, 14_300)]));
    }

    [Fact]
    public void A_payment_on_a_ticket_that_is_not_the_sessions_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ShiftReport.TakingsOf(Currency.Dzd, [Sale("t1", 14_300)], [Part("elsewhere", PaymentMethod.Cash, 14_300)]));
    }

    [Theory]
    [InlineData(TicketKind.Sale, -100)]  // a sale that gave money back
    [InlineData(TicketKind.Refund, 100)] // a refund that took some
    public void A_part_whose_sign_is_not_its_tickets_throws(TicketKind kind, long amount)
    {
        var ticket = kind == TicketKind.Sale ? Sale("t", 100) : Refund("t", 100);

        Assert.Throws<ArgumentException>(() => ShiftReport.TakingsOf(Currency.Dzd, [ticket], [Part("t", PaymentMethod.Cash, amount)]));
    }

    [Fact]
    public void A_part_in_another_currency_throws()
    {
        Assert.Throws<InvalidOperationException>(() => ShiftReport.TakingsOf(
            Currency.Dzd,
            [Sale("t1", 100), Sale("t2", 100)],
            [Part("t1", PaymentMethod.Cash, 100), new ReportPart("t2", PaymentMethod.Cash, Money.FromMinorUnits(100, Currency.Eur))]));
    }

    // ------------------------------------------------------------------ Reconciles

    [Fact]
    public void The_takings_reconcile_when_they_come_to_the_tickets_totals()
    {
        var tickets = new[] { Sale("t1", 40_650), Refund("r1", 14_300), Cancel("c1", 9_999) };
        var takings = ShiftReport.TakingsOf(
            Currency.Dzd, tickets,
            [Part("t1", PaymentMethod.Card, 20_000), Part("t1", PaymentMethod.Cash, 20_650), Part("r1", PaymentMethod.Cash, -14_300)]);

        Assert.True(ShiftReport.Reconciles(takings, tickets)); // the cancel is in neither side
    }

    [Theory]
    [InlineData(20_649, -14_300)] // a centime short on a sale
    [InlineData(20_650, -14_299)] // a centime short on a refund
    public void A_centime_between_the_parts_and_the_tickets_does_not_reconcile(long cashPart, long refundPart)
    {
        var tickets = new[] { Sale("t1", 40_650), Refund("r1", 14_300) };
        var takings = ShiftReport.TakingsOf(
            Currency.Dzd, tickets,
            [Part("t1", PaymentMethod.Card, 20_000), Part("t1", PaymentMethod.Cash, cashPart), Part("r1", PaymentMethod.Cash, refundPart)]);

        Assert.False(ShiftReport.Reconciles(takings, tickets));
    }

    [Fact]
    public void A_sale_with_no_payment_row_does_not_reconcile()
    {
        var tickets = new[] { Sale("t1", 14_300), Sale("t2", 5_000) };
        var takings = ShiftReport.TakingsOf(Currency.Dzd, tickets, [Part("t1", PaymentMethod.Cash, 14_300)]);

        Assert.False(ShiftReport.Reconciles(takings, tickets));
    }

    // ------------------------------------------------------------------ ActivityOf

    [Fact]
    public void Tickets_are_counted_by_kind_each_with_what_it_comes_to_above_zero()
    {
        var tickets = new[] { Sale("t1", 14_300), Sale("t2", 40_650), Refund("r1", 14_300), Cancel("c1", 9_000), Cancel("c2", 1_000) };

        var activity = ShiftReport.ActivityOf(Currency.Dzd, tickets, []);

        Assert.Equal(new Tally(2, Dzd(54_950)), activity.Sold);
        Assert.Equal(new Tally(1, Dzd(14_300)), activity.Refunded);
        Assert.Equal(new Tally(2, Dzd(10_000)), activity.Cancelled);
        Assert.Equal(new Tally(0, Dzd(0)), activity.StruckLines);
    }

    [Fact]
    public void Struck_lines_are_counted_by_line_whatever_the_ticket_and_the_rows_of_one_line_once()
    {
        var tickets = new[] { Sale("t1", 14_300), Cancel("c1", 0) };
        var rows = new[]
        {
            Row("t1", 1, 14_300),                 // sold: not a struck line
            Row("t1", 2, 6_000, struck: true),    // one struck line over two batches
            Row("t1", 2, 3_000, struck: true),
            Row("c1", 1, 2_500, struck: true),    // a ticket struck empty, recorded as a cancel (D-106)
        };

        var activity = ShiftReport.ActivityOf(Currency.Dzd, tickets, rows);

        Assert.Equal(new Tally(2, Dzd(11_500)), activity.StruckLines);
        Assert.Equal(new Tally(1, Dzd(14_300)), activity.Sold); // the ticket's own total, nothing struck in it
    }

    // ------------------------------------------------------------------ ReductionsOf

    [Fact]
    public void With_nothing_given_away_every_tally_is_zero()
    {
        var reductions = ShiftReport.ReductionsOf(Currency.Dzd, [Sale("t1", 14_300)], [Row("t1", 1, 14_300)]);

        Assert.Equal(new Tally(0, Dzd(0)), reductions.LineDiscounts);
        Assert.Equal(new Tally(0, Dzd(0)), reductions.TicketDiscounts);
        Assert.Equal(new Tally(0, Dzd(0)), reductions.Overrides);
        Assert.Equal(new Tally(0, Dzd(0)), reductions.Total);
    }

    [Fact]
    public void A_line_discount_is_counted_by_line_over_its_batch_rows()
    {
        // One line of 3 over two batches, 1,00 off in all; another line with nothing off.
        var rows = new[] { Row("t1", 1, 28_533, 67, "geste"), Row("t1", 1, 14_267, 33, "geste"), Row("t1", 2, 12_050) };

        var reductions = ShiftReport.ReductionsOf(Currency.Dzd, [Sale("t1", 54_850)], rows);

        Assert.Equal(new Tally(1, Dzd(100)), reductions.LineDiscounts);
        Assert.Equal(new Tally(0, Dzd(0)), reductions.TicketDiscounts);
    }

    [Fact]
    public void A_ticket_discount_is_counted_by_ticket_over_all_its_lines()
    {
        // 5 % off the ticket, spread over its two lines, each row carrying the ticket's reason.
        var rows = new[] { Row("t1", 1, 13_585, 715, "fidelite"), Row("t1", 2, 11_447, 603, "fidelite") };

        var reductions = ShiftReport.ReductionsOf(Currency.Dzd, [Sale("t1", 25_032, ticketDiscount: "fidelite")], rows);

        Assert.Equal(new Tally(1, Dzd(1_318)), reductions.TicketDiscounts);
        Assert.Equal(new Tally(0, Dzd(0)), reductions.LineDiscounts);
    }

    [Fact]
    public void A_line_that_keeps_its_own_reason_on_a_discounted_ticket_is_the_lines()
    {
        // Line 1 had its own discount ("abime") and its share of the ticket's inside the same figure:
        // the row cannot say which is which, so it is counted with the line's (F-41).
        var rows = new[] { Row("t1", 1, 12_000, 2_300, "abime"), Row("t1", 2, 11_447, 603, "fidelite") };

        var reductions = ShiftReport.ReductionsOf(Currency.Dzd, [Sale("t1", 23_447, ticketDiscount: "fidelite")], rows);

        Assert.Equal(new Tally(1, Dzd(2_300)), reductions.LineDiscounts);
        Assert.Equal(new Tally(1, Dzd(603)), reductions.TicketDiscounts);
        Assert.Equal(Dzd(2_903), reductions.Total.Value); // whatever the split, the total is exact
    }

    [Fact]
    public void Refunds_cancels_and_struck_rows_give_nothing_away()
    {
        var tickets = new[] { Sale("t1", 14_300), Refund("r1", 13_300), Cancel("c1", 13_300) };
        var rows = new[]
        {
            Row("t1", 1, 14_300),
            Row("t1", 2, 9_000, 1_000, "geste", struck: true),
            Row("r1", 1, -13_300, -1_000, "geste"),
            Row("c1", 1, 13_300, 1_000, "geste", listPrice: 20_000),
        };

        Assert.Equal(new Tally(0, Dzd(0)), ShiftReport.ReductionsOf(Currency.Dzd, tickets, rows).Total);
    }

    [Fact]
    public void An_override_gives_away_the_list_price_less_what_the_line_came_to_before_its_discount()
    {
        // 3 units listed at 150,00 sold at 120,00, with 10,00 off besides: 90,00 by the price, 10,00 by the discount.
        var rows = new[] { Row("t1", 1, 35_000, 1_000, "geste", units: 3, listPrice: 15_000) };

        var reductions = ShiftReport.ReductionsOf(Currency.Dzd, [Sale("t1", 35_000)], rows);

        Assert.Equal(new Tally(1, Dzd(9_000)), reductions.Overrides);
        Assert.Equal(new Tally(1, Dzd(1_000)), reductions.LineDiscounts);
        Assert.Equal(new Tally(2, Dzd(10_000)), reductions.Total);
    }

    [Fact]
    public void An_overridden_line_is_priced_once_at_its_list_price_not_row_by_row()
    {
        // 0,670 kg over two batches of 0,335, listed at 1 285,50 /kg, sold for 800,00. Row by row the
        // list comes to 430,64 + 430,64 = 861,28; the line priced once comes to 861,29 (861,285 half up).
        var rows = new[]
        {
            new ReportRow("t1", 1, false, 335, Dzd(40_000), Dzd(0), null, Dzd(128_550)),
            new ReportRow("t1", 1, false, 335, Dzd(40_000), Dzd(0), null, Dzd(128_550)),
        };

        var reductions = ShiftReport.ReductionsOf(Currency.Dzd, [Sale("t1", 80_000)], rows);

        Assert.Equal(new Tally(1, Dzd(86_129 - 80_000)), reductions.Overrides);
    }

    [Fact]
    public void An_override_is_priced_by_its_own_tickets_policy()
    {
        // 0,670 kg at 1 285,50: 861,285, which is 861,29 half up and 861,28 half even.
        var row = new ReportRow("t1", 1, false, 670, Dzd(80_000), Dzd(0), null, Dzd(128_550));

        var halfEven = ShiftReport.ReductionsOf(Currency.Dzd, [Sale("t1", 80_000, policy: Rounding.HalfEven)], [row]);

        Assert.Equal(Dzd(86_128 - 80_000), halfEven.Overrides.Value);
    }

    [Fact]
    public void A_price_raised_is_an_override_that_gave_nothing_away_and_is_summed_as_it_is()
    {
        // Listed 100,00, sold 120,00: the shop took 20,00 more.
        var reductions = ShiftReport.ReductionsOf(Currency.Dzd, [Sale("t1", 12_000)], [Row("t1", 1, 12_000, listPrice: 10_000)]);

        Assert.Equal(new Tally(1, Dzd(-2_000)), reductions.Overrides);
    }

    // ------------------------------------------------------------------ FlagsOf

    private static readonly ReportThresholds None = new(null, null, null);

    [Fact]
    public void A_quiet_session_has_no_flag()
    {
        Assert.Empty(ShiftReport.FlagsOf([Sale("t1", 14_300), Cancel("c1", 14_300)], new ReportThresholds(3, Dzd(500_000), Dzd(20_000)), Dzd(-5_000)));
    }

    [Fact]
    public void A_cancel_after_encaisser_is_flagged_whatever_the_thresholds()
    {
        var flags = ShiftReport.FlagsOf([Cancel("c1", 14_300, "yacine", opened: true)], None, null);

        Assert.Equal([new ShiftFlag(ShiftFlagKind.CancelAfterEncaisser, "yacine", "c1", Dzd(14_300), null)], flags);
    }

    [Fact]
    public void One_person_past_the_count_is_flagged_once_with_their_count_and_nobody_else_is()
    {
        // The threshold is 2 a session: Yacine cancels three, Nabil two.
        var tickets = new[]
        {
            Cancel("y1", 100, "yacine", minute: 1), Cancel("n1", 100, "nabil", minute: 2), Cancel("y2", 100, "yacine", minute: 3),
            Cancel("n2", 100, "nabil", minute: 4), Cancel("y3", 100, "yacine", minute: 5),
        };

        var flags = ShiftReport.FlagsOf(tickets, new ReportThresholds(2, null, null), null);

        Assert.Equal([new ShiftFlag(ShiftFlagKind.TooManyCancels, "yacine", null, null, 3)], flags);
    }

    [Fact]
    public void Somebody_far_past_the_count_is_still_flagged_once_with_the_count_they_ended_on()
    {
        // Every cancel past the threshold is one the rule flags; the report says it once a person.
        var tickets = new[] { Cancel("y1", 100, "yacine", minute: 1), Cancel("y2", 100, "yacine", minute: 2), Cancel("y3", 100, "yacine", minute: 3) };

        var flags = ShiftReport.FlagsOf(tickets, new ReportThresholds(1, null, null), null);

        Assert.Equal([new ShiftFlag(ShiftFlagKind.TooManyCancels, "yacine", null, null, 3)], flags);
    }

    [Fact]
    public void A_cancel_above_the_value_is_flagged_and_one_worth_exactly_it_is_not()
    {
        var tickets = new[] { Cancel("c1", 150_000, minute: 1), Cancel("c2", 150_001, minute: 2) };

        var flags = ShiftReport.FlagsOf(tickets, new ReportThresholds(null, Dzd(150_000), null), null);

        Assert.Equal([new ShiftFlag(ShiftFlagKind.CancelAboveValue, "nabil", "c2", Dzd(150_001), null)], flags);
    }

    [Theory]
    [InlineData(-35_000, true)]
    [InlineData(-20_000, false)] // exactly the threshold
    [InlineData(20_001, true)]   // an excess is asked about as a shortage is
    public void The_variance_is_flagged_past_its_threshold_as_the_drawer_says(long variance, bool flagged)
    {
        var flags = ShiftReport.FlagsOf([], new ReportThresholds(null, null, Dzd(20_000)), Dzd(variance));

        Assert.Equal(flagged ? [new ShiftFlag(ShiftFlagKind.VariancePastThreshold, null, null, Dzd(variance), null)] : [], flags);
    }

    [Fact]
    public void An_open_session_has_no_variance_to_flag()
    {
        Assert.Empty(ShiftReport.FlagsOf([], new ReportThresholds(null, null, Dzd(0)), null));
    }

    [Fact]
    public void Every_flag_comes_in_the_reports_order_each_kind_in_the_order_things_happened()
    {
        var tickets = new[]
        {
            Cancel("b", 200_000, "yacine", opened: true, minute: 20),
            Cancel("a", 100, "nabil", opened: true, minute: 10),
            Cancel("c", 300_000, "yacine", minute: 30),
            Sale("t1", 14_300),
        };

        var flags = ShiftReport.FlagsOf(tickets, new ReportThresholds(1, Dzd(150_000), Dzd(20_000)), Dzd(-35_000));

        Assert.Equal(
            [
                new ShiftFlag(ShiftFlagKind.CancelAfterEncaisser, "nabil", "a", Dzd(100), null),
                new ShiftFlag(ShiftFlagKind.CancelAfterEncaisser, "yacine", "b", Dzd(200_000), null),
                new ShiftFlag(ShiftFlagKind.VariancePastThreshold, null, null, Dzd(-35_000), null),
                new ShiftFlag(ShiftFlagKind.TooManyCancels, "yacine", null, null, 2),
                new ShiftFlag(ShiftFlagKind.CancelAboveValue, "yacine", "b", Dzd(200_000), null),
                new ShiftFlag(ShiftFlagKind.CancelAboveValue, "yacine", "c", Dzd(300_000), null),
            ],
            flags);
    }
}
