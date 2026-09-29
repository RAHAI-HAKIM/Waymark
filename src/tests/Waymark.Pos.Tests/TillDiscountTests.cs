using Waymark.Contracts.Pos;
using Waymark.Contracts.Reference;
using Waymark.Domain.Values;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;
using Waymark.Pos.Server;

namespace Waymark.Pos.Tests;

/// <summary>
/// A discount given at the counter, at the till (B4, D-091). The silent failures: a preview that is
/// not what the sale will charge; a discounted line that takes a repeat scan into its discount; a
/// discount sent as money rather than as what was given; a panel that lets a discount through with
/// no reason; the manager's PIN on the screen.
/// </summary>
public sealed class TillDiscountTests
{
    private static readonly TimeZoneInfo Algiers = TimeZoneInfo.CreateCustomTimeZone("Africa/Algiers", TimeSpan.FromHours(1), "Algiers", "Algiers");

    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    private static ProductForSale Product(string id, string price) =>
        new("v-" + id, "p-" + id, "Café Bonal", "250 g", "pc", 0, 900, TvaRateSource.FromCategory, price, "DZD", false, "40");

    private static CounterDiscount Percent(int basisPoints) =>
        new(DiscountForms.Percent, basisPoints, "geste_commercial", "Geste commercial", "لفتة تجارية", "auth-1");

    private static CounterDiscount Amount(long centimes) =>
        new(DiscountForms.Amount, centimes, "geste_commercial", "Geste commercial", "لفتة تجارية", "auth-1");

    private static readonly ReasonCodeList Reasons = new("discount",
        [new ReasonCodeOption("geste_commercial", "لفتة تجارية", "Geste commercial", false, false),
         new ReasonCodeOption("article_abime", "منتج تالف", "Article abîmé", false, false)]);

    private static ScreenState State(Cart cart, DiscountState? discounting = null, string? selected = null) => new(
        TillText.For(TillLanguage.French), Algiers, new DateTimeOffset(2026, 9, 28, 13, 32, 0, TimeSpan.Zero), cart, null, null, null, null,
        ServerState.Reachable, new TillContext(TillContextOutcome.Found, "El Bahdja", "Caisse 1", "DZD", "Nabil B.", "Caissier", "أمين الصندوق"),
        selected, null, 0, Discounting: discounting);

    private static Cart CartOf(params (string Id, string Price, int Count)[] lines)
    {
        var cart = new Cart();
        foreach (var (id, price, count) in lines)
        {
            cart.Add(Product(id, price), "613" + id, count);
        }

        return cart;
    }

    // ------------------------------------------------------------------ the cart's figures

    [Fact]
    public void A_line_discount_is_shown_under_the_line_and_comes_off_the_total()
    {
        var cart = CartOf(("1", "420.00", 1), ("2", "120.00", 2));
        cart.SetDiscount(cart.Lines[0].LineId, Percent(1_000));

        Assert.Equal((Dzd(66_000), Dzd(4_200), Dzd(61_800)), (cart.Subtotal!.Value, cart.DiscountTotal!.Value, cart.Total!.Value));
        Assert.Equal(Dzd(42_000), cart.Lines[0].LineTotal); // the line itself is as priced; the discount is shown under it
    }

    [Fact]
    public void A_ticket_discount_follows_the_ticket_as_it_changes()
    {
        var cart = CartOf(("1", "100.00", 1));
        cart.SetTicketDiscount(Percent(1_000));
        Assert.Equal(Dzd(1_000), cart.DiscountTotal!.Value);

        cart.Add(Product("2", "100.00"), "6132");

        Assert.Equal(Dzd(2_000), cart.DiscountTotal!.Value);
    }

    [Fact]
    public void A_ticket_amount_is_capped_at_what_the_ticket_comes_to()
    {
        var cart = CartOf(("1", "100.00", 1));
        cart.SetTicketDiscount(Amount(50_000));

        Assert.Equal(Dzd(0), cart.Total!.Value);
    }

    [Fact]
    public void A_discounted_line_takes_no_repeat_scan()
    {
        var cart = CartOf(("1", "420.00", 1));
        cart.SetDiscount(cart.Lines[0].LineId, Percent(1_000));

        cart.Add(Product("1", "420.00"), "6131");

        Assert.Equal(2, cart.Lines.Count);
        Assert.Null(cart.Lines[1].Discount);
    }

    [Fact]
    public void No_ticket_discount_on_a_ticket_with_nothing_in_the_sale_and_none_after_it_is_paid()
    {
        var cart = new Cart();
        Assert.False(cart.SetTicketDiscount(Percent(1_000)));

        var paid = CartOf(("1", "100.00", 1));
        paid.SetTicketDiscount(Percent(1_000));
        paid.Clear();
        Assert.Null(paid.TicketDiscount);
    }

    [Fact]
    public void A_discount_is_sent_as_what_was_given_never_as_money()
    {
        Assert.Equal(new DiscountRequest("percent", "10.00", "geste_commercial", "auth-1"), Percent(1_000).ToWire());
        Assert.Equal(new DiscountRequest("amount", "50.00", "geste_commercial", "auth-1"), Amount(5_000).ToWire());
    }

    [Fact]
    public void The_preview_rounds_by_the_stores_policy()
    {
        // 10 % of 0,05 is half a centime: 0,01 half up, nothing half even.
        var up = CartOf(("1", "0.05", 1));
        up.SetDiscount(up.Lines[0].LineId, Percent(1_000));
        var even = CartOf(("1", "0.05", 1));
        even.Policy = Rounding.HalfEven;
        even.SetDiscount(even.Lines[0].LineId, Percent(1_000));

        Assert.Equal((Dzd(1), Dzd(0)), (up.DiscountTotal!.Value, even.DiscountTotal!.Value));
    }

    // ------------------------------------------------------------------ what is typed

    [Theory]
    [InlineData("10", "percent", 1_000)]
    [InlineData("12,5", "percent", 1_250)]
    [InlineData("0.01", "percent", 1)]
    [InlineData("100", "percent", 10_000)]
    [InlineData("50", "amount", 5_000)]
    [InlineData("42,90", "amount", 4_290)]
    public void A_value_is_read_as_hundredths(string typed, string form, long hundredths)
    {
        Assert.True(DiscountEntry.TryParse(typed, form, out var read));
        Assert.Equal(hundredths, read);
    }

    [Theory]
    [InlineData("0", "percent")]
    [InlineData("100,01", "percent")]
    [InlineData("12,345", "percent")]
    [InlineData("0", "amount")]
    [InlineData("-5", "amount")]
    [InlineData("5,", "amount")]
    [InlineData("١٠", "percent")]
    [InlineData("", "amount")]
    public void Anything_else_is_refused(string typed, string form) =>
        Assert.False(DiscountEntry.TryParse(typed, form, out _));

    // ------------------------------------------------------------------ what the screen shows

    [Fact]
    public void A_discounted_line_reads_as_the_board_draws_it()
    {
        var cart = CartOf(("1", "420.00", 1));
        cart.SetDiscount(cart.Lines[0].LineId, Percent(1_000));

        var row = TillScreen.Build(State(cart)).Cart.Lines[0];

        Assert.Equal(new DiscountLine("REMISE 10 % · GESTE COMMERCIAL", "−42,00"), row.Discount);
    }

    [Fact]
    public void The_bottom_bar_says_the_subtotal_and_what_came_off()
    {
        var cart = CartOf(("1", "420.00", 1));
        cart.SetTicketDiscount(Amount(4_200));

        var bottom = TillScreen.Build(State(cart)).Bottom;

        Assert.Equal(["420,00", "−42,00"], bottom.Summary.Select(figure => figure.Value));
        Assert.StartsWith("378,00", bottom.BigFigure, StringComparison.Ordinal);
        Assert.NotNull(TillScreen.Build(State(cart)).Cart.TicketDiscount);
    }

    [Fact]
    public void The_panel_previews_what_the_value_takes_off_and_continues_only_with_a_reason()
    {
        var cart = CartOf(("1", "420.00", 1));
        var id = cart.Lines[0].LineId;

        var noReason = Assert.IsType<Rail.Discount>(TillScreen.Build(State(cart, new DiscountState(id, "percent", "10", Reasons, false, null))).Rail);
        var ready = Assert.IsType<Rail.Discount>(TillScreen.Build(State(cart, new DiscountState(id, "percent", "10", Reasons, false, "article_abime"))).Rail);

        Assert.Equal("−42,00 DA", noReason.Preview);
        Assert.False(noReason.MayContinue);
        Assert.True(ready.MayContinue);
        Assert.Equal([false, true], ready.Reasons.Select(reason => reason.Selected));
    }

    [Fact]
    public void A_shop_with_no_discount_reason_cannot_give_one_and_is_told_why()
    {
        var cart = CartOf(("1", "420.00", 1));
        var panel = Assert.IsType<Rail.Discount>(TillScreen.Build(State(cart,
            new DiscountState(null, "percent", "10", new ReasonCodeList("discount", []), false, null))).Rail);

        Assert.False(panel.MayContinue);
        Assert.StartsWith("Aucun motif de remise", panel.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void While_a_discount_is_given_the_field_says_so_and_encaisser_waits()
    {
        var cart = CartOf(("1", "420.00", 1));
        var screen = TillScreen.Build(State(cart, new DiscountState(null, "percent", "", Reasons, false, null)));

        Assert.Equal(new FieldChip("REMISE · %", Active: true), screen.Field);
        Assert.False(screen.Bottom.Primary.Enabled);
    }

    [Fact]
    public void The_manager_step_shows_dots_never_digits_and_says_a_wrong_pin()
    {
        var cart = CartOf(("1", "420.00", 1));
        var staff = new TillStaff([new TillStaffMember("samia", "Samia K.", "Responsable", "مسؤولة", true)]);
        var step = new DiscountState(null, "percent", "10", Reasons, false, "geste_commercial", Authorising: true, Staff: staff,
            ManagerId: "samia", PinLength: 4, Problem: DiscountProblem.WrongPin, AttemptsLeft: 3);

        var panel = Assert.IsType<Rail.Authorise>(TillScreen.Build(State(cart, step)).Rail);

        Assert.Equal(4, panel.PinLength);
        Assert.True(panel.MayValidate);
        Assert.Equal("PIN incorrect · 3 essais avant blocage", panel.Message);
        Assert.Equal("REMISE TICKET 10 %", panel.Title);
    }

    // ------------------------------------------------------------------ the session

    private sealed class Products : IProductSource
    {
        public Task<LookupAnswer> LookupAsync(string barcode, CancellationToken cancellationToken = default) =>
            Task.FromResult<LookupAnswer>(new LookupAnswer.Answered(new ProductLookup(ProductLookupOutcome.Found, barcode, Product(barcode, "420.00"), null)));
    }

    private sealed class Sales : IStoreSales
    {
        public List<SaleRequest> Sent { get; } = [];

        public Task<SaleAnswer> CompleteSaleAsync(SaleRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult<SaleAnswer>(new SaleAnswer.Completed(
                new SaleOutcome(SaleOutcomes.Completed, "t1", "S-2026-000001", "1.00", "0.08", "0.00", "DZD", null)));
        }
    }

    [Fact]
    public async Task A_sale_sends_each_discount_as_given_and_the_server_works_out_the_money()
    {
        var sales = new Sales();
        var session = new TillSession(new Products(), sales, new TillIdentity("till-1"));
        session.SignIn(new SignedInStaff("nabil", "token"));
        await session.SubmitAsync("111");
        await session.SubmitAsync("222");

        Assert.True(session.Discount(session.Cart.Lines[0].LineId, Percent(1_000)));
        Assert.True(session.Discount(null, Amount(5_000)));
        await session.PayAsync();

        var sent = Assert.Single(sales.Sent);
        Assert.Equal([Percent(1_000).ToWire(), null], sent.Lines.Select(line => line.Discount));
        Assert.Equal(Amount(5_000).ToWire(), sent.TicketDiscount);
    }

    [Fact]
    public void No_discount_on_a_ticket_with_nothing_in_the_sale()
    {
        var session = new TillSession(new Products(), new Sales(), new TillIdentity("till-1"));

        Assert.False(session.MayDiscount);
        Assert.False(session.Discount(null, Percent(1_000)));
    }

    [Fact]
    public async Task The_stores_policy_reaches_the_cart_on_screen_and_the_next_one()
    {
        var session = new TillSession(new Products(), new Sales(), new TillIdentity("till-1"));
        session.SignIn(new SignedInStaff("nabil", "token"));

        session.SetRoundingPolicy(Rounding.HalfEven);
        await session.SubmitAsync("111");
        session.Park();

        Assert.Equal(Rounding.HalfEven, session.Cart.Policy);
        Assert.Equal(Rounding.HalfEven, session.Parked[0].Cart.Policy);
    }
}
