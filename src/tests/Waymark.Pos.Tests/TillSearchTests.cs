using Waymark.Contracts.Pos;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;
using Waymark.Pos.Server;

namespace Waymark.Pos.Tests;

/// <summary>
/// B1 at the till (D-088): "QTÉ × n", a product sold from a search, a past ticket opened read-only,
/// and what the screen shows of each. The silent failures: a count typed before a scan that stays
/// for the next, a search result and a scan that count differently, a sale sent from under a past
/// ticket, and an unsellable result that a touch sells anyway.
/// </summary>
public sealed class TillSearchTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 13, 32, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Algiers = TimeZoneInfo.CreateCustomTimeZone("Africa/Algiers", TimeSpan.FromHours(1), "Algiers", "Algiers");

    private static ProductForSale Milk(string price = "143.00", string stock = "12") =>
        new("v-milk", "p-milk", "Lait UHT Candia", "Brique 1L", "pc", 0, 900, TvaRateSource.FromCategory, price, "DZD", false, stock);

    private sealed class Products : IProductSource
    {
        public Task<LookupAnswer> LookupAsync(string barcode, CancellationToken cancellationToken = default) =>
            Task.FromResult<LookupAnswer>(new LookupAnswer.Answered(new ProductLookup(ProductLookupOutcome.Found, barcode, Milk(), null)));
    }

    private sealed class Sales : IStoreSales
    {
        public List<SaleRequest> Sent { get; } = [];

        public Task<SaleAnswer> CompleteSaleAsync(SaleRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult<SaleAnswer>(new SaleAnswer.Refused("test"));
        }
    }

    private readonly Sales _sales = new();

    private TillSession Till()
    {
        var session = new TillSession(new Products(), _sales, new TillIdentity("till-1"));
        session.SignIn(new SignedInStaff("nabil", "token", "Nabil B."));
        return session;
    }

    private static readonly PastTicketDetail Past = new(
        "t-142", "S-2026-000142", Now.AddHours(-4), "till-1", "Nabil B.", "completed",
        [
            new PastTicketLineWire("Lait UHT Candia", "Brique 1L", "2", "pc", "143.00", "286.00"),
            new PastTicketLineWire("Pain", "Baguette", "1", "pc", "20.00", "20.00"),
        ],
        "306.00", "48.86", "305.00", "DZD", [new PastPaymentWire("cash", "305.00")]);

    // ================================================================ QTÉ × n

    [Fact]
    public async Task A_count_typed_before_a_scan_is_spent_by_it()
    {
        var session = Till();

        session.SetNextCount(3);
        await session.SubmitAsync("111");
        await session.SubmitAsync("111");

        Assert.Equal(4, Assert.Single(session.Cart.Lines).Count);
        Assert.Equal(1, session.NextCount);
    }

    [Fact]
    public async Task A_search_result_takes_the_count_as_a_scan_does()
    {
        var session = Till();

        session.SetNextCount(5);
        await session.AddFoundAsync(Milk(), "6130000000017");

        var line = Assert.Single(session.Cart.Lines);
        Assert.Equal((5, "6130000000017"), (line.Count, line.Barcode));
        Assert.Equal(1, session.NextCount);
    }

    [Fact]
    public void A_count_outside_the_stepper_s_range_is_refused_and_echap_resets_it()
    {
        var session = Till();

        session.SetNextCount(0);
        session.SetNextCount(10_000);
        Assert.Equal(1, session.NextCount);

        session.SetNextCount(7);
        session.ResetNextCount();
        Assert.Equal(1, session.NextCount);
    }

    // ================================================================ a past ticket

    [Fact]
    public async Task Nothing_is_sold_or_put_aside_while_a_past_ticket_is_open()
    {
        // The ticket that would be sold is hidden under the past one.
        var session = Till();
        await session.SubmitAsync("111");

        session.View(Past);
        await session.PayAsync();

        Assert.Empty(_sales.Sent);
        Assert.False(session.Park());
        Assert.False(session.CancelTicket());
        Assert.Single(session.Cart.ActiveLines);
    }

    [Fact]
    public async Task A_scan_closes_the_past_ticket_and_goes_into_the_ticket_on_screen()
    {
        var session = Till();
        session.View(Past);

        await session.SubmitAsync("111");

        Assert.Null(session.Viewing);
        Assert.Single(session.Cart.Lines);
    }

    [Fact]
    public void A_past_ticket_is_shown_read_only_in_the_ticket_view()
    {
        var screen = TillScreen.Build(State(viewing: Past));

        Assert.Equal(["Lait UHT Candia Brique 1L", "Pain Baguette"], screen.Cart.Lines.Select(line => line.Article));
        Assert.All(screen.Cart.Lines, line => Assert.False(line.Selected));
        Assert.Null(screen.Cart.Actions);
        Assert.Equal(("Ticket n° S-2026-000142", "25/09 10:32"), (screen.Top.Tab!.Title, screen.Top.Tab.Detail));
        Assert.Equal(("TICKET PASSÉ", "Vendu le 25/09 à 10:32 par Nabil B. · lecture seule"), (screen.Notice.Label, screen.Notice.Text));
        Assert.Equal(new PrimaryKey("Fermer", null, "Échap", true), screen.Bottom.Primary);
        Assert.Equal("305,00\\u00A0DA".Replace("\\u00A0", " ", StringComparison.Ordinal), screen.Bottom.BigFigure);
        Assert.Null(screen.Results);
    }

    [Fact]
    public void A_past_ticket_shows_its_payments_and_figures_as_recorded()
    {
        var rail = Assert.IsType<Rail.Past>(TillScreen.Build(State(viewing: Past)).Rail);

        Assert.Contains(new Figure("Total du ticket", "305,00"), rail.Figures);
        Assert.Contains(new Figure("Espèces", "305,00"), rail.Figures);
        // What transactions.subtotal holds is the total before TVA, and the ticket says so (block B review).
        Assert.Contains(new Figure("Total HT", "306,00"), rail.Figures);
    }

    [Theory]
    [InlineData("voided", "ANNULÉE")]
    [InlineData("refunded", "REMBOURSÉE")]
    [InlineData("partially_refunded", "REMBOURSÉE EN PARTIE")]
    public void A_ticket_that_is_not_a_plain_sale_says_so_first(string status, string label)
    {
        var notice = TillScreen.Build(State(viewing: Past with { Status = status })).Notice;

        Assert.Equal((Tone.Warning, label), (notice.Tone, notice.Label));
    }

    // ================================================================ the search's results

    private static ProductSearchAnswer Answer(params ProductSearchResult[] results) => new("lait", results);

    private static ProductSearchResult Found(string id, string code = "6130000000017") =>
        new(id, "Lait UHT Candia", "Brique 1L", code, ProductLookupOutcome.Found, Milk(), null);

    private static ProductSearchResult Refused(string id, string reason) =>
        new(id, "Beurre", "250g", "613", ProductLookupOutcome.NotSellable, null, reason);

    [Fact]
    public void A_result_shows_its_price_and_stock_and_can_be_touched()
    {
        var results = TillScreen.Build(State(search: new SearchState("lait", Answer(Found("v1")), false, 0))).Results!;

        var row = Assert.Single(results.Rows);
        Assert.Equal(("Lait UHT Candia Brique 1L", "143,00 DA · stock 12", true, true), (row.Title, row.Detail, row.Available, row.Highlighted));
    }

    [Fact]
    public void An_unsellable_result_says_why_and_cannot_be_touched()
    {
        var row = Assert.Single(TillScreen.Build(State(search: new SearchState("beurre", Answer(Refused("v2", NotSellableReason.NoCurrentPrice)), false, 0))).Results!.Rows);

        Assert.False(row.Available);
        Assert.Equal(new Chip(Tone.Warning, "NON VENDABLE"), row.Chip);
        Assert.Equal("aucun prix en vigueur aujourd'hui", row.Detail);
    }

    [Fact]
    public void While_asked_nothing_found_and_offline_are_three_sentences_never_an_empty_list()
    {
        Assert.Equal("Recherche…", TillScreen.Build(State(search: new SearchState("lait", null, false, 0))).Results!.Message);
        Assert.Contains("lait x", TillScreen.Build(State(search: new SearchState("lait x", Answer(), false, 0))).Results!.Message, StringComparison.Ordinal);
        Assert.Contains("injoignable", TillScreen.Build(State(search: new SearchState("lait", null, true, 0))).Results!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_field_chip_says_the_next_count_and_marks_one_typed()
    {
        Assert.Equal(new FieldChip("QTÉ × 1", false), TillScreen.Build(State()).Field);
        Assert.Equal(new FieldChip("QTÉ × 3", true), TillScreen.Build(State(nextCount: 3)).Field);
    }

    [Fact]
    public void Two_frames_of_the_same_search_redraw_nothing()
    {
        var search = new SearchState("lait", Answer(Found("v1"), Refused("v2", NotSellableReason.Archived)), false, 0);

        var changes = TillScreen.Compare(TillScreen.Build(State(search: search)), TillScreen.Build(State(search: search)));

        Assert.Equal(new FrameChanges(false, false, false, false, false), changes);
    }

    // ================================================================ the Tickets list

    private static TicketList List(string outcome, params TicketSummary[] tickets) => new(outcome, "2026-09-25", false, tickets);

    [Fact]
    public void Todays_list_names_the_day_and_each_ticket_with_its_time_lines_and_total()
    {
        var list = List(TicketOutcomes.Answered,
            new TicketSummary("t1", "S-2026-000142", Now.AddHours(-4), "till-1", "completed", 2, "305.00", "DZD"),
            new TicketSummary("t2", "S-2026-000141", Now.AddHours(-5), "till-1", "voided", 1, "20.00", "DZD"));

        var rail = Assert.IsType<Rail.Tickets>(TillScreen.Build(State(tickets: new TicketsState(new DateOnly(2026, 9, 25), false, list, false))).Rail);

        Assert.Equal(("Aujourd'hui · 25/09", "Cette caisse", "Toutes les caisses", false), (rail.Day, rail.Scope, rail.OtherScope, rail.MayGoForward));
        Assert.Equal(("10:32 · Ticket n° S-2026-000142", "2 lignes · 305,00"), (rail.Rows[0].Title, rail.Rows[0].Detail));
        Assert.Equal(new Chip(Tone.Warning, "ANNULÉE"), rail.Rows[1].Chip);
    }

    [Fact]
    public void Another_day_can_go_forward_and_a_refusal_is_said()
    {
        var yesterday = new TicketsState(new DateOnly(2026, 9, 24), false, List(TicketOutcomes.NotAllowed), false);

        var rail = Assert.IsType<Rail.Tickets>(TillScreen.Build(State(tickets: yesterday)).Rail);

        Assert.True(rail.MayGoForward);
        Assert.Equal("24/09", rail.Day);
        Assert.Empty(rail.Rows);
        Assert.Contains("réservé au responsable", rail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tickets_is_an_operation_key_always_available()
    {
        var keys = Assert.IsType<Rail.Rest>(TillScreen.Build(State()).Rail).Operations;

        Assert.Contains(new OperationKey(Operation.Tickets, "Tickets", null, true), keys);
    }

    private static ScreenState State(
        PastTicketDetail? viewing = null,
        SearchState? search = null,
        int nextCount = 1,
        TicketsState? tickets = null) =>
        new(TillText.French, Algiers, Now, new Cart(), null, null, null, null, ServerState.Reachable,
            new TillContext(TillContextOutcome.Found, "El Bahdja", "Caisse 1", "DZD", "Nabil B.", "Caissier", "أمين الصندوق"),
            null, null, 0, null, null, null, false, search, nextCount, viewing, tickets);
}
