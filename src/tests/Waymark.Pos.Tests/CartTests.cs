using Waymark.Contracts.Pos;
using Waymark.Domain.Values;
using Waymark.Pos.Checkout;

namespace Waymark.Pos.Tests;

/// <summary>
/// The cart's preview arithmetic and the wire's figures read back (D-068). The
/// silent failures: a centime created by parsing, a repeat scan that adds a
/// second line instead of a second unit, a total that drops a line.
/// </summary>
public sealed class CartTests
{
    private static ProductForSale Product(
        string variantId = "v1", string price = "120.50", string stock = "10", string currency = "DZD",
        bool promotional = false) =>
        new(variantId, "p-" + variantId, "Lait UHT Candia", "Brique 1L", "pc", 0, 1_900,
            TvaRateSource.FromCategory, price, currency, promotional, stock);

    private static Money Dzd(long minorUnits) => Money.FromMinorUnits(minorUnits, Currency.Dzd);

    // ------------------------------------------------------------- figures

    [Theory]
    [InlineData("120.50", 12_050)]
    [InlineData("120.5", 12_050)]
    [InlineData("0.05", 5)]
    [InlineData("1234567.89", 123_456_789)]
    [InlineData("0", 0)]
    public void A_price_is_read_exactly(string text, long minorUnits) =>
        Assert.Equal(Dzd(minorUnits), WireFigures.Money(text, "DZD"));

    [Theory]
    [InlineData("120.505")]
    [InlineData("1,50")]
    [InlineData("12e2")]
    [InlineData("")]
    [InlineData("abc")]
    public void A_price_that_would_need_rounding_or_guessing_is_refused(string text) =>
        Assert.Throws<FormatException>(() => WireFigures.Money(text, "DZD"));

    [Theory]
    [InlineData("24", 24_000)]
    [InlineData("-3", -3_000)]
    [InlineData("1.5", 1_500)]
    [InlineData("0.001", 1)]
    public void A_stock_level_is_read_exactly_and_keeps_its_sign(string text, long thousandths) =>
        Assert.Equal(Quantity.FromThousandths(thousandths, "pc"), WireFigures.Quantity(text, "pc"));

    [Fact]
    public void A_price_is_read_the_same_whatever_the_machines_culture()
    {
        // Under a French culture "120.50" would not parse, or "1.500" would read
        // as fifteen hundred. The wire is invariant; so is its reading.
        var comma = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        comma.NumberFormat.NumberGroupSeparator = ".";

        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = comma;
            Assert.Equal(Dzd(12_050), WireFigures.Money("120.50", "DZD"));
            Assert.Equal(Quantity.FromThousandths(1_500, "pc"), WireFigures.Quantity("1.500", "pc"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void A_stock_level_finer_than_a_thousandth_is_refused() =>
        Assert.Throws<FormatException>(() => WireFigures.Quantity("0.0005", "pc"));

    // ---------------------------------------------------------------- lines

    [Fact]
    public void A_scan_adds_one_unit_at_the_unit_price()
    {
        var cart = new Cart();

        var line = cart.Add(Product(), "6130000000017");

        Assert.Equal(1, line.Count);
        Assert.Equal(Quantity.FromThousandths(1_000, "pc"), line.Quantity);
        Assert.Equal(Dzd(12_050), line.LineTotal);
    }

    [Fact]
    public void Scanning_the_same_variant_again_adds_a_unit_not_a_line()
    {
        var cart = new Cart();

        cart.Add(Product(), "6130000000017");
        cart.Add(Product(), "6130000000017");
        cart.Add(Product(), "6130000000017");

        var line = Assert.Single(cart.Lines);
        Assert.Equal(3, line.Count);
        Assert.Equal(Dzd(36_150), line.LineTotal);
    }

    [Fact]
    public void Lines_keep_the_order_they_were_first_scanned_in()
    {
        var cart = new Cart();

        cart.Add(Product("a"), "6130000000017");
        cart.Add(Product("b"), "6130000000017");
        cart.Add(Product("a"), "6130000000017");

        Assert.Equal(["a", "b"], cart.Lines.Select(line => line.VariantId));
    }

    [Fact]
    public void The_total_is_the_sum_of_the_lines()
    {
        var cart = new Cart();

        cart.Add(Product("a", price: "120.50"), "6130000000017");
        cart.Add(Product("a", price: "120.50"), "6130000000017");
        cart.Add(Product("b", price: "35.00"), "6130000000017");

        Assert.Equal(Dzd(27_600), cart.Total);
    }

    [Fact]
    public void An_empty_cart_has_no_total_rather_than_a_zero_without_a_currency() =>
        Assert.Null(new Cart().Total);

    [Fact]
    public void Removing_a_line_takes_it_out_of_the_total()
    {
        var cart = new Cart();
        cart.Add(Product("a", price: "120.50"), "6130000000017");
        cart.Add(Product("b", price: "35.00"), "6130000000017");

        Assert.True(cart.Remove("a"));

        Assert.Equal(Dzd(3_500), cart.Total);
        Assert.False(cart.Remove("a"));
    }

    [Fact]
    public void A_product_in_another_currency_is_refused_rather_than_summed()
    {
        var cart = new Cart();
        cart.Add(Product("a"), "6130000000017");

        Assert.Throws<InvalidOperationException>(() => cart.Add(Product("b", currency: "EUR"), "6130000000017"));
        Assert.Single(cart.Lines);
    }

    [Fact]
    public void A_later_scan_brings_the_fresher_stock_level()
    {
        var cart = new Cart();
        cart.Add(Product(stock: "10"), "6130000000017");

        var line = cart.Add(Product(stock: "4"), "6130000000017");

        Assert.Equal(Quantity.FromThousandths(4_000, "pc"), line.StockOnHand);
    }

    // ------------------------------------------- the promotional label (D-076)

    [Fact]
    public void A_line_says_when_its_price_is_promotional()
    {
        var cart = new Cart();

        var line = cart.Add(Product(price: "90.00", promotional: true), "6130000000017");

        Assert.True(line.IsPromotionalPrice);
        Assert.Equal(Dzd(9_000), line.LineTotal);
    }

    [Fact]
    public void A_promotion_that_ended_between_two_scans_unlabels_the_line()
    {
        // The label is taken from the latest answer, like the price and the level. Keeping
        // the first scan's label would print PROMOTIONAL PRICE beside the retail price.
        var cart = new Cart();
        cart.Add(Product(price: "90.00", promotional: true), "6130000000017");

        var line = cart.Add(Product(price: "120.50", promotional: false), "6130000000017");

        Assert.False(line.IsPromotionalPrice);
    }

    // ------------------------------------------------------ the stock notice

    [Theory]
    [InlineData("0", 1, true)]
    [InlineData("-2", 1, true)]
    [InlineData("2", 2, false)]
    [InlineData("2", 3, true)]
    [InlineData("10", 1, false)]
    public void The_line_says_when_it_holds_more_than_the_store_records(string stock, int scans, bool exceeds)
    {
        // Hakim, 18/09: stock at or below zero is a notice, never a refusal, and
        // so is a cart holding more than the shelf records.
        var cart = new Cart();
        CartLine line = null!;
        for (var i = 0; i < scans; i++)
        {
            line = cart.Add(Product(stock: stock), "6130000000017");
        }

        Assert.Equal(exceeds, line.ExceedsStockOnHand);
    }
}
