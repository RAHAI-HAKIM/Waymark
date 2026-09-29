using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
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
    /// <summary>
    /// The variant whose barcode is <paramref name="code"/>, or failing that whose PLU is (D-088), or
    /// failing both the product a scale label names in the store's format (D-090). The barcode is
    /// tried first because a scan is one, and because every in-store barcode starts like a label.
    /// </summary>
    /// <param name="typedWeightThousandths">
    /// A weight typed at the till, in thousandths of the selling unit, for a product sold by
    /// weight; null otherwise. A label carries its own and takes none.
    /// </param>
    Task<ProductLookupResult> FindForSaleAsync(
        string code, long? typedWeightThousandths = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// The products whose name answers what the cashier typed, each with what a scan of it would say
/// (session B1, D-088): the search is the stock lookup, and an unsellable product is listed with
/// its reason rather than hidden.
/// </summary>
public interface IProductSearch
{
    /// <summary>Best first, at most <see cref="NameSearch.MaximumResults"/>, ranked by <see cref="NameSearch"/>.</summary>
    Task<IReadOnlyList<ProductSearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>A product the search found.</summary>
/// <param name="Code">
/// What the till sends to sell it: the barcode, or the PLU when it has none (D-070 sends codes,
/// never variant ids). Null when it has neither, and <paramref name="Result"/> then says
/// <see cref="NotSellableReason.NoCode"/>.
/// </param>
/// <param name="Result">What a scan of it would answer: found, or not sellable and why.</param>
public sealed record ProductSearchHit(string VariantId, string ProductName, string VariantName, string? Code, ProductLookupResult Result);

/// <summary>One of three answers, and never an exception for a product that cannot be sold.</summary>
public abstract record ProductLookupResult
{
    private ProductLookupResult()
    {
    }

    /// <summary>The till may sell it.</summary>
    /// <param name="Weighed">
    /// For a product sold by weight whose weight is known (typed, or read from a label): how much,
    /// from where, and what the line comes to. Null for a product sold by count, and for a weighed
    /// one whose weight the till has still to ask for.
    /// </param>
    public sealed record Found(ProductForSale Product, WeighedQuantity? Weighed = null) : ProductLookupResult;

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

    /// <summary>A weight was typed for a product sold by count (B3).</summary>
    NotSoldByWeight,

    /// <summary>A typed weight that is not above zero, or finer than the unit is sold to (B3).</summary>
    WeightInvalid,

    /// <summary>
    /// A scale label names a product that is not set up for labels: sold by count, or its
    /// <c>barcode_type</c> is <c>standard</c>. The catalogue needs fixing, not the label (B3).
    /// </summary>
    LabelNotSetUp,

    /// <summary>
    /// A scale label whose value cannot be sold: a weight of zero or off the unit's step, or a price
    /// worth less than half a step of the product (B3, D-090).
    /// </summary>
    LabelValueInvalid,

    /// <summary>
    /// Neither a barcode nor a PLU (B1): a sale sends codes (D-070), so a product with none cannot be
    /// sold from a search. Only a search answers this; a lookup is by a code, so it has one.
    /// </summary>
    NoCode,
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
/// <param name="IsWeighted">Sold by weight (B3): the till asks for a weight unless a label gave one.</param>
/// <param name="UnitCost">
/// What a unit of the oldest batch in stock cost the shop, or null with none: the till warns when a
/// price is overridden below it (B5, D-092). Never shown as a figure.
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
    Quantity StockOnHand,
    bool IsWeighted = false,
    Money? UnitCost = null);

/// <summary>A weighed product's quantity, where it came from, and what the line comes to (B3, D-090).</summary>
/// <param name="Quantity">In the selling unit, on a step the unit allows.</param>
/// <param name="Source">Typed, a weight label or a price label: which figure is exact.</param>
/// <param name="Amounts">The line as one, before it is split over batches; the sale re-derives it per batch.</param>
public sealed record WeighedQuantity(Quantity Quantity, QuantitySource Source, LineAmounts Amounts);
