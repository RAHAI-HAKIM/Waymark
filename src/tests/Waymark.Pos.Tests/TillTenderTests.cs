using Waymark.Contracts.Pos;
using Waymark.Contracts.Reference;
using Waymark.Domain.Enums;
using Waymark.Domain.Values;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;
using Waymark.Pos.Server;

namespace Waymark.Pos.Tests;

/// <summary>
/// The payment panel (B6, D-095), without a window. The silent failures: a card part for more than is
/// left, which would need cash back; a card number kept as a reference; a part shown but not sent; the
/// cash to collect worked out on the whole ticket instead of what the parts leave; a scan sold under a
/// frozen ticket.
/// </summary>
/// <remarks>
/// The tests that settle parts ask <see cref="Domain.Sales.Tender"/>, Hakim's rule: they are red until
/// it is written. With no part the ticket is all cash and the rule is not asked.
/// </remarks>
public sealed class TillTenderTests
{
    private static readonly TimeZoneInfo Algiers = TimeZoneInfo.CreateCustomTimeZone("Africa/Algiers", TimeSpan.FromHours(1), "Algiers", "Algiers");

    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    /// <summary>The screen's thin spaces between thousands and before the unit, as plain spaces, so an expectation reads as the screen does.</summary>
    private static string Plain(string shown) => shown.Replace(' ', ' ').Replace(' ', ' ');

    private static ProductForSale Product(string price) =>
        new("v-1", "p-1", "Café Bonal", "250 g", "pc", 0, 900, TvaRateSource.FromCategory, price, "DZD", false, "40");

    /// <summary>The board's ticket: 3 320,80.</summary>
    private static Cart Ticket()
    {
        var cart = new Cart();
        cart.Add(Product("3320.80"), "6131");
        return cart;
    }

    private static ScreenState State(Cart cart, PaymentState? paying = null, PaidTicket? paid = null, DiscountState? discounting = null) => new(
        TillText.For(TillLanguage.French), Algiers, new DateTimeOffset(2026, 9, 30, 13, 32, 0, TimeSpan.Zero), cart, null, paid, null, null,
        ServerState.Reachable, new TillContext(TillContextOutcome.Found, "El Bahdja", "Caisse 1", "DZD", "Nabil B.", "Caissier", "أمين الصندوق"),
        null, null, 0, Discounting: discounting, Paying: paying);

    private static PaymentPanel Panel(PaymentState paying) => TillScreen.Build(State(Ticket(), paying)).Payment!;

    private static PaymentState Typing(PaymentState open, string keys) => keys.Aggregate(open, PaymentScreen.Press);

    private static PaymentState Card => PaymentScreen.Choose(PaymentState.Open, PaymentMethod.Card);

    // ------------------------------------------------------------------ opened: all cash

    [Fact]
    public void Encaisser_opens_on_cash_with_no_part_and_valider_sends_it_all()
    {
        var panel = Panel(PaymentState.Open);

        Assert.Equal(["Total du ticket 3 320,80", "Arrondi espèces −0,80"], panel.Figures.Select(figure => Plain($"{figure.Label} {figure.Value}")));
        Assert.Equal("3 320,00 DA", Plain(panel.Due));
        Assert.Equal("Aucune part : tout le ticket en espèces.", panel.NoParts);
        Assert.Equal(PaymentMethod.Cash, Assert.Single(panel.Methods, method => method.Selected).Method);
        Assert.Null(panel.Entry);
        Assert.False(panel.PadAvailable); // nothing is typed for cash: no amount received, no change (D-095)
        Assert.Equal(("Valider", true), (panel.Primary, panel.MayPrimary));
    }

    [Fact]
    public void With_cash_chosen_a_key_typed_goes_nowhere()
    {
        Assert.Equal(PaymentState.Open, Typing(PaymentState.Open, "1000"));
    }

    [Fact]
    public void The_notice_behind_the_panel_says_scans_are_ignored_and_behind_the_pin_step_too()
    {
        var paying = TillScreen.Build(State(Ticket(), PaymentState.Open)).Notice;
        var approving = TillScreen.Build(State(Ticket(), discounting: new DiscountState(null, "percent", "10", null, false, "x", Authorising: true))).Notice;

        Assert.Equal(("ENCAISSEMENT EN COURS", "Les scans sont ignorés tant que le paiement est ouvert."), (paying.Label, paying.Text));
        Assert.Equal("AUTORISATION EN COURS", approving.Label);
    }

    // ------------------------------------------------------------------ a card or BaridiMob part

    [Fact]
    public void A_card_part_starts_at_the_rest_and_the_first_key_replaces_it()
    {
        var prefilled = Panel(Card).Entry!;
        var typed = Panel(Typing(Card, "2000")).Entry!;

        Assert.Equal(("3 320,80", true, "0,00 DA"), (Plain(prefilled.Amount), prefilled.Prefilled, Plain(prefilled.Rest)));
        Assert.Equal(("2000", false, "1 320,80 DA"), (typed.Amount, typed.Prefilled, Plain(typed.Rest)));
        Assert.Equal("Ajouter la part", Panel(Card).Primary);
        Assert.True(Panel(Card).PadAvailable);
    }

    [Fact]
    public void A_part_above_the_rest_is_refused_as_it_is_typed()
    {
        var panel = Panel(Typing(Card, "3500"));

        Assert.False(panel.MayPrimary);
        Assert.Equal("—", panel.Entry!.Rest);
        Assert.Equal("MONTANT TROP ÉLEVÉ", panel.Message!.Title);
        Assert.Equal(PaymentProblem.AboveRest, PaymentScreen.AddPart(Typing(Card, "3500"), Dzd(332_080)).Problem);
    }

    [Fact]
    public void Adding_a_part_lists_it_and_chooses_cash_again()
    {
        var added = PaymentScreen.AddPart(Typing(Card, "2000") with { Reference = " 4417 " }, Dzd(332_080));

        Assert.Equal([new TenderEntry(PaymentMethod.Card, Dzd(200_000), "4417")], added.Parts);
        Assert.Equal((PaymentMethod.Cash, string.Empty, string.Empty), (added.Method, added.Typed, added.Reference));
    }

    [Fact]
    public void The_rest_prefilled_is_a_part_too()
    {
        var added = PaymentScreen.AddPart(PaymentScreen.Choose(PaymentState.Open, PaymentMethod.MobileWallet), Dzd(332_080));

        Assert.Equal([new TenderEntry(PaymentMethod.MobileWallet, Dzd(332_080), null)], added.Parts);
    }

    [Fact]
    public void A_reference_that_reads_as_a_card_number_is_cleared_and_the_part_is_not_added()
    {
        var refused = PaymentScreen.AddPart(Card with { Reference = "4970 1012 3456 7893" }, Dzd(332_080));

        Assert.Empty(refused.Parts);
        Assert.Equal((string.Empty, PaymentProblem.ReferenceLooksLikeCard), (refused.Reference, refused.Problem));
        Assert.Equal("RÉFÉRENCE REFUSÉE", Panel(refused).Message!.Title);
    }

    [Fact]
    public void With_the_reference_touched_the_keys_go_to_it()
    {
        var typed = Typing(Card with { OnReference = true }, "4417");

        Assert.Equal(("4417", string.Empty), (typed.Reference, typed.Typed));
        Assert.Equal("441", PaymentScreen.Backspace(typed).Reference);
    }

    [Theory]
    [InlineData(",5", "0,5")]
    [InlineData("12,5,0", "12,50")]   // a second comma is not taken
    [InlineData("12.5", "12,5")]      // a point is a comma
    [InlineData("12a", "12")]         // a letter is not an amount
    public void An_amount_takes_digits_and_one_comma(string keys, string typed)
    {
        Assert.Equal(typed, Typing(Card, keys).Typed);
    }

    [Fact]
    public void Tout_le_reste_puts_the_rest_back_and_a_part_can_be_taken_out()
    {
        Assert.Equal(string.Empty, PaymentScreen.WholeRest(Typing(Card, "12")).Typed);

        var two = PaymentState.Open with
        {
            Parts = [new TenderEntry(PaymentMethod.Card, Dzd(200_000), "4417"), new TenderEntry(PaymentMethod.MobileWallet, Dzd(50_000), null)],
        };
        Assert.Equal([new TenderEntry(PaymentMethod.MobileWallet, Dzd(50_000), null)], PaymentScreen.RemovePart(two, 0).Parts);
    }

    [Fact]
    public void A_part_is_sent_as_given_never_rounded()
    {
        Assert.Equal(new TenderRequest("card", "2000.50", "4417"), new TenderEntry(PaymentMethod.Card, Dzd(200_050), "4417").ToWire());
        Assert.Equal("mobile_wallet", new TenderEntry(PaymentMethod.MobileWallet, Dzd(100), null).ToWire().Method);
    }

    // ------------------------------------------------------------------ settled by Hakim's rule

    [Fact]
    public void Two_parts_leave_the_cash_rest_to_collect_rounded_on_its_own()
    {
        // The board: 3 320,80, 2 000,00 by card and 500,00 by BaridiMob; 820,80 is left, 820,00 collected.
        var paying = PaymentState.Open with
        {
            Parts = [new TenderEntry(PaymentMethod.Card, Dzd(200_000), "4417"), new TenderEntry(PaymentMethod.MobileWallet, Dzd(50_000), "88213")],
        };

        var panel = Panel(paying);

        Assert.Equal("820,00 DA", Plain(panel.Due));
        Assert.Equal("−0,80", panel.Figures[1].Value);
        Assert.Equal([("Carte", "réf 4417", "2 000,00"), ("BaridiMob", "réf 88213", "500,00")], panel.Parts.Select(part => (part.Label, part.Reference!, Plain(part.Amount))));
        Assert.Null(panel.NoParts);
    }

    // ------------------------------------------------------------------ after the sale

    [Fact]
    public void The_paid_rail_lists_each_part_then_the_cash_and_its_rounding()
    {
        var outcome = new SaleOutcome(SaleOutcomes.Completed, "t1", "0142", "3320.80", "530.21", "820.00", "DZD", null,
            [new PaymentLine("card", "2000.00", "4417"), new PaymentLine("mobile_wallet", "500.00", "88213"), new PaymentLine("cash", "820.80", null)]);

        var paid = Assert.IsType<Rail.Paid>(TillScreen.Build(State(new Cart(), paid: new PaidTicket(outcome, [], DateTimeOffset.UnixEpoch))).Rail);

        Assert.Equal(
            ["Total du ticket 3 320,80", "Carte · réf 4417 2 000,00", "BaridiMob · réf 88213 500,00", "Arrondi espèces −0,80", "ESPÈCES DUES 820,00"],
            paid.Figures.Select(figure => Plain($"{figure.Label} {figure.Value}")));
    }

    [Fact]
    public void A_ticket_paid_all_by_card_says_nothing_of_cash()
    {
        var outcome = new SaleOutcome(SaleOutcomes.Completed, "t1", "0142", "3320.80", "530.21", "0.00", "DZD", null,
            [new PaymentLine("card", "3320.80", null)]);

        var paid = Assert.IsType<Rail.Paid>(TillScreen.Build(State(new Cart(), paid: new PaidTicket(outcome, [], DateTimeOffset.UnixEpoch))).Rail);

        Assert.Equal(["Total du ticket 3 320,80", "Carte 3 320,80"], paid.Figures.Select(figure => Plain($"{figure.Label} {figure.Value}")));
    }

    [Fact]
    public void After_parts_the_bar_rounds_the_cash_rest_never_the_whole_ticket()
    {
        // Block B review: 1 001,00 with 500 by card and 300 by BaridiMob leaves 201,00, collected as
        // 200,00. The bar said "Arrondi espèces −801,00": the cash due less the whole ticket.
        var outcome = new SaleOutcome(SaleOutcomes.Completed, "t1", "0142", "1001.00", "159.82", "200.00", "DZD", null,
            [new PaymentLine("card", "500.00", null), new PaymentLine("mobile_wallet", "300.00", null), new PaymentLine("cash", "201.00", null)]);

        var bar = TillScreen.Build(State(new Cart(), paid: new PaidTicket(outcome, [], DateTimeOffset.UnixEpoch))).Bottom;

        Assert.Equal(["Total du ticket 1 001,00", "Arrondi espèces −1,00"], bar.Summary.Select(figure => Plain($"{figure.Label} {figure.Value}")));
        Assert.Equal("200,00 DA", Plain(bar.BigFigure));
    }

    [Fact]
    public void A_ticket_with_no_cash_row_rounded_nothing()
    {
        var outcome = new SaleOutcome(SaleOutcomes.Completed, "t1", "0142", "286.00", "45.66", "0.00", "DZD", null,
            [new PaymentLine("on_account", "286.00", null)]);

        var bar = TillScreen.Build(State(new Cart(), paid: new PaidTicket(outcome, [], DateTimeOffset.UnixEpoch))).Bottom;

        Assert.Equal("Arrondi espèces 0,00", Plain($"{bar.Summary[1].Label} {bar.Summary[1].Value}"));
        Assert.Equal("0,00 DA", Plain(bar.BigFigure));
    }

    [Fact]
    public void A_tab_part_and_a_store_credit_part_are_named_as_what_they_are()
    {
        // Block B review: both read "Carte" on the paid card, since anything not BaridiMob was a card.
        var outcome = new SaleOutcome(SaleOutcomes.Completed, "t1", "0142", "429.00", "68.50", "80.00", "DZD", null,
            [new PaymentLine("on_account", "200.00", null), new PaymentLine("store_credit", "150.00", null), new PaymentLine("cash", "79.00", null)]);

        var paid = Assert.IsType<Rail.Paid>(TillScreen.Build(State(new Cart(), paid: new PaidTicket(outcome, [], DateTimeOffset.UnixEpoch))).Rail);

        Assert.Equal(
            ["Total du ticket 429,00", "Carnet 200,00", "Avoir 150,00", "Arrondi espèces 1,00", "ESPÈCES DUES 80,00"],
            paid.Figures.Select(figure => Plain($"{figure.Label} {figure.Value}")));
    }

    // ------------------------------------------------------------------ the session

    private sealed class Products : IProductSource
    {
        public Task<LookupAnswer> LookupAsync(string barcode, CancellationToken cancellationToken = default) =>
            Task.FromResult<LookupAnswer>(new LookupAnswer.Answered(new ProductLookup(ProductLookupOutcome.Found, barcode, Product("3320.80"), null)));
    }

    private sealed class Sales : IStoreSales
    {
        public List<SaleRequest> Sent { get; } = [];

        public Task<SaleAnswer> CompleteSaleAsync(SaleRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult<SaleAnswer>(new SaleAnswer.Completed(
                new SaleOutcome(SaleOutcomes.Completed, "t1", "S-2026-000001", "3320.80", "530.21", "820.00", "DZD", null)));
        }
    }

    [Fact]
    public async Task A_sale_sends_its_parts_and_an_all_cash_sale_sends_none()
    {
        var sales = new Sales();
        var session = new TillSession(new Products(), sales, new TillIdentity("till-1"));
        session.SignIn(new SignedInStaff("nabil", "token"));

        await session.SubmitAsync("111");
        await session.PayAsync([new TenderRequest("card", "2000.00", "4417")]);
        await session.SubmitAsync("111");
        await session.PayAsync();

        Assert.Equal([new TenderRequest("card", "2000.00", "4417")], sales.Sent[0].Tenders);
        Assert.Null(sales.Sent[1].Tenders);
    }
}
