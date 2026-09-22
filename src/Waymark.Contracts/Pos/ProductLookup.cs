using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>
/// StoreServer's answer when the till asks what a barcode is. Hop 1 of the
/// walking skeleton.
///
/// <para>
/// <b>Every outcome is an answer, not an error.</b> An unknown barcode, or a
/// product the till may not sell, comes back as a 200 with its
/// <see cref="Outcome"/>. An HTTP error means only that the server failed, so the
/// till never confuses "no such product" with "the server is down": the first
/// is shown to the cashier, the second is an outage.
/// </para>
/// </summary>
/// <param name="Outcome">One of <see cref="ProductLookupOutcome"/>.</param>
/// <param name="Barcode">The code asked about, echoed so the till can show it.</param>
/// <param name="Product">Set when <see cref="Outcome"/> is <c>found</c>, null otherwise.</param>
/// <param name="Reason">
/// Set when <see cref="Outcome"/> is <c>not_sellable</c>: one of
/// <see cref="NotSellableReason"/>. Null otherwise.
/// </param>
public sealed record ProductLookup(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("barcode")] string Barcode,
    [property: JsonPropertyName("product")] ProductForSale? Product,
    [property: JsonPropertyName("reason")] string? Reason);

/// <summary>
/// A variant as the till may sell it, now, in this store.
///
/// <para>
/// Figures cross as exact decimal text (D-044, D-049): a price never passes
/// through a binary floating point on the wire. The price shown to the cashier
/// is a <b>preview</b>: the receipt's figures, the TVA split and the cash
/// rounding, are computed by the server when the sale completes.
/// </para>
/// </summary>
/// <param name="VariantId">What the sale will reference.</param>
/// <param name="ProductId">The product the variant belongs to.</param>
/// <param name="ProductName">The product's name, for the cart line.</param>
/// <param name="VariantName">The variant's name, for the cart line.</param>
/// <param name="SellingUnitCode">The unit a quantity is counted in.</param>
/// <param name="SellingUnitDecimalPlaces">
/// How many decimals a quantity in that unit may have: 0 for pieces.
/// </param>
/// <param name="TvaRateBasisPoints">The TVA rate, in hundredths of a percent: 1900 is 19%.</param>
/// <param name="TvaRateSource">
/// One of <see cref="Pos.TvaRateSource"/>: whether the product's categories gave that rate,
/// or D-075's catch-all did because they were silent or disagreed. The till shows nothing for
/// it — the cashier cannot fix a catalogue — but the fact travels so Admin can list every
/// product selling on the fallback.
/// </param>
/// <param name="PriceTtc">The price in force today, tax included, as exact decimal text.</param>
/// <param name="Currency">The price's currency code.</param>
/// <param name="IsPromotionalPrice">
/// True when <paramref name="PriceTtc"/> came from a <c>promotional</c> row rather than a
/// <c>retail</c> one (D-076). It is the price, not a discount: nothing is taken off it, and
/// <c>transaction_items.discount_amount</c> stays zero.
/// </param>
/// <param name="StockOnHand">
/// This store's level, in the selling unit, as exact decimal text. Zero or
/// negative is a warning for the cashier, never a refusal: a level going
/// negative is not a bug (CLAUDE.md §3.8).
/// </param>
public sealed record ProductForSale(
    [property: JsonPropertyName("variant_id")] string VariantId,
    [property: JsonPropertyName("product_id")] string ProductId,
    [property: JsonPropertyName("product_name")] string ProductName,
    [property: JsonPropertyName("variant_name")] string VariantName,
    [property: JsonPropertyName("selling_unit_code")] string SellingUnitCode,
    [property: JsonPropertyName("selling_unit_decimal_places")] int SellingUnitDecimalPlaces,
    [property: JsonPropertyName("tva_rate_basis_points")] int TvaRateBasisPoints,
    [property: JsonPropertyName("tva_rate_source")] string TvaRateSource,
    [property: JsonPropertyName("price_ttc")] string PriceTtc,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("is_promotional_price")] bool IsPromotionalPrice,
    [property: JsonPropertyName("stock_on_hand")] string StockOnHand);

/// <summary>The values of <see cref="ProductForSale.TvaRateSource"/> (D-075).</summary>
public static class TvaRateSource
{
    /// <summary>The product's categories agreed on a rate, and every one of them stated it.</summary>
    public const string FromCategory = "from_category";

    /// <summary>
    /// The catch-all: the product is in no category, or a category states no rate, or its
    /// categories disagree. The rate is the standard 19% and the catalogue needs fixing.
    /// </summary>
    public const string StandardFallback = "standard_fallback";
}

/// <summary>The values of <see cref="ProductLookup.Outcome"/>.</summary>
public static class ProductLookupOutcome
{
    /// <summary>The till may sell it: <see cref="ProductLookup.Product"/> is set.</summary>
    public const string Found = "found";

    /// <summary>No variant carries this barcode.</summary>
    public const string UnknownBarcode = "unknown_barcode";

    /// <summary>The variant exists, but the till may not sell it: see <see cref="ProductLookup.Reason"/>.</summary>
    public const string NotSellable = "not_sellable";
}

/// <summary>
/// The values of <see cref="ProductLookup.Reason"/>: Hakim's spec for hop 1 (D-066).
///
/// <para>
/// <c>no_tax_rate</c> and <c>conflicting_tax_rates</c> were here while O-24 was open. D-075
/// answers that question with a rate instead of a refusal, so the till can no longer receive
/// either, and a reason it cannot receive is a lie in the contract. The catalogue's silence
/// now crosses as <see cref="ProductForSale.TvaRateSource"/> on a product that sells.
/// </para>
/// </summary>
public static class NotSellableReason
{
    /// <summary>
    /// No retail price is in force for this store today. Never sold at zero:
    /// absence is not zero (D-037).
    /// </summary>
    public const string NoCurrentPrice = "no_current_price";

    /// <summary>The price in force is HT; converting it would be a new rounding site.</summary>
    public const string PriceNotTaxInclusive = "price_not_tax_inclusive";

    /// <summary>The variant is archived. A discontinued one still sells.</summary>
    public const string Archived = "archived";

    /// <summary>Sold by weight: scales and weight-embedded codes are Phase 1.</summary>
    public const string Weighted = "weighted";
}
