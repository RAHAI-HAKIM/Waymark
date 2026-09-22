using Waymark.Domain.Values;

namespace Waymark.Domain.Catalogue;

/// <summary>
/// What the till may sell for a barcode, in the current store, now (D-066).
///
/// <para>
/// A read: it stages nothing and needs no unit of work. Persistence implements
/// it, and StoreServer maps the result onto the wire.
/// </para>
/// </summary>
public interface IProductLookup
{
    Task<ProductLookupResult> FindForSaleAsync(string barcode, CancellationToken cancellationToken = default);
}

/// <summary>One of three answers, and never an exception for a product that cannot be sold.</summary>
public abstract record ProductLookupResult
{
    private ProductLookupResult()
    {
    }

    /// <summary>The till may sell it.</summary>
    public sealed record Found(ProductForSale Product) : ProductLookupResult;

    /// <summary>No variant carries this barcode.</summary>
    public sealed record UnknownBarcode : ProductLookupResult;

    /// <summary>The variant exists and the till may not sell it, for this reason.</summary>
    public sealed record NotSellable(NotSellableReason Reason) : ProductLookupResult;
}

/// <summary>
/// Why a known variant may not be sold. Hakim's spec for hop 1 (D-066).
///
/// <para>
/// <b>There is no TVA reason here any more.</b> <c>NoTaxRate</c> and
/// <c>ConflictingTaxRates</c> were hop 1's two refusals while O-24 was open. D-075 answers it
/// with a rate rather than a refusal, so neither state can occur, and a refusal the till can
/// never receive is a lie in the contract. What the catalogue failed to say is carried
/// instead by <see cref="ProductForSale.TvaRateSource"/>, on a product that sells.
/// </para>
/// </summary>
public enum NotSellableReason
{
    /// <summary>No retail price in force for this store today. Never sold at zero (D-037).</summary>
    NoCurrentPrice,

    /// <summary>The price in force is HT. Converting it would be a new rounding site.</summary>
    PriceNotTaxInclusive,

    /// <summary>The variant is archived. A discontinued one still sells its remaining stock.</summary>
    Archived,

    /// <summary>Sold by weight: scales and weight-embedded codes are Phase 1.</summary>
    Weighted,
}

/// <summary>A variant as the till may sell it, now, in this store.</summary>
/// <param name="VariantId">What the sale will reference.</param>
/// <param name="ProductId">The product the variant belongs to.</param>
/// <param name="ProductName">For the cart line.</param>
/// <param name="VariantName">For the cart line.</param>
/// <param name="Unit">The selling unit and how many decimals a quantity in it may have.</param>
/// <param name="TvaRate">The rate TVA is extracted at, from the TTC price (D-033).</param>
/// <param name="TvaRateSource">
/// Whether the catalogue gave that rate or D-075's catch-all did. Nothing at the till acts on
/// it; it travels so Admin can list every product selling on the fallback (block E).
/// </param>
/// <param name="PriceTtc">The price in force today, tax included: promotional if one is in
/// force, otherwise retail (D-076).</param>
/// <param name="IsPromotionalPrice">
/// True when <paramref name="PriceTtc"/> came from a <c>promotional</c> row rather than a
/// <c>retail</c> one. The cashier is told, because customers ask.
/// </param>
/// <param name="StockOnHand">
/// This store's level across its batches. Zero or negative is a warning at the
/// till, never a refusal (CLAUDE.md §3.8).
/// </param>
public sealed record ProductForSale(
    string VariantId,
    string ProductId,
    string ProductName,
    string VariantName,
    UnitPrecision Unit,
    BasisPoints TvaRate,
    TvaRateSource TvaRateSource,
    Money PriceTtc,
    bool IsPromotionalPrice,
    Quantity StockOnHand);
