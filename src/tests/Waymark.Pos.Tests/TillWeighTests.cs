using Waymark.Contracts.Pos;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;
using Waymark.Pos.Server;

namespace Waymark.Pos.Tests;

/// <summary>
/// Weighed goods at the till (B3, D-090). The silent failures: a weighed product added as one unit
/// of nothing; "3*" turned into one weighing without a word; a second weighing merged into the
/// first; a typed weight sent as a count, or a label's weight sent twice; a weight the till worked
/// out itself; a label's weight retyped.
/// </summary>
public sealed class TillWeighTests
{
    /// <summary>
    /// The server as the till sees it: PLU 4011 is tomatoes at 180,00/kg, sold by weight;
    /// "LABEL" is a scale label of them, 0,556 kg; 111 is milk, counted. A weight is priced as the
    /// server would, weight × price, rounded half up, and refused at zero.
    /// </summary>
    private sealed class Products : IProductSource
    {
        public List<(string Code, string Weight)> Weighed { get; } = [];

        public Task<LookupAnswer> LookupAsync(string barcode, CancellationToken cancellationToken = default) =>
            Task.FromResult<LookupAnswer>(new LookupAnswer.Answered(barcode switch
            {
                "4011" => new ProductLookup(ProductLookupOutcome.Found, barcode, Tomatoes, null),
                "LABEL" => new ProductLookup(ProductLookupOutcome.Found, barcode, Tomatoes, null,
                    new WeighedAnswer("0.556", QuantitySources.LabelWeight, "100.08")),
                _ => new ProductLookup(ProductLookupOutcome.Found, barcode, Milk, null),
            }));

        public Task<LookupAnswer> WeighAsync(string code, string weight, CancellationToken cancellationToken = default)
        {
            Weighed.Add((code, weight));
            var thousandths = (long)(decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture) * 1000);
            if (thousandths > 50_000)
            {
                return Task.FromResult<LookupAnswer>(new LookupAnswer.Answered(
                    new ProductLookup(ProductLookupOutcome.NotSellable, code, null, NotSellableReason.WeightInvalid)));
            }

            var centimes = ((thousandths * 18_000) + 500) / 1000;
            var total = $"{centimes / 100}.{centimes % 100:D2}";
            return Task.FromResult<LookupAnswer>(new LookupAnswer.Answered(new ProductLookup(
                ProductLookupOutcome.Found, code, Tomatoes, null, new WeighedAnswer(weight, QuantitySources.TypedWeight, total))));
        }

        private static ProductForSale Tomatoes { get; } = new(
            "v-tomatoes", "p-tomatoes", "Tomates", "Vrac", "kg", 3, 900, TvaRateSource.FromCategory, "180.00", "DZD", false, "40", IsWeighted: true);

        private static ProductForSale Milk { get; } = new(
            "v-milk", "p-milk", "Lait", "1L", "pc", 0, 900, TvaRateSource.FromCategory, "143.00", "DZD", false, "10");
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

    private readonly Products _products = new();
    private readonly Sales _sales = new();

    private TillSession Till()
    {
        var session = new TillSession(_products, _sales, new TillIdentity("till-1"));
        session.SignIn(new SignedInStaff("nabil", "token", "Nabil B."));
        return session;
    }

    /// <summary>Entrée on a weight, then waits for the weighing to land.</summary>
    private static async Task Weigh(TillSession session, string typed)
    {
        Assert.True(session.ConfirmWeight(typed));
        await session.SubmitAsync(string.Empty); // nothing to submit: waits for what is queued
    }

    // ------------------------------------------------------------------ asking for a weight

    [Fact]
    public async Task A_weighed_products_code_asks_for_a_weight_and_adds_no_line()
    {
        var session = Till();

        await session.SubmitAsync("4011");

        Assert.Empty(session.Cart.Lines);
        Assert.Equal(("4011", "Tomates", 3), (session.Weighing?.Code, session.Weighing?.ProductName, session.Weighing?.Decimals));
    }

    [Fact]
    public async Task A_typed_weight_is_weighed_by_the_server_and_goes_on_the_ticket_as_typed()
    {
        var session = Till();
        await session.SubmitAsync("4011");

        await Weigh(session, "0,556");

        Assert.Null(session.Weighing);
        Assert.Equal(("4011", "0.556"), Assert.Single(_products.Weighed));
        var line = Assert.Single(session.Cart.Lines);
        Assert.Equal((556L, QuantitySources.TypedWeight), (line.Quantity.Thousandths, line.Weight!.Source));
        Assert.Equal(10_008, line.LineTotal.MinorUnits);
    }

    [Theory]
    [InlineData("0,5555")] // finer than a gram
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task A_weight_the_unit_cannot_take_is_refused_before_anything_is_asked(string typed)
    {
        var session = Till();
        await session.SubmitAsync("4011");

        Assert.False(session.ConfirmWeight(typed));

        Assert.True(session.Weighing!.Invalid);
        Assert.Empty(_products.Weighed);
        Assert.Empty(session.Cart.Lines);
    }

    [Fact]
    public async Task A_weight_the_server_refuses_is_told_and_nothing_is_added()
    {
        var session = Till();
        await session.SubmitAsync("4011");

        await Weigh(session, "60");

        Assert.Empty(session.Cart.Lines);
        Assert.Equal((TillNoticeKind.NotSellable, NotSellableReason.WeightInvalid), (session.Notice?.Kind, session.Notice?.Detail));
    }

    [Fact]
    public async Task Echap_gives_the_weighing_up()
    {
        var session = Till();
        await session.SubmitAsync("4011");

        session.CancelWeighing();

        Assert.Null(session.Weighing);
        Assert.Empty(session.Cart.Lines);
    }

    [Fact]
    public async Task A_scan_gives_up_a_weight_not_yet_typed_and_is_sold()
    {
        var session = Till();
        await session.SubmitAsync("4011");

        await session.SubmitAsync("111");

        Assert.Null(session.Weighing);
        Assert.Equal("v-milk", Assert.Single(session.Cart.Lines).VariantId);
    }

    [Fact]
    public async Task A_touched_search_result_sold_by_weight_asks_for_a_weight_too()
    {
        var session = Till();
        var answer = await _products.LookupAsync("4011");
        var product = ((LookupAnswer.Answered)answer).Lookup.Product!;

        await session.AddFoundAsync(product, "4011");

        Assert.Equal("4011", session.Weighing?.Code);
        Assert.Empty(session.Cart.Lines);
    }

    // ------------------------------------------------------------------ labels and counts

    [Fact]
    public async Task A_label_goes_straight_on_the_ticket_with_its_weight()
    {
        var session = Till();

        await session.SubmitAsync("LABEL");

        Assert.Null(session.Weighing);
        var line = Assert.Single(session.Cart.Lines);
        Assert.Equal((556L, QuantitySources.LabelWeight), (line.Quantity.Thousandths, line.Weight!.Source));
    }

    [Fact]
    public async Task Two_labels_of_one_product_are_two_lines()
    {
        var session = Till();

        await session.SubmitAsync("LABEL");
        await session.SubmitAsync("LABEL");

        Assert.Equal(2, session.Cart.Lines.Count);
    }

    [Fact]
    public async Task A_count_typed_before_a_weighed_product_is_spent_and_the_cashier_told()
    {
        var session = Till();
        session.SetNextCount(3);

        await session.SubmitAsync("LABEL");

        Assert.Equal(1, session.NextCount);
        Assert.Equal(TillNoticeKind.WeighedTakesNoCount, session.Notice?.Kind);
        Assert.Single(session.Cart.Lines);
    }

    // ------------------------------------------------------------------ paying

    [Fact]
    public async Task A_typed_weight_is_sent_as_text_and_a_labels_weight_travels_in_its_code()
    {
        var session = Till();
        await session.SubmitAsync("4011");
        await Weigh(session, "1,5");
        await session.SubmitAsync("LABEL");
        await session.SubmitAsync("111");

        await session.PayAsync();

        var lines = Assert.Single(_sales.Sent).Lines;
        Assert.Equal(
            [("4011", 1, "1.5"), ("LABEL", 1, (string?)null), ("111", 1, null)],
            lines.Select(line => (line.Barcode, line.Count, line.Weight)));
    }

    [Fact]
    public async Task Nothing_is_paid_or_put_aside_while_a_weight_is_awaited()
    {
        var session = Till();
        await session.SubmitAsync("111");
        await session.SubmitAsync("4011");

        await session.PayAsync();

        Assert.Empty(_sales.Sent);
        Assert.False(session.MayPutAside);
    }

    // ------------------------------------------------------------------ typed again

    [Fact]
    public async Task Poids_types_a_typed_weight_again_on_the_same_line()
    {
        var session = Till();
        await session.SubmitAsync("4011");
        await Weigh(session, "0,556");
        var lineId = session.Cart.Lines[0].LineId;

        session.Reweigh(lineId);
        await Weigh(session, "1");

        var line = Assert.Single(session.Cart.Lines);
        Assert.Equal((lineId, 1_000L), (line.LineId, line.Quantity.Thousandths));
    }

    [Fact]
    public async Task A_labels_weight_is_not_typed_again()
    {
        var session = Till();
        await session.SubmitAsync("LABEL");

        session.Reweigh(session.Cart.Lines[0].LineId);

        Assert.Null(session.Weighing);
    }

    // ------------------------------------------------------------------ the card's preview

    [Fact]
    public async Task The_card_shows_the_servers_total_for_what_is_typed()
    {
        var session = Till();
        await session.SubmitAsync("4011");

        await session.PreviewWeightAsync("0,556");

        Assert.Equal(10_008, session.Weighing!.Preview!.LineTotal.MinorUnits);
        Assert.Empty(session.Cart.Lines);
    }

    [Fact]
    public async Task A_preview_of_text_that_is_not_a_weight_asks_nothing_and_says_so()
    {
        var session = Till();
        await session.SubmitAsync("4011");

        await session.PreviewWeightAsync("0,5,5");

        Assert.True(session.Weighing!.Invalid);
        Assert.Empty(_products.Weighed);
    }
}
