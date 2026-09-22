using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Waymark.Contracts.Pos;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Values;
using Waymark.StoreServer.Catalogue;
using DomainProduct = Waymark.Domain.Catalogue.ProductForSale;
using DomainReason = Waymark.Domain.Catalogue.NotSellableReason;
using DomainTvaSource = Waymark.Domain.Catalogue.TvaRateSource;
using WireReason = Waymark.Contracts.Pos.NotSellableReason;
using WireTvaSource = Waymark.Contracts.Pos.TvaRateSource;

namespace Waymark.Integration.Tests;

/// <summary>
/// The lookup's answer as the till reads it (D-066). The mapping is a pure
/// function; what can go wrong silently is a figure: a price through a double,
/// or a comma for a decimal point on a French-configured machine.
/// </summary>
[SupportedOSPlatform("windows")] // StoreServer is Windows-only (D-017), and so is its code.
public sealed class ProductLookupWireTests
{
    private static DomainProduct Milk(
        long priceMinor = 12_050,
        long stockThousandths = 24_000,
        int decimals = 0,
        DomainTvaSource tvaSource = DomainTvaSource.FromCategory,
        bool promotional = false) => new(
        "variant-1",
        "product-1",
        "Lait UHT Candia",
        "Brique 1L",
        UnitPrecision.For("pc", decimals),
        BasisPoints.StandardVat,
        tvaSource,
        Money.FromMinorUnits(priceMinor, Currency.Dzd),
        promotional,
        Quantity.FromThousandths(stockThousandths, "pc"));

    [Fact]
    public void A_found_product_carries_every_field_the_till_shows()
    {
        var wire = ProductLookupWire.ToWire("6130000000017", new ProductLookupResult.Found(Milk()));

        Assert.Equal(ProductLookupOutcome.Found, wire.Outcome);
        Assert.Equal("6130000000017", wire.Barcode);
        Assert.Null(wire.Reason);

        var product = Assert.IsType<Contracts.Pos.ProductForSale>(wire.Product);
        Assert.Equal("variant-1", product.VariantId);
        Assert.Equal("product-1", product.ProductId);
        Assert.Equal("Lait UHT Candia", product.ProductName);
        Assert.Equal("Brique 1L", product.VariantName);
        Assert.Equal("pc", product.SellingUnitCode);
        Assert.Equal(0, product.SellingUnitDecimalPlaces);
        Assert.Equal(1_900, product.TvaRateBasisPoints);
        Assert.Equal(WireTvaSource.FromCategory, product.TvaRateSource);
        Assert.Equal("120.50", product.PriceTtc);
        Assert.Equal("DZD", product.Currency);
        Assert.False(product.IsPromotionalPrice);
        Assert.Equal("24", product.StockOnHand);
    }

    [Theory]
    [InlineData(12_000, "120.00")]
    [InlineData(5, "0.05")]
    [InlineData(0, "0.00")]
    [InlineData(123_456_789, "1234567.89")]
    public void A_price_crosses_as_exact_text_with_two_places(long minorUnits, string expected) =>
        Assert.Equal(expected, ProductLookupWire.ToWire("x", new ProductLookupResult.Found(Milk(priceMinor: minorUnits))).Product!.PriceTtc);

    [Theory]
    [InlineData(0, "0")]
    [InlineData(-3_000, "-3")]
    [InlineData(1_500, "1.5")]
    [InlineData(250, "0.25")]
    public void Stock_crosses_as_exact_text_and_keeps_its_sign(long thousandths, string expected) =>
        Assert.Equal(expected, ProductLookupWire.ToWire("x", new ProductLookupResult.Found(Milk(stockThousandths: thousandths))).Product!.StockOnHand);

    [Fact]
    public void Figures_ignore_the_machines_culture()
    {
        // The build is culture-invariant, but a figure must not depend on that:
        // under a French culture 120.50 formats as "120,50", which the till would
        // misread. Invariant mode gives fr-FR invariant data, so the comma is set
        // by hand to make the culture really differ.
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = comma;
            Assert.Equal("120.50", ProductLookupWire.ToWire("x", new ProductLookupResult.Found(Milk())).Product!.PriceTtc);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void An_unknown_barcode_is_an_answer_with_no_product()
    {
        var wire = ProductLookupWire.ToWire("0000000000000", new ProductLookupResult.UnknownBarcode());

        Assert.Equal(ProductLookupOutcome.UnknownBarcode, wire.Outcome);
        Assert.Equal("0000000000000", wire.Barcode);
        Assert.Null(wire.Product);
        Assert.Null(wire.Reason);
    }

    public static TheoryData<DomainReason, string> Reasons() => new()
    {
        { DomainReason.NoCurrentPrice, WireReason.NoCurrentPrice },
        { DomainReason.PriceNotTaxInclusive, WireReason.PriceNotTaxInclusive },
        { DomainReason.Archived, WireReason.Archived },
        { DomainReason.Weighted, WireReason.Weighted },
    };

    [Theory]
    [MemberData(nameof(Reasons))]
    public void Each_refusal_crosses_with_its_own_reason(DomainReason reason, string expected)
    {
        var wire = ProductLookupWire.ToWire("x", new ProductLookupResult.NotSellable(reason));

        Assert.Equal(ProductLookupOutcome.NotSellable, wire.Outcome);
        Assert.Equal(expected, wire.Reason);
        Assert.Null(wire.Product);
    }

    [Fact]
    public void Every_domain_reason_has_a_wire_reason()
    {
        // A reason added to the Domain enum without a wire name would throw at
        // the till the first time a product hit it.
        Assert.Equal(Enum.GetValues<DomainReason>().Length, Reasons().Count);
    }

    [Fact]
    public void No_refusal_is_about_TVA_any_more()
    {
        // D-075 answers O-24 with a rate, not a refusal. If either of hop 1's two TVA
        // refusals comes back, something is deciding the rate in a query again.
        // (PriceNotTaxInclusive stays: it is about the price row, not the rate.)
        var names = Enum.GetNames<DomainReason>();

        Assert.DoesNotContain("NoTaxRate", names);
        Assert.DoesNotContain("ConflictingTaxRates", names);
    }

    public static TheoryData<DomainTvaSource, string> TvaSources() => new()
    {
        { DomainTvaSource.FromCategory, WireTvaSource.FromCategory },
        { DomainTvaSource.StandardFallback, WireTvaSource.StandardFallback },
    };

    [Theory]
    [MemberData(nameof(TvaSources))]
    public void Each_TVA_source_crosses_with_its_own_name(DomainTvaSource source, string expected)
    {
        var wire = ProductLookupWire.ToWire("x", new ProductLookupResult.Found(Milk(tvaSource: source)));

        Assert.Equal(expected, wire.Product!.TvaRateSource);
    }

    [Fact]
    public void Every_domain_TVA_source_has_a_wire_name()
    {
        Assert.Equal(Enum.GetValues<DomainTvaSource>().Length, TvaSources().Count);
    }

    [Fact]
    public void The_json_uses_the_contracts_names()
    {
        var json = JsonSerializer.Serialize(
            ProductLookupWire.ToWire("6130000000017", new ProductLookupResult.Found(Milk())));

        Assert.Contains("\"outcome\":\"found\"", json, StringComparison.Ordinal);
        Assert.Contains("\"price_ttc\":\"120.50\"", json, StringComparison.Ordinal);
        Assert.Contains("\"tva_rate_basis_points\":1900", json, StringComparison.Ordinal);
        Assert.Contains("\"tva_rate_source\":\"from_category\"", json, StringComparison.Ordinal);
        Assert.Contains("\"is_promotional_price\":false", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_promotional_price_says_so_in_the_json()
    {
        var json = JsonSerializer.Serialize(
            ProductLookupWire.ToWire("6130000000017", new ProductLookupResult.Found(
                Milk(tvaSource: DomainTvaSource.StandardFallback, promotional: true))));

        Assert.Contains("\"tva_rate_source\":\"standard_fallback\"", json, StringComparison.Ordinal);
        Assert.Contains("\"is_promotional_price\":true", json, StringComparison.Ordinal);
    }
}
