using Waymark.Contracts.Pos;
using Waymark.Contracts.Reference;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// The customer and cash panels' figures and words, without a window (B7, B9, B10; block B review).
/// The silent failures: a repayment shown as a negative amount; store credit issued shown short of
/// what the balance took; cash out asked for a reason "for cash in"; "1 lignes".
/// </summary>
public sealed class CustomerPanelTests
{
    private static string Plain(string shown) => shown.Replace(' ', ' ').Replace(' ', ' ');

    private static readonly CustomerSummaryWire Samira = new("c-samira", "Samira Benali", "+213550123456");

    private static TabAnswer Tab(string balance) =>
        new(CustomerOutcomes.Ok, Samira, balance, "2000.00", "0.00", false, null, null, "DZD", []);

    private static FormPanel Panel(CustomerPanelState panel, Cart? cart = null) => Assert.IsType<FormPanel>(CustomerScreen.Panel(new ScreenState(
        TillText.For(TillLanguage.French), TimeZoneInfo.Utc, DateTimeOffset.UnixEpoch, cart ?? new Cart(), null, null, null, null,
        ServerState.Reachable, new TillContext(TillContextOutcome.Found, "El Bahdja", "Caisse 1", "DZD", "Nabil B.", "Caissier", "أمين الصندوق"),
        null, null, 0, Customer: new CustomerScreenState(true, true, Panel: panel))));

    [Fact]
    public void A_repayment_recorded_shows_what_came_in_as_an_amount_not_a_negative_one()
    {
        var panel = Panel(new CustomerPanelState(CustomerPanelKind.Repaid, Before: Tab("286.00"), After: Tab("86.00"), At: DateTimeOffset.UnixEpoch));

        Assert.Equal("86,00 DA", Plain(panel.Big!));
        Assert.Equal(["Solde avant 286,00", "Réglé en espèces 200,00"], panel.Figures.Select(figure => Plain($"{figure.Label} {figure.Value}")));
    }

    [Fact]
    public void A_whole_due_repaid_shows_what_the_cash_step_took_off_and_what_the_drawer_took()
    {
        // D-108: 286,00 owed, cleared by 285,00 in cash. The three figures add up as they read.
        var after = Tab("0.00") with { CashCollected = "285.00" };
        var panel = Panel(new CustomerPanelState(CustomerPanelKind.Repaid, Before: Tab("286.00"), After: after, At: DateTimeOffset.UnixEpoch));

        Assert.Equal("0,00 DA", Plain(panel.Big!));
        Assert.Equal(
            ["Solde avant 286,00", "Arrondi espèces −1,00", "Réglé en espèces 285,00"],
            panel.Figures.Select(figure => Plain($"{figure.Label} {figure.Value}")));
    }

    private static FormPanel Repaying(string balance, string typed) => Assert.IsType<FormPanel>(CustomerScreen.Panel(new ScreenState(
        TillText.For(TillLanguage.French), TimeZoneInfo.Utc, DateTimeOffset.UnixEpoch, new Cart(), null, null, null, null,
        ServerState.Reachable, new TillContext(TillContextOutcome.Found, "El Bahdja", "Caisse 1", "DZD", "Nabil B.", "Caissier", "أمين الصندوق"),
        null, null, 0,
        Customer: new CustomerScreenState(
            true, true, Carnet: new CarnetState(Tab(balance), false, DateTimeOffset.UnixEpoch, null),
            Panel: new CustomerPanelState(CustomerPanelKind.Repay, Typed: typed, ReasonCode: "tab_repayment", Reasons: CashReasons)))));

    private static readonly ReasonCodeList CashReasons = new(
        "cash_movement", [new ReasonCodeOption("tab_repayment", "تسديد الدفتر", "Règlement carnet", false, false, "in")]);

    [Fact]
    public void Paying_the_whole_due_says_what_the_cash_step_takes_off_and_what_the_drawer_takes_before_encaisser()
    {
        // D-108: 286,00 owed is cleared by 285,00. Said before the key is pressed, not found at the drawer.
        var panel = Repaying("286.00", "286");

        Assert.True(panel.MayPrimary);
        Assert.Null(panel.Message);
        Assert.Equal(
            ["SOLDE DÛ 286,00 DA", "Arrondi espèces −1,00", "Espèces à encaisser 285,00 DA"],
            panel.Tiles.Select(figure => Plain($"{figure.Label} {figure.Value}")));
    }

    [Fact]
    public void A_part_no_coin_pays_is_refused_in_the_panel_before_the_server_is_asked()
    {
        var panel = Repaying("1714.00", "1001");

        Assert.False(panel.MayPrimary);
        Assert.Equal("PAS DE MONNAIE POUR CE MONTANT", panel.Message?.Title);
        Assert.Contains("5,00 DA", Plain(panel.Message!.Body));
    }

    [Fact]
    public void A_part_on_the_cash_step_is_taken_as_typed_with_nothing_more_to_say()
    {
        var panel = Repaying("1714.00", "1000");

        Assert.True(panel.MayPrimary);
        Assert.Equal(["SOLDE DÛ 1 714,00 DA"], panel.Tiles.Select(figure => Plain($"{figure.Label} {figure.Value}")));
    }

    [Fact]
    public void A_panels_first_choice_is_drawn_first()
    {
        // Cash in or out is decided before the amount, who clocks before their PIN: both were drawn last.
        var petty = Panel(new CustomerPanelState(CustomerPanelKind.PettyCash));
        var clock = Panel(new CustomerPanelState(CustomerPanelKind.Clock));

        Assert.True(petty.KeysFirst);
        Assert.False(petty.RowsFirst);
        Assert.True(clock.RowsFirst);
        Assert.False(Repaying("286.00", "286").KeysFirst);
    }

    [Fact]
    public void A_field_of_money_gets_the_money_pad_and_a_pin_the_phone_pad()
    {
        // One till, two layouts for a PIN (block B review): sign-in read 1 2 3, the clock 7 8 9.
        Assert.False(Repaying("286.00", "286").PhonePad);
        Assert.True(Panel(new CustomerPanelState(CustomerPanelKind.Clock, Chosen: "nabil")).PhonePad);
    }

    [Fact]
    public void Store_credit_issued_counts_the_share_that_came_back_as_credit_first()
    {
        // B9b (D-101): the sale had spent 250,00 of credit; its refund gives that back as credit, and
        // the 50,00 left too. The balance took 300,00: the panel said 50,00, and a "before" 250,00 too high.
        var issued = new RefundAnswer(
            RefundOutcomes.Refunded, "r-1", "S-2026-000009", "300.00", "0.00", "50.00", RefundDestinations.StoreCredit, null, "320.00", true, "DZD",
            ToCredit: "250.00");

        var panel = Panel(new CustomerPanelState(CustomerPanelKind.CreditIssued, Issued: issued, IssuedTo: "Samira B.", At: DateTimeOffset.UnixEpoch));

        Assert.Equal("320,00 DA", Plain(panel.Big!));
        Assert.Equal(["Avoir avant 20,00", "Émis pour le retour n° S-2026-000009 +300,00"], panel.Figures.Select(figure => Plain($"{figure.Label} {figure.Value}")));
    }

    [Theory]
    [InlineData(CashDirections.Out, "MOTIF DE SORTIE DE CAISSE")]
    [InlineData(CashDirections.In, "MOTIF D'ENTRÉE DE CAISSE")]
    public void Petite_caisse_titles_its_reasons_by_the_way_the_cash_goes(string direction, string title)
    {
        var reasons = new ReasonCodeList("cash_movement",
        [
            new ReasonCodeOption("depot", "إيداع", "Dépôt en banque", false, false, CashDirections.Out),
            new ReasonCodeOption("apport", "تزويد", "Apport de fond de caisse", false, false, CashDirections.In),
        ]);

        var panel = Panel(new CustomerPanelState(CustomerPanelKind.PettyCash, Reasons: reasons, Direction: direction));

        Assert.Equal(title, panel.RowsTitle);
        Assert.Equal(direction == CashDirections.Out ? "Dépôt en banque" : "Apport de fond de caisse", Assert.Single(panel.Rows).Label);
    }

    [Theory]
    [InlineData(1, "Rattacher au ticket en cours · 1 ligne")]
    [InlineData(3, "Rattacher au ticket en cours · 3 lignes")]
    public void The_customer_search_counts_the_tickets_lines_in_the_singular_too(int lines, string subtitle)
    {
        var cart = new Cart();
        for (var i = 0; i < lines; i++)
        {
            cart.Add(new ProductForSale($"v-{i}", $"p-{i}", "Café Bonal", "250 g", "pc", 0, 900, TvaRateSource.FromCategory, "65.00", "DZD", false, "40"), $"61300000000{i}");
        }

        Assert.Equal(subtitle, Plain(Panel(new CustomerPanelState(CustomerPanelKind.Search), cart).Subtitle!));
    }
}
