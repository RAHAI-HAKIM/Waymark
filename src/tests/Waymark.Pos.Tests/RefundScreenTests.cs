using Waymark.Contracts.Pos;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// B9 (D-098): the refund's rules at the till, without a window. The silent failures: a refund offered
/// on a refund, or on a ticket with nothing left; a line sent as more than is left of it; a weighed
/// line sent in part, which the server then refuses at the counter.
/// </summary>
public sealed class RefundScreenTests
{
    private static PastTicketLineWire Milk(string sold = "3", string returned = "0") =>
        new("Lait UHT Candia", "Brique 1L", sold, "pc", "143.00", "429.00", "v-milk", returned);

    private static readonly PastTicketLineWire Tomatoes =
        new("Tomates", "Vrac", "1.24", "kg", "180.00", "223.20", "v-4011", "0", Weighed: true);

    private static PastTicketDetail Ticket(string status = "completed", string? original = null, IReadOnlyList<PastTicketLineWire>? lines = null) =>
        new("t-1", "S-2026-000001", DateTimeOffset.UnixEpoch, "till-1", "Nabil B.", status, lines ?? [Milk()],
            "0.00", "0.00", "0.00", "DZD", [], original);

    [Theory]
    [InlineData("completed", null, true)]
    [InlineData("partially_refunded", null, true)]
    [InlineData("refunded", null, false)]
    [InlineData("voided", null, false)]
    [InlineData("completed", "S-2026-000000", false)] // a refund is not refunded
    public void Rembourser_is_offered_on_a_paid_sale_only(string status, string? original, bool offered)
    {
        Assert.Equal(offered, RefundScreen.Refundable(Ticket(status, original)));
    }

    [Fact]
    public void A_ticket_with_everything_already_back_offers_nothing()
    {
        Assert.False(RefundScreen.Refundable(Ticket("partially_refunded", null, [Milk(returned: "3")])));
    }

    [Fact]
    public void A_touch_takes_one_unit_and_plus_never_goes_above_what_is_left()
    {
        var ticket = Ticket(lines: [Milk(sold: "3", returned: "1")]);
        var state = RefundScreen.Touch(RefundState.For(ticket), ticket, 0);
        Assert.Equal((0, 1_000L), (state.Selected!.Value, state.Picks[0].Quantity));

        state = RefundScreen.Press(RefundScreen.Press(state, ticket, RefundScreen.MoreKey), ticket, RefundScreen.MoreKey);
        Assert.Equal(2_000, state.Picks[0].Quantity); // 3 sold, 1 back already: 2 at most

        state = RefundScreen.Press(RefundScreen.Press(RefundScreen.Press(state, ticket, RefundScreen.LessKey), ticket, RefundScreen.LessKey), ticket, RefundScreen.LessKey);
        Assert.Equal(0, state.Picks[0].Quantity); // never below nothing
    }

    [Fact]
    public void A_weighed_line_comes_back_whole()
    {
        var ticket = Ticket(lines: [Tomatoes]);
        var state = RefundScreen.Touch(RefundState.For(ticket), ticket, 0);

        Assert.Equal(1_240, state.Picks[0].Quantity);
        Assert.Equal(1_240, RefundScreen.Press(state, ticket, RefundScreen.MoreKey).Picks[0].Quantity);
        Assert.Equal(0, RefundScreen.Press(state, ticket, RefundScreen.LessKey).Picks[0].Quantity);
    }

    [Fact]
    public void A_line_with_nothing_left_is_not_taken()
    {
        var ticket = Ticket(lines: [Milk(sold: "2", returned: "2")]);

        Assert.Null(RefundScreen.Touch(RefundState.For(ticket), ticket, 0).Selected);
    }

    [Fact]
    public void The_request_names_the_lines_touched_by_variant_and_price_and_nothing_else()
    {
        var ticket = Ticket(lines: [Milk(), Tomatoes]);
        var state = RefundScreen.Press(RefundScreen.Touch(RefundState.For(ticket), ticket, 1), ticket, RefundScreen.RestockKey);

        var request = RefundScreen.Request("till-1", ticket, state with { To = RefundDestinations.StoreCredit }, "retour_defectueux", null, "auth-1", quote: false);

        Assert.Equal(new RefundLineRequest("v-4011", "180.00", "1.24", false), Assert.Single(request.Lines));
        Assert.Equal(("t-1", RefundDestinations.StoreCredit, "auth-1", false), (request.Original, request.RefundTo, request.Authorisation, request.Quote));
    }

    // ------------------------------------------------------------------ the floating panel's figures

    private static string Plain(string shown) => shown.Replace(' ', ' ').Replace(' ', ' ');

    /// <summary>The refund's panel once the server has quoted it.</summary>
    private static PaymentPanel Quoted(RefundAnswer quote)
    {
        var ticket = Ticket();
        var state = new ScreenState(
            TillText.For(TillLanguage.French), TimeZoneInfo.Utc, DateTimeOffset.UnixEpoch, new Waymark.Pos.Checkout.Cart(), null, null, null, null,
            Waymark.Pos.Checkout.ServerState.Reachable, null, null, null, 0,
            Viewing: ticket, Refunding: RefundState.For(ticket) with { Quote = quote });
        return Assert.IsType<PaymentPanel>(RefundScreen.Panel(state));
    }

    [Theory]
    [InlineData("143.00", "145.00", "2,00")]    // the step adds two dinars to what is handed back
    [InlineData("142.00", "140.00", "−2,00")]   // and here takes two off
    public void The_cash_rounding_adds_up_with_what_is_given_back_and_what_is_handed_over(string rest, string cashOut, string rounding)
    {
        // Block B review: "À rendre 143,00 · Arrondi espèces −2,00 · ESPÈCES À RENDRE 145,00" did not add up.
        var panel = Quoted(new RefundAnswer(
            RefundOutcomes.Quoted, Total: rest, ToTab: "0.00", Rest: rest, RefundTo: RefundDestinations.Cash, CashOut: cashOut, Currency: "DZD"));

        Assert.Equal([$"À rendre {rest.Replace('.', ',')}", $"Arrondi espèces {rounding}"], panel.Figures.Select(figure => Plain($"{figure.Label} {figure.Value}")));
        Assert.Equal($"{cashOut.Replace('.', ',')} DA", Plain(panel.Due));
    }

    [Fact]
    public void A_refund_on_the_cash_step_shows_no_rounding()
    {
        var panel = Quoted(new RefundAnswer(
            RefundOutcomes.Quoted, Total: "150.00", ToTab: "0.00", Rest: "150.00", RefundTo: RefundDestinations.Cash, CashOut: "150.00", Currency: "DZD"));

        Assert.Equal(["À rendre 150,00"], panel.Figures.Select(figure => Plain($"{figure.Label} {figure.Value}")));
    }
}
