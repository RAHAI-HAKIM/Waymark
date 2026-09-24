using Waymark.Contracts.Pos;
using Waymark.Contracts.Recommendations;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// What the till shows, decided without a window (session A4, G1). The failures here are the
/// ones a screen hides well: a ticket number shown before the sale exists, an Encaisser button
/// asking for centimes nobody can hand over, a struck line still in the total, a notice with a
/// colour and no word, a card a cashier was never meant to see.
/// </summary>
public sealed class TillScreenTests
{
    private static readonly TimeZoneInfo Algiers = TimeZoneInfo.CreateCustomTimeZone("Africa/Algiers", TimeSpan.FromHours(1), "Algiers", "Algiers");

    /// <summary>14:32 in Algiers.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 13, 32, 0, TimeSpan.Zero);

    private static readonly TillContext Context = new(
        TillContextOutcome.Found, "El Bahdja", "Caisse 1", "DZD", "Nabil B.", "Caissier", "أمين صندوق");

    private static ProductForSale Product(string id, string price = "65.00", string stock = "10", bool promotional = false) =>
        new(id, "p-" + id, "Danone", "Activia fraise", "pc", 0, 1_900, TvaRateSource.FromCategory, price, "DZD", promotional, stock);

    private static ScreenState State(
        Cart? cart = null,
        TillNotice? notice = null,
        PaidTicket? paid = null,
        SaleOutcome? lastSale = null,
        DateTimeOffset? lastSaleAt = null,
        ServerState? server = null,
        TillContext? context = null,
        string? selected = null,
        BoardAnswer? board = null,
        int cardIndex = 0,
        TillLanguage language = TillLanguage.French) =>
        new(
            TillText.For(language),
            Algiers,
            Now,
            cart ?? new Cart(),
            notice,
            paid,
            lastSale,
            lastSaleAt,
            server ?? ServerState.Reachable,
            context ?? Context,
            selected,
            board,
            cardIndex);

    private static Cart CartWith(params (string Id, string Price, int Count)[] lines)
    {
        var cart = new Cart();
        foreach (var (id, price, count) in lines)
        {
            for (var i = 0; i < count; i++)
            {
                cart.Add(Product(id, price), "613" + id);
            }
        }

        return cart;
    }

    private static readonly SaleOutcome Sale =
        new(SaleOutcomes.Completed, "t1", "0142", "3320.80", "530.20", "3320.00", "DZD", null);

    // ================================================================ the ticket tab

    [Fact]
    public void An_empty_till_offers_a_new_ticket()
    {
        var tab = TillScreen.Build(State()).Top.Tab;

        Assert.Equal(("Nouveau ticket", "vide"), (tab.Title, tab.Detail));
    }

    [Fact]
    public void A_ticket_being_rung_up_has_no_number_and_counts_only_the_lines_still_in_it()
    {
        // D-070: the invoice number is given when the sale is recorded, gapless. A number shown
        // now would be a promise the server may not keep. And a struck line is not a line.
        var cart = CartWith(("a", "65.00", 1), ("b", "120.00", 1), ("c", "10.00", 1));
        cart.Remove("c", Now);

        var tab = TillScreen.Build(State(cart)).Top.Tab;

        Assert.Equal(("Ticket en cours", "2 lignes"), (tab.Title, tab.Detail));
    }

    [Fact]
    public void Only_a_recorded_sale_shows_its_number()
    {
        var paid = new PaidTicket(Sale, [], Now);

        var tab = TillScreen.Build(State(paid: paid)).Top.Tab;

        Assert.Equal(("Ticket n° 0142", "payé"), (tab.Title, tab.Detail));
    }

    // ============================================================= the top bar

    [Fact]
    public void The_top_bar_names_the_store_the_till_and_the_person()
    {
        var top = TillScreen.Build(State()).Top;

        Assert.Equal("El Bahdja · Caisse 1", top.Place);
        Assert.Equal(new StaffChip("Nabil B.", "CAISSIER"), top.Staff);
        Assert.Equal("14:32", top.Clock);
    }

    [Fact]
    public void An_unknown_terminal_names_no_store_rather_than_guessing()
    {
        var unknown = new TillContext(TillContextOutcome.UnknownTerminal, null, null, null, null, null, null);

        var top = TillScreen.Build(State(context: unknown)).Top;

        Assert.Null(top.Place);
        Assert.Null(top.Staff);
    }

    [Fact]
    public void Online_is_a_label_with_no_colour_and_offline_is_critical()
    {
        // There is no positive state (CLAUDE.md §6): "EN LIGNE" is the absence of a problem.
        Assert.Equal(new Connection("EN LIGNE", Tone.Neutral), TillScreen.Build(State()).Top.Connection);
        Assert.Equal(
            new Connection("HORS LIGNE", Tone.Critical),
            TillScreen.Build(State(server: new ServerState(Now))).Top.Connection);
    }

    // ============================================================= the notice slot

    [Fact]
    public void Offline_says_since_when_in_the_tills_own_time()
    {
        var since = Now.AddMinutes(-1);

        var notice = TillScreen.Build(State(server: new ServerState(since))).Notice;

        Assert.Equal(Tone.Critical, notice.Tone);
        Assert.Equal("HORS LIGNE", notice.Label);
        Assert.Equal("Serveur du magasin injoignable depuis 14:31", notice.Text);
    }

    [Fact]
    public void An_unknown_code_is_a_warning_and_a_product_that_cannot_be_sold_is_critical()
    {
        var unknown = TillScreen.Build(State(notice: new TillNotice(TillNoticeKind.UnknownCode, "6130985042117", ""))).Notice;
        var refused = TillScreen.Build(State(notice: new TillNotice(TillNoticeKind.NotSellable, "222", NotSellableReason.NoCurrentPrice))).Notice;

        Assert.Equal((Tone.Warning, "CODE INCONNU"), (unknown.Tone, unknown.Label));
        Assert.Contains("6130985042117", unknown.Text, StringComparison.Ordinal);
        Assert.Equal((Tone.Critical, "NON VENDABLE"), (refused.Tone, refused.Label));
        Assert.Equal("222 — aucun prix en vigueur aujourd'hui", refused.Text);
    }

    public static TheoryData<TillNoticeKind, TillLanguage> NoticeKinds()
    {
        var data = new TheoryData<TillNoticeKind, TillLanguage>();
        foreach (var kind in Enum.GetValues<TillNoticeKind>())
        {
            data.Add(kind, TillLanguage.French);
            data.Add(kind, TillLanguage.Arabic);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NoticeKinds))]
    public void Every_notice_has_a_label_before_its_colour(TillNoticeKind kind, TillLanguage language)
    {
        // CLAUDE.md §6: a coloured edge with no label is not a valid state, in either language.
        var screen = TillScreen.Build(State(notice: new TillNotice(kind, "111", "why"), language: language));

        Assert.False(string.IsNullOrWhiteSpace(screen.Notice.Label));
        if (screen.Rail is Rail.Unconfirmed unconfirmed)
        {
            Assert.False(string.IsNullOrWhiteSpace(unconfirmed.Label));
        }
    }

    [Fact]
    public void After_a_scan_the_slot_names_the_last_article()
    {
        var notice = TillScreen.Build(State(CartWith(("a", "180.00", 1)))).Notice;

        Assert.Equal((Tone.Neutral, "DERNIER ARTICLE"), (notice.Tone, notice.Label));
        Assert.Equal("Danone Activia fraise · 180,00", notice.Text);
    }

    [Fact]
    public void A_ready_till_recalls_the_last_sale_by_number_time_and_total()
    {
        var notice = TillScreen.Build(State(lastSale: Sale, lastSaleAt: Now.AddMinutes(-6))).Notice;

        Assert.Equal("PRÊT", notice.Label);
        Assert.Equal("Dernière vente : ticket n° 0142 à 14:26 · 3\u202F320,80", notice.Text);
    }

    // ==================================================================== the cart

    [Fact]
    public void A_struck_line_says_it_was_removed_and_nothing_else()
    {
        var cart = new Cart();
        cart.Add(Product("a", stock: "0"), "613a");
        cart.Remove("a", Now.AddMinutes(-4));

        var row = Assert.Single(TillScreen.Build(State(cart)).Cart.Lines);

        Assert.True(row.Struck);
        Assert.Equal([new Chip(Tone.Neutral, "RETIRÉE")], row.Chips);
    }

    [Fact]
    public void A_line_beyond_recorded_stock_is_a_warning_that_says_what_to_check()
    {
        var cart = new Cart();
        cart.Add(Product("a", stock: "0"), "613a");

        var row = Assert.Single(TillScreen.Build(State(cart)).Cart.Lines);

        Assert.Equal([new Chip(Tone.Warning, "AU-DELÀ DU STOCK ENREGISTRÉ")], row.Chips);
    }

    [Fact]
    public void Only_a_live_line_can_be_selected_and_only_then_are_its_actions_shown()
    {
        var cart = CartWith(("a", "65.00", 4), ("b", "120.00", 1));
        cart.Remove("b", Now);

        var onLive = TillScreen.Build(State(cart, selected: "a")).Cart;
        var onStruck = TillScreen.Build(State(cart, selected: "b")).Cart;

        Assert.True(onLive.Lines[0].Selected);
        Assert.Equal(new LineActions("Retirer la ligne", "F8"), onLive.Actions);
        Assert.False(onStruck.Lines[1].Selected);
        Assert.Null(onStruck.Actions);
    }

    [Fact]
    public void A_line_shows_its_count_its_unit_price_and_its_total()
    {
        var row = Assert.Single(TillScreen.Build(State(CartWith(("a", "65.00", 4)))).Cart.Lines);

        Assert.Equal(("4", "65,00", "260,00"), (row.Quantity, row.UnitPrice, row.Total));
    }

    [Fact]
    public void An_empty_ticket_says_so()
    {
        var cart = TillScreen.Build(State()).Cart;

        Assert.Equal(new EmptyState("Le ticket est vide", "Scannez un article."), cart.Empty);
    }

    // ============================================================= the bottom bar

    [Theory]
    [InlineData("3320.80", "Espèces 3\u202F320,00\u00A0DA")]
    [InlineData("3322.40", "Espèces 3\u202F320,00\u00A0DA")]
    [InlineData("3322.50", "Espèces 3\u202F325,00\u00A0DA")]
    [InlineData("3325.00", "Espèces 3\u202F325,00\u00A0DA")]
    public void Encaisser_asks_for_the_cash_the_drawer_can_take(string total, string expected)
    {
        // D-034: the tender rounds to 5 DA, never the invoice. A button asking for 3 320,80 in
        // cash asks for coins that do not exist.
        var screen = TillScreen.Build(State(CartWith(("a", total, 1))));

        Assert.Equal(expected, screen.Bottom.Primary.Detail);
        Assert.Equal("F12", screen.Bottom.Primary.Key);
    }

    [Fact]
    public void The_total_to_pay_is_not_rounded()
    {
        var bottom = TillScreen.Build(State(CartWith(("a", "3320.80", 1)))).Bottom;

        Assert.Equal("3\u202F320,80\u00A0DA", bottom.BigFigure);
    }

    [Fact]
    public void Encaisser_is_unavailable_until_the_ticket_holds_a_live_line()
    {
        var removed = CartWith(("a", "65.00", 1));
        removed.Remove("a", Now);

        Assert.False(TillScreen.Build(State()).Bottom.Primary.Enabled);
        Assert.False(TillScreen.Build(State(removed)).Bottom.Primary.Enabled);
        Assert.True(TillScreen.Build(State(CartWith(("a", "65.00", 1)))).Bottom.Primary.Enabled);
    }

    [Fact]
    public void A_struck_line_is_not_in_the_total()
    {
        var cart = CartWith(("a", "65.00", 1), ("b", "150.00", 1));
        cart.Remove("b", Now);

        var bottom = TillScreen.Build(State(cart)).Bottom;

        Assert.Equal("65,00\u00A0DA", bottom.BigFigure);
    }

    [Fact]
    public void An_empty_ticket_totals_zero_in_the_stores_currency()
    {
        Assert.Equal("0,00\u00A0DA", TillScreen.Build(State()).Bottom.BigFigure);
    }

    // ======================================================== after the sale

    [Fact]
    public void A_recorded_sale_shows_what_it_came_to_and_what_the_drawer_takes()
    {
        var paid = new PaidTicket(Sale, [], Now);

        var screen = TillScreen.Build(State(paid: paid));

        var rail = Assert.IsType<Rail.Paid>(screen.Rail);
        Assert.Equal("Ticket n° 0142", rail.Title);
        Assert.Equal("14:32 · Nabil B.", rail.Subtitle);
        Assert.Equal(
            [
                new Figure("Total du ticket", "3\u202F320,80"),
                new Figure("Arrondi espèces", "\u22120,80"),
                new Figure("ESPÈCES DUES", "3\u202F320,00"),
            ],
            rail.Figures);
        Assert.Equal(new PrimaryKey("Nouvelle vente", null, "Entrée", true), screen.Bottom.Primary);
        Assert.Equal("3\u202F320,00\u00A0DA", screen.Bottom.BigFigure);
    }

    [Fact]
    public void An_unconfirmed_sale_takes_the_rail_and_offers_no_retry()
    {
        // Nothing yet makes a second request harmless: retrying a sale the server did record
        // would record it twice (D-070). The card says what to do instead.
        var screen = TillScreen.Build(State(
            CartWith(("a", "65.00", 1)),
            notice: new TillNotice(TillNoticeKind.SaleOutcomeUnknown, "-", "timeout")));

        var rail = Assert.IsType<Rail.Unconfirmed>(screen.Rail);
        Assert.Equal("VENTE NON CONFIRMÉE", rail.Label);
        Assert.Contains("Ne rendez pas la monnaie", rail.Body, StringComparison.Ordinal);
        Assert.Single(screen.Cart.Lines);
    }

    // ================================================================ Almanac

    private static RecommendationEnvelope NearExpiry(string id) => new(
        id, "store", "near_expiry", Department.Inventory, Urgency.Warning, ActionType.Binary,
        RecommendationSubject.Batch, "batch-" + id,
        "Soummam nature 1 L : péremption dans 2 jours",
        new BecauseBlock(
            "near_expiry",
            new Dictionary<string, string> { ["product_name"] = "Soummam nature 1 L", ["expires_on"] = "2026-09-25" },
            [
                new BecauseFactor("days_to_expiry", "2", "days", FactorDirection.Supports),
                new BecauseFactor("units_on_hand", "18", "pc", FactorDirection.Supports),
                new BecauseFactor("value_at_cost", "1260.00", "DZD", FactorDirection.Context),
            ]),
        null, null, Now, 1, "store_evaluator", "manager", RecommendationStatus.Pending, Now, null, null,
        [new RecommendationOption("opt-" + id, "Appliquer une démarque", 1, new IntentPayload("apply_markdown", new Dictionary<string, string>()), null)]);

    private static BoardAnswer Board(int withheld, params RecommendationEnvelope[] cards) =>
        new(BoardOutcome.Answered, "Karim M.", "manager", withheld, cards);

    [Fact]
    public void No_board_means_no_Almanac_slot()
    {
        var unknown = new BoardAnswer(BoardOutcome.UnknownStaff, null, null, 0, []);

        Assert.Null(Assert.IsType<Rail.Rest>(TillScreen.Build(State()).Rail).Almanac);
        Assert.Null(Assert.IsType<Rail.Rest>(TillScreen.Build(State(board: unknown)).Rail).Almanac);
    }

    [Fact]
    public void A_cashier_with_no_cards_sees_the_count_waiting_for_the_manager_and_no_card()
    {
        // D-074: cards above your rank are counted, never shown. An empty slot with no count
        // would tell the cashier the engine found nothing.
        var slot = Assert.IsType<Rail.Rest>(TillScreen.Build(State(board: Board(2))).Rail).Almanac;

        var quiet = Assert.IsType<AlmanacSlot.Quiet>(slot);
        Assert.Equal("Aucune carte pour vous pour l'instant.", quiet.Text);
        Assert.Equal("2 cartes attendent le responsable", quiet.Awaiting);
    }

    [Fact]
    public void A_card_is_written_from_its_Because_block_in_the_tills_language()
    {
        var slot = Assert.IsType<Rail.Rest>(TillScreen.Build(State(board: Board(0, NearExpiry("r1"), NearExpiry("r2")))).Rail).Almanac;

        var card = Assert.IsType<AlmanacSlot.Card>(slot);
        Assert.Equal("PÉREMPTION", card.Kind);
        Assert.Equal("1 / 2", card.Position);
        Assert.Equal("Soummam nature 1 L : péremption dans 2 jours", card.Claim);
        Assert.Equal("18 en stock, 1\u202F260,00\u00A0DA au prix d'achat. Le lot expire le 25/09.", card.Detail);
        Assert.Equal(new CardAction("opt-r1", "Démarquer"), card.Primary);
        Assert.Null(card.Awaiting);
    }

    [Fact]
    public void A_card_has_three_answers_and_adjust_records_nothing_yet()
    {
        // The brand: a suggestion has three answers, never a yes or no. D-074 records accept and
        // dismiss only, so Adjust is on the card and carries no option to record.
        var card = Assert.IsType<AlmanacSlot.Card>(
            Assert.IsType<Rail.Rest>(TillScreen.Build(State(board: Board(0, NearExpiry("r1")))).Rail).Almanac);

        Assert.NotNull(card.Primary);
        Assert.Equal(new CardAction(null, "Ajuster"), card.Adjust);
        Assert.Equal(new CardAction(null, "Ignorer"), card.Dismiss);
    }

    [Fact]
    public void The_card_index_wraps_round_the_board()
    {
        var board = Board(0, NearExpiry("r1"), NearExpiry("r2"));

        var third = Assert.IsType<AlmanacSlot.Card>(Assert.IsType<Rail.Rest>(TillScreen.Build(State(board: board, cardIndex: 2)).Rail).Almanac);
        var before = Assert.IsType<AlmanacSlot.Card>(Assert.IsType<Rail.Rest>(TillScreen.Build(State(board: board, cardIndex: -1)).Rail).Almanac);

        Assert.Equal(("r1", "1 / 2"), (third.RecommendationId, third.Position));
        Assert.Equal(("r2", "2 / 2"), (before.RecommendationId, before.Position));
    }

    [Fact]
    public void A_card_the_till_cannot_word_falls_back_to_its_headline()
    {
        var odd = NearExpiry("r1") with
        {
            RecommendationType = "reorder",
            Because = new BecauseBlock("reorder", new Dictionary<string, string>(), []),
            Headline = "Réassort suggéré : 48 unités",
        };

        var card = Assert.IsType<AlmanacSlot.Card>(Assert.IsType<Rail.Rest>(TillScreen.Build(State(board: Board(0, odd))).Rail).Almanac);

        Assert.Equal("Réassort suggéré : 48 unités", card.Claim);
        Assert.Null(card.Detail);
    }

    [Fact]
    public void Almanac_leaves_the_rail_while_a_sale_is_being_settled()
    {
        var board = Board(0, NearExpiry("r1"));

        Assert.IsType<Rail.Paid>(TillScreen.Build(State(paid: new PaidTicket(Sale, [], Now), board: board)).Rail);
    }

    // ================================================================= Arabic

    [Fact]
    public void Arabic_turns_the_screen_round_and_keeps_its_figures()
    {
        var screen = TillScreen.Build(State(CartWith(("a", "65.00", 4)), language: TillLanguage.Arabic));

        Assert.True(screen.RightToLeft);
        Assert.Equal("التذكرة الحالية", screen.Top.Tab.Title);
        Assert.Equal("سطر واحد", screen.Top.Tab.Detail);
        Assert.Equal("أمين صندوق", screen.Top.Staff!.Role);
        Assert.Equal("260,00\u00A0د.ج", screen.Bottom.BigFigure);
    }
}
