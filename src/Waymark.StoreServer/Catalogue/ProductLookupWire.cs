using System.Diagnostics;
using System.Globalization;
using Waymark.Contracts.Pos;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Values;
using DomainProduct = Waymark.Domain.Catalogue.ProductForSale;
using DomainReason = Waymark.Domain.Catalogue.NotSellableReason;
using WireProduct = Waymark.Contracts.Pos.ProductForSale;
using WireReason = Waymark.Contracts.Pos.NotSellableReason;

namespace Waymark.StoreServer.Catalogue;

/// <summary>
/// The lookup's answer as the till reads it (D-066). A pure function, so the
/// whole mapping is tested without starting a server.
/// </summary>
public static class ProductLookupWire
{
    public static ProductLookup ToWire(string barcode, ProductLookupResult result) => result switch
    {
        ProductLookupResult.Found found =>
            new ProductLookup(ProductLookupOutcome.Found, barcode, Product(found.Product), Reason: null),
        ProductLookupResult.UnknownBarcode =>
            new ProductLookup(ProductLookupOutcome.UnknownBarcode, barcode, Product: null, Reason: null),
        ProductLookupResult.NotSellable refused =>
            new ProductLookup(ProductLookupOutcome.NotSellable, barcode, Product: null, Reason(refused.Reason)),
        _ => throw new UnreachableException($"A lookup result this mapping does not know: {result}."),
    };

    private static WireProduct Product(DomainProduct product) => new(
        product.VariantId,
        product.ProductId,
        product.ProductName,
        product.VariantName,
        product.Unit.Code,
        product.Unit.DecimalPlaces,
        product.TvaRate.Value,
        Figure(product.PriceTtc),
        product.PriceTtc.Currency.Code,
        Figure(product.StockOnHand));

    private static string Reason(DomainReason reason) => reason switch
    {
        DomainReason.NoCurrentPrice => WireReason.NoCurrentPrice,
        DomainReason.PriceNotTaxInclusive => WireReason.PriceNotTaxInclusive,
        DomainReason.Archived => WireReason.Archived,
        DomainReason.NoTaxRate => WireReason.NoTaxRate,
        DomainReason.ConflictingTaxRates => WireReason.ConflictingTaxRates,
        DomainReason.Weighted => WireReason.Weighted,
        _ => throw new UnreachableException($"A refusal this mapping does not know: {reason}."),
    };

    // Exact decimal text, never through a double (D-044). decimal division by a
    // power of ten is exact, and the invariant culture keeps '.' on every machine.
    private static string Figure(Money money) =>
        (money.MinorUnits / (decimal)Currency.StorageScale).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Figure(Quantity quantity) =>
        (quantity.Thousandths / (decimal)Quantity.Scale).ToString("0.###", CultureInfo.InvariantCulture);
}
