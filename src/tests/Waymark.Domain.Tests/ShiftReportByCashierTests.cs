using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Session C3: <see cref="ShiftReport.ByCashierOf"/>, a session's figures by who did each thing
/// (D-118). The silent failures: a refund counted to whoever sold the ticket weeks ago; a struck line
/// counted to the ticket's cashier when somebody else struck it; a ticket or a cash movement that
/// falls in nobody's column, so the people no longer come to the session; a person who only opened the
/// drawer left out, so the hours start late.
/// </summary>
public sealed class ShiftReportByCashierTests
{
    private const string Nabil = "nabil";
    private const string Samia = "samia";
    private const string Karim = "karim";

    private static readonly DateTimeOffset Opening = new(2026, 10, 5, 8, 12, 0, TimeSpan.Zero);

    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    private static DateTimeOffset At(int minutes) => Opening.AddMinutes(minutes);

    private static ReportTicket Sale(string id, long total, string staff, int minute, string? ticketDiscount = null) =>
        new(id, TicketKind.Sale, staff, At(minute), Dzd(total), false, ticketDiscount, Rounding.HalfUp);

    private static ReportTicket Refund(string id, long givenBack, string staff, int minute) =>
        new(id, TicketKind.Refund, staff, At(minute), Dzd(-givenBack), false, null, Rounding.HalfUp);

    private static ReportTicket Cancel(string id, long worth, string staff, int minute) =>
        new(id, TicketKind.Cancel, staff, At(minute), Dzd(worth), false, null, Rounding.HalfUp);

    private static ReportPart Part(string ticket, PaymentMethod method, long amount) => new(ticket, method, Dzd(amount));

    private static ReportRow Row(string ticket, int line, long total, long discount = 0, string? reason = null, long? listPrice = null) =>
        new(ticket, line, false, 1000, Dzd(total), Dzd(discount), reason, listPrice is { } list ? Dzd(list) : null);

    private static ReportRow Struck(string ticket, int line, long total, string? by = null, int? minute = null) =>
        new(ticket, line, true, 1000, Dzd(total), Dzd(0), null, null, by, minute is { } at ? At(at) : null);

    private static ReportCash Moved(string staff, CashRowKind kind, long amount, int minute) => new(staff, At(minute), kind, Dzd(amount));

    private static IReadOnlyList<CashierShare> Split(
        IReadOnlyList<ReportTicket> tickets, IReadOnlyList<ReportRow>? rows = null, IReadOnlyList<ReportPart>? parts = null,
        IReadOnlyList<ReportCash>? cash = null, IReadOnlyList<ReportMoment>? moments = null) =>
        ShiftReport.ByCashierOf(Currency.Dzd, tickets, rows ?? [], parts ?? [], cash ?? [], moments ?? []);

    private static CashierShare Of(IReadOnlyList<CashierShare> shares, string staff) => shares.Single(share => share.StaffId == staff);

    [Fact]
    public void A_session_nobody_touched_has_nobody_in_it()
    {
        Assert.Empty(Split([]));
    }

    [Fact]
    public void A_sale_is_whoever_rang_it_a_refund_whoever_refunded_and_a_cancel_whoever_cancelled()
    {
        // Nabil sold t1; Samia refunded it, sold t2 and cancelled t3.
        var tickets = new[] { Sale("t1", 14_300, Nabil, 1), Refund("r1", 14_300, Samia, 20), Sale("t2", 9_000, Samia, 30), Cancel("c1", 5_000, Samia, 40) };
        var parts = new[] { Part("t1", PaymentMethod.Cash, 14_300), Part("r1", PaymentMethod.Cash, -14_300), Part("t2", PaymentMethod.Card, 9_000) };

        var shares = Split(tickets, parts: parts);

        var nabil = Of(shares, Nabil);
        Assert.Equal((new Tally(1, Dzd(14_300)), new Tally(0, Dzd(0)), new Tally(0, Dzd(0))), (nabil.Activity.Sold, nabil.Activity.Refunded, nabil.Activity.Cancelled));
        Assert.Equal(Dzd(14_300), nabil.Takings.Net);

        var samia = Of(shares, Samia);
        Assert.Equal((new Tally(1, Dzd(9_000)), new Tally(1, Dzd(14_300)), new Tally(1, Dzd(5_000))), (samia.Activity.Sold, samia.Activity.Refunded, samia.Activity.Cancelled));
        Assert.Equal(Dzd(9_000 - 14_300), samia.Takings.Net);
    }

    [Fact]
    public void A_tenders_figure_is_sales_less_refunds_and_goes_below_zero_for_who_only_gave_back()
    {
        var tickets = new[] { Sale("t1", 20_000, Nabil, 1), Refund("r1", 5_000, Nabil, 2), Refund("r2", 3_000, Samia, 3) };
        var parts = new[]
        {
            Part("t1", PaymentMethod.Cash, 12_000), Part("t1", PaymentMethod.Card, 8_000),
            Part("r1", PaymentMethod.Cash, -5_000), Part("r2", PaymentMethod.Cash, -3_000),
        };

        var shares = Split(tickets, parts: parts);

        Assert.Equal(Dzd(7_000), ShiftReport.NetOf(Of(shares, Nabil).Takings, PaymentMethod.Cash));
        Assert.Equal(Dzd(8_000), ShiftReport.NetOf(Of(shares, Nabil).Takings, PaymentMethod.Card));
        Assert.Equal(Dzd(0), ShiftReport.NetOf(Of(shares, Nabil).Takings, PaymentMethod.OnAccount));
        Assert.Equal(Dzd(-3_000), ShiftReport.NetOf(Of(shares, Samia).Takings, PaymentMethod.Cash));
    }

    [Fact]
    public void A_struck_line_is_whoever_struck_it_even_on_somebody_elses_ticket()
    {
        // Samia struck line 2 of the ticket Nabil then rang; line 3 names nobody, so it is the ticket's cashier's.
        var tickets = new[] { Sale("t1", 14_300, Nabil, 10) };
        var rows = new[] { Row("t1", 1, 14_300), Struck("t1", 2, 9_000, by: Samia, minute: 5), Struck("t1", 3, 4_000) };

        var shares = Split(tickets, rows, [Part("t1", PaymentMethod.Cash, 14_300)]);

        Assert.Equal(new Tally(1, Dzd(9_000)), Of(shares, Samia).Activity.StruckLines);
        Assert.Equal(new Tally(1, Dzd(4_000)), Of(shares, Nabil).Activity.StruckLines);
        Assert.Equal(0, Of(shares, Samia).Activity.Sold.Count); // the strike gave her a column, not the ticket
    }

    [Fact]
    public void What_was_given_away_is_counted_to_who_rang_the_ticket()
    {
        // Nabil: a line discount and an override. Samia: a ticket discount over two lines.
        var tickets = new[] { Sale("t1", 20_000, Nabil, 1), Sale("t2", 18_000, Samia, 2, ticketDiscount: "gesture") };
        var rows = new[]
        {
            Row("t1", 1, 9_000, discount: 1_000, reason: "damaged"),
            Row("t1", 2, 11_000, listPrice: 12_500),
            Row("t2", 1, 9_000, discount: 1_000, reason: "gesture"),
            Row("t2", 2, 9_000, discount: 1_000, reason: "gesture"),
        };

        var shares = Split(tickets, rows);

        var nabil = Of(shares, Nabil).Reductions;
        Assert.Equal((new Tally(1, Dzd(1_000)), new Tally(0, Dzd(0)), new Tally(1, Dzd(1_500))), (nabil.LineDiscounts, nabil.TicketDiscounts, nabil.Overrides));
        var samia = Of(shares, Samia).Reductions;
        Assert.Equal((new Tally(0, Dzd(0)), new Tally(1, Dzd(2_000)), new Tally(0, Dzd(0))), (samia.LineDiscounts, samia.TicketDiscounts, samia.Overrides));
    }

    [Fact]
    public void Cash_in_and_out_and_a_tab_repaid_are_whoever_moved_them()
    {
        var cash = new[]
        {
            Moved(Karim, CashRowKind.PaidIn, 200_000, 60), Moved(Karim, CashRowKind.PaidOut, 85_000, 61),
            Moved(Nabil, CashRowKind.PaidOut, 50_000, 62), Moved(Nabil, CashRowKind.TabRepayment, 200_000, 63),
            Moved(Nabil, CashRowKind.TabRepayment, 120_000, 64),
        };

        var shares = Split([], cash: cash);

        Assert.Equal(new CashierCash(Dzd(200_000), Dzd(85_000), Dzd(0)), Of(shares, Karim).Cash);
        Assert.Equal(new CashierCash(Dzd(0), Dzd(50_000), Dzd(320_000)), Of(shares, Nabil).Cash);
    }

    [Fact]
    public void People_come_in_the_order_of_their_first_operation_with_their_first_and_their_last()
    {
        // Nabil opened the drawer and sold at the end; Samia sold twice in between; Karim only closed.
        var tickets = new[] { Sale("t3", 1_000, Nabil, 600), Sale("t1", 1_000, Samia, 28), Sale("t2", 1_000, Samia, 473) };
        var moments = new[] { new ReportMoment(Nabil, At(0)), new ReportMoment(Karim, At(834)) };

        var shares = Split(tickets, moments: moments);

        Assert.Equal([Nabil, Samia, Karim], shares.Select(share => share.StaffId));
        Assert.Equal((At(0), At(600)), (Of(shares, Nabil).First, Of(shares, Nabil).Last));
        Assert.Equal((At(28), At(473)), (Of(shares, Samia).First, Of(shares, Samia).Last));

        // Who only opened or closed has a column, with nothing in it but the hour.
        var karim = Of(shares, Karim);
        Assert.Equal((At(834), At(834)), (karim.First, karim.Last));
        Assert.Equal((0, Dzd(0)), (karim.Activity.Sold.Count, karim.Takings.Net));
    }

    [Fact]
    public void A_strike_and_a_cash_movement_count_for_the_hours()
    {
        var tickets = new[] { Sale("t1", 1_000, Nabil, 100) };
        var rows = new[] { Struck("t1", 2, 500, by: Samia, minute: 90) };
        var cash = new[] { Moved(Samia, CashRowKind.Drop, 10_000, 200) };

        var samia = Of(Split(tickets, rows, cash: cash), Samia);

        Assert.Equal((At(90), At(200)), (samia.First, samia.Last));
        Assert.Equal(new CashierCash(Dzd(0), Dzd(0), Dzd(0)), samia.Cash); // a drop is on no line of the report
    }

    [Fact]
    public void The_people_come_to_the_session_line_by_line_and_a_figure_moved_between_two_of_them_still_does()
    {
        var tickets = new[]
        {
            Sale("t1", 14_300, Nabil, 1), Sale("t2", 18_000, Samia, 2, ticketDiscount: "gesture"), Refund("r1", 14_300, Samia, 3),
            Cancel("c1", 5_000, Karim, 4), Sale("t3", 11_000, Karim, 5),
        };
        var rows = new[]
        {
            Row("t1", 1, 14_300), Struck("t1", 2, 9_000, by: Samia, minute: 0),
            Row("t2", 1, 9_000, discount: 1_000, reason: "gesture"), Row("t2", 2, 9_000, discount: 1_000, reason: "gesture"),
            Row("t3", 1, 11_000, listPrice: 12_500), Row("c1", 1, 5_000),
        };
        var parts = new[]
        {
            Part("t1", PaymentMethod.Cash, 14_300), Part("t2", PaymentMethod.Card, 10_000), Part("t2", PaymentMethod.Cash, 8_000),
            Part("r1", PaymentMethod.Cash, -14_300), Part("t3", PaymentMethod.OnAccount, 11_000),
        };
        var cash = new[] { Moved(Karim, CashRowKind.PaidIn, 200_000, 6), Moved(Nabil, CashRowKind.PaidOut, 50_000, 7), Moved(Samia, CashRowKind.TabRepayment, 30_000, 8) };

        var shares = Split(tickets, rows, parts, cash);
        var takings = ShiftReport.TakingsOf(Currency.Dzd, tickets, parts);
        var activity = ShiftReport.ActivityOf(Currency.Dzd, tickets, rows);
        var reductions = ShiftReport.ReductionsOf(Currency.Dzd, tickets, rows);
        var moved = new CashierCash(Dzd(200_000), Dzd(50_000), Dzd(30_000));

        Assert.True(ShiftReport.SharesReconcile(shares, takings, activity, reductions, moved));

        // A ticket lost from one column, a centime of cash, a count: each is seen.
        var short1 = shares.Select(share => share.StaffId == Nabil ? share with { Activity = share.Activity with { Sold = new Tally(0, Dzd(0)) } } : share).ToList();
        Assert.False(ShiftReport.SharesReconcile(short1, takings, activity, reductions, moved));
        Assert.False(ShiftReport.SharesReconcile(shares, takings, activity, reductions, moved with { PaidOut = Dzd(50_001) }));
        Assert.False(ShiftReport.SharesReconcile([.. shares.Where(share => share.StaffId != Samia)], takings, activity, reductions, moved));
        Assert.False(ShiftReport.SharesReconcile(shares, takings, activity, reductions with { Overrides = new Tally(2, Dzd(1_500)) }, moved));
    }

    [Fact]
    public void A_payment_or_a_line_of_a_ticket_that_is_not_the_sessions_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Split([Sale("t1", 1_000, Nabil, 1)], parts: [Part("elsewhere", PaymentMethod.Cash, 1_000)]));
        Assert.Throws<ArgumentException>(() => Split([Sale("t1", 1_000, Nabil, 1)], rows: [Struck("elsewhere", 1, 1_000)]));
    }
}
