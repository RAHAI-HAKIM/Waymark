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

/// <summary>Why a known variant may not be sold. Hakim's spec for hop 1 (D-066).</summary>
public enum NotSellableReason
{
    /// <summary>No retail price in force for this store today. Never sold at zero (D-037).</summary>
    NoCurrentPrice,

    /// <summary>The price in force is HT. Converting it would be a new rounding site.</summary>
    PriceNotTaxInclusive,

    /// <summary>The variant is archived. A discontinued one still sells its remaining stock.</summary>
    Archived,

    /// <summary>None of the product's categories carries a TVA rate. Absence is not zero.</summary>
    NoTaxRate,

    /// <summary>The product's categories carry different TVA rates (O-24 decides the real rule).</summary>
    ConflictingTaxRates,

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
/// <param name="PriceTtc">The retail price in force today, tax included.</param>
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
    Money PriceTtc,
    Quantity StockOnHand);
