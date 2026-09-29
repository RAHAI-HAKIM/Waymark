using Waymark.Contracts.Pos;
using Waymark.Contracts.Reference;
using Waymark.Domain.Values;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;
using Waymark.Pos.Server;

namespace Waymark.Pos.Tests;

/// <summary>
/// A price typed at the counter, at the till (B5, D-092), and the note a discount reason can ask for
/// (F-28). The silent failures: a new price shown but the old one sent; a price past the band let
/// through by the panel; a price below cost charged without a word; an overridden line that takes a
/// repeat scan at its new price; a note that never leaves the till.
/// </summary>
public sealed class TillOverrideTests
{
    private static readonly TimeZoneInfo Algiers = TimeZoneInfo.CreateCustomTimeZone("Africa/Algiers", TimeSpan.FromHours(1), "Algiers", "Algiers");

    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    private static ProductForSale Milk(string id = "1") =>
        new("v-" + id, "p-" + id, "Lait", "1L", "pc", 0, 900, TvaRateSource.FromCategory, "143.00", "DZD", false, "40", UnitCost: "100.00");

    private static PriceOverride Override(long centimes) => new(Dzd(centimes), "etiquette_rayon", "Étiquette rayon", "ملصق الرف", "auth-o");

    private static readonly ReasonCodeList OverrideReasons = new("price_override",
        [new ReasonCodeOption("etiquette_rayon", "ملصق الرف", "Étiquette rayon", false, false)]);

    private static readonly ReasonCodeList DiscountReasons = new("discount",
        [new ReasonCodeOption("autre", "أخرى", "Autre", true, false)]);

    private static ScreenState State(Cart cart, DiscountState? discounting = null, string? selected = null) => new(
        TillText.For(TillLanguage.French), Algiers, new DateTimeOffset(2026, 9, 29, 13, 32, 0, TimeSpan.Zero), cart, null, null, null, null,
        ServerState.Reachable, new TillContext(TillContextOutcome.Found, "El Bahdja", "Caisse 1", "DZD", "Nabil B.", "Caissier", "أمين الصندوق"),
        selected, null, 0, Discounting: discounting);

    private static Cart CartOfMilk(int count = 2)
    {
        var cart = new Cart();
        cart.Add(Milk(), "6131", count);
        return cart;
    }

    private static DiscountState Pricing(Cart cart, string typed, string? reason = "etiquette_rayon") =>
        new(cart.Lines[0].LineId, DiscountForms.Amount, typed, OverrideReasons, false, reason, Kind: CounterKind.PriceOverride);

    // ------------------------------------------------------------------ the cart

    [Fact]
    public void An_overridden_line_charges_the_new_price_and_keeps_the_one_in_force()
    {
        var cart = CartOfMilk();

        Assert.True(cart.SetOverride(cart.Lines[0].LineId, Override(12_000)));

        Assert.Equal((Dzd(14_300), Dzd(12_000), Dzd(24_000)), (cart.Lines[0].UnitPrice, cart.Lines[0].ChargedPrice, cart.Total!.Value));
    }

    [Fact]
    public void An_overridden_line_takes_no_repeat_scan()
    {
        var cart = CartOfMilk(1);
        cart.SetOverride(cart.Lines[0].LineId, Override(12_000));

        cart.Add(Milk(), "6131");

        Assert.Equal(2, cart.Lines.Count);
        Assert.Equal(Dzd(14_300), cart.Lines[1].ChargedPrice);
    }

    [Fact]
    public void A_weighed_line_takes_no_override()
    {
        var cart = new Cart();
        var tomatoes = new ProductForSale("v-t", "p-t", "Tomates", "Vrac", "kg", 3, 900, TvaRateSource.FromCategory, "180.00", "DZD", false, "40", IsWeighted: true);
        var line = cart.AddWeighed(tomatoes, "4011", new LineWeight(Quantity.FromThousandths(556, "kg"), QuantitySources.TypedWeight, Dzd(10_008), 3));

        Assert.False(cart.SetOverride(line.LineId, Override(15_000)));
    }

    [Fact]
    public void An_override_is_sent_as_the_price_typed_and_a_note_with_its_discount()
    {
        Assert.Equal(new PriceOverrideRequest("120.00", "etiquette_rayon", "auth-o"), Override(12_000).ToWire());
        Assert.Equal("client fidèle", new CounterDiscount("percent", 1_000, "autre", "Autre", "أخرى", "auth-1", "client fidèle").ToWire().Note);
    }

    // ------------------------------------------------------------------ what the screen shows

    [Fact]
    public void An_overridden_line_shows_its_new_price_and_says_so()
    {
        var cart = CartOfMilk();
        cart.SetOverride(cart.Lines[0].LineId, Override(12_000));

        var row = TillScreen.Build(State(cart)).Cart.Lines[0];

        Assert.Equal("120,00", row.UnitPrice);
        Assert.Contains(row.Chips, chip => chip.Label == "PRIX MODIFIÉ");
    }

    [Fact]
    public void The_price_key_is_offered_under_a_counted_line()
    {
        var cart = CartOfMilk();

        Assert.Equal("Prix", TillScreen.Build(State(cart, selected: cart.Lines[0].LineId)).Cart.Actions!.Price);
    }

    [Fact]
    public void The_panel_shows_the_new_total_within_the_band()
    {
        var cart = CartOfMilk();

        var panel = Assert.IsType<Rail.Discount>(TillScreen.Build(State(cart, Pricing(cart, "120"))).Rail);

        Assert.Equal("Nouveau total 240,00 DA", panel.Preview);
        Assert.True(panel.MayContinue);
        Assert.Empty(panel.Forms);
    }

    [Fact]
    public void Past_the_band_the_panel_says_the_ceiling_and_will_not_continue()
    {
        var cart = CartOfMilk();

        var panel = Assert.IsType<Rail.Discount>(TillScreen.Build(State(cart, Pricing(cart, "180"))).Rail);

        Assert.False(panel.MayContinue);
        Assert.True(panel.Refused);
        Assert.StartsWith("Au plus 171,60", panel.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Below_cost_the_panel_warns_and_still_continues()
    {
        var cart = CartOfMilk();

        var panel = Assert.IsType<Rail.Discount>(TillScreen.Build(State(cart, Pricing(cart, "90"))).Rail);

        Assert.True(panel.MayContinue);
        Assert.False(panel.Refused);
        Assert.StartsWith("SOUS LE PRIX D'ACHAT", panel.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("143")]  // unchanged
    [InlineData("0")]
    public void A_price_that_changes_nothing_or_is_nothing_does_not_continue(string typed)
    {
        var cart = CartOfMilk();

        Assert.False(Assert.IsType<Rail.Discount>(TillScreen.Build(State(cart, Pricing(cart, typed))).Rail).MayContinue);
    }

    [Fact]
    public void A_reason_that_asks_for_a_note_takes_the_field_until_one_is_written()
    {
        var cart = CartOfMilk();
        var step = new DiscountState(null, DiscountForms.Percent, "10", DiscountReasons, false, "autre", Noting: true);

        var empty = Assert.IsType<Rail.Discount>(TillScreen.Build(State(cart, step)).Rail);
        var written = Assert.IsType<Rail.Discount>(TillScreen.Build(State(cart, step with { Note = "client fidèle" })).Rail);

        Assert.Equal(new FieldChip("NOTE", Active: true), TillScreen.Build(State(cart, step)).Field);
        Assert.False(empty.MayContinue);
        Assert.StartsWith("Précisez « Autre »", empty.Message, StringComparison.Ordinal);
        Assert.True(written.MayContinue);
        Assert.Equal("« client fidèle »", written.Preview);
    }

    // ------------------------------------------------------------------ the session

    private sealed class Products : IProductSource
    {
        public Task<LookupAnswer> LookupAsync(string barcode, CancellationToken cancellationToken = default) =>
            Task.FromResult<LookupAnswer>(new LookupAnswer.Answered(new ProductLookup(ProductLookupOutcome.Found, barcode, Milk(barcode), null)));
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
    public async Task A_sale_sends_the_override_as_typed_and_the_server_checks_it_again()
    {
        var sales = new Sales();
        var session = new TillSession(new Products(), sales, new TillIdentity("till-1"));
        session.SignIn(new SignedInStaff("nabil", "token"));
        await session.SubmitAsync("111");

        Assert.True(session.OverridePrice(session.Cart.Lines[0].LineId, Override(12_000)));
        await session.PayAsync();

        Assert.Equal(Override(12_000).ToWire(), Assert.Single(Assert.Single(sales.Sent).Lines).PriceOverride);
    }
}
