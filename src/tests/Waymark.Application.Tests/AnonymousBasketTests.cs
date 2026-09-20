using System.Text.Json;
using Waymark.Application.Sync;
using Waymark.Domain.Values;

namespace Waymark.Application.Tests;

/// <summary>
/// The basket a sale sends out (D-043, D-064). What it must not carry is the point of it,
/// and the coarsening happens here, at emit: afterwards nobody knows which rows to fix.
/// </summary>
public sealed class AnonymousBasketTests
{
    private static readonly DateOnly Friday = new(2026, 9, 18);

    private static SoldLine Line(string productId, long units, long minorUnits) => new(
        productId,
        Quantity.FromThousandths(units * Quantity.Scale, "pc"),
        Money.FromMinorUnits(minorUnits, Currency.Dzd));

    private static Contracts.Sync.AnonymousBasketRecord Basket(params SoldLine[] lines) =>
        AnonymousBasket.From("basket-1", "store-1", Friday, 14, lines, AnonymousBasket.Cash, hasDiscount: false);

    [Fact]
    public void A_basket_carries_the_day_its_hour_and_its_weekday_never_a_time()
    {
        var basket = Basket(Line("milk", 2, 28_600));

        Assert.Equal(Friday, basket.Date);
        Assert.Equal(14, basket.HourBucket);
        Assert.Equal((int)DayOfWeek.Friday, basket.DayOfWeek);
        Assert.Equal(AnonymousBasket.Cash, basket.PaymentClass);
        Assert.False(basket.HasDiscount);
    }

    [Fact]
    public void Lines_are_at_product_grain_so_two_batches_of_one_product_are_one_line()
    {
        // The sale took 1 from an old batch and 2 from a new one. The batches are the
        // store's business; the cloud sees a product and a quantity.
        var basket = Basket(Line("milk", 1, 14_300), Line("milk", 2, 28_600));

        var line = Assert.Single(basket.Lines);
        Assert.Equal("milk", line.ProductId);
        Assert.Equal("3", line.Quantity);
        Assert.Equal("429.00", line.LineValue);
    }

    [Fact]
    public void Figures_cross_as_exact_text()
    {
        var basket = Basket(Line("milk", 2, 28_600), Line("bread", 1, 12_050));

        Assert.Equal(["1", "2"], basket.Lines.Select(line => line.Quantity).Order(StringComparer.Ordinal));
        Assert.Equal(["120.50", "286.00"], basket.Lines.Select(line => line.LineValue).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_json_names_a_product_and_nothing_of_the_till()
    {
        // Nothing here may join back to the sale: no transaction, invoice, staff, terminal,
        // session or variant, and no customer column exists at all (D-043).
        var json = JsonSerializer.Serialize(Basket(Line("milk", 2, 28_600)));

        Assert.Contains("\"product_id\":\"milk\"", json, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "transaction", "invoice", "staff", "terminal", "session", "variant", "customer" })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_basket_with_no_line_is_refused() =>
        Assert.Throws<ArgumentException>(() => Basket());

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void An_hour_outside_the_day_is_refused(int hour) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnonymousBasket.From("b", "s", Friday, hour, [Line("milk", 1, 100)], AnonymousBasket.Cash, false));
}
