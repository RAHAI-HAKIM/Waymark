using System.Diagnostics;
using Waymark.Contracts.Pos;
using Waymark.Domain.Catalogue;
using DomainProduct = Waymark.Domain.Catalogue.ProductForSale;
using DomainReason = Waymark.Domain.Catalogue.NotSellableReason;
using DomainTvaSource = Waymark.Domain.Catalogue.TvaRateSource;
using WireProduct = Waymark.Contracts.Pos.ProductForSale;
using WireReason = Waymark.Contracts.Pos.NotSellableReason;
using WireTvaSource = Waymark.Contracts.Pos.TvaRateSource;

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
        TvaSource(product.TvaRateSource),
        WireText.Figure(product.PriceTtc),
        product.PriceTtc.Currency.Code,
        product.IsPromotionalPrice,
        WireText.Figure(product.StockOnHand));

    private static string TvaSource(DomainTvaSource source) => source switch
    {
        DomainTvaSource.FromCategory => WireTvaSource.FromCategory,
        DomainTvaSource.StandardFallback => WireTvaSource.StandardFallback,
        _ => throw new UnreachableException($"A TVA rate source this mapping does not know: {source}."),
    };

    private static string Reason(DomainReason reason) => reason switch
    {
        DomainReason.NoCurrentPrice => WireReason.NoCurrentPrice,
        DomainReason.PriceNotTaxInclusive => WireReason.PriceNotTaxInclusive,
        DomainReason.Archived => WireReason.Archived,
        DomainReason.Weighted => WireReason.Weighted,
        _ => throw new UnreachableException($"A refusal this mapping does not know: {reason}."),
    };
}
