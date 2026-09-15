using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Generator.Catalogues;

/// <summary>
/// Everything a catalogue supplies (D-046 §4): what the store sells, from whom, at what
/// price, and the store itself. The simulation depends on this and on nothing else about
/// the activity, so a second catalogue — a hardware shop, a boutique — generates a valid
/// year with no code change.
/// </summary>
internal sealed record Catalogue(
    string Directory,
    StoreProfile Store,
    IReadOnlyList<CatalogueUnit> Units,
    IReadOnlyList<CatalogueSubcategory> Subcategories,
    IReadOnlyList<CatalogueSupplier> Suppliers,
    IReadOnlyList<CatalogueVariant> Variants,
    IReadOnlyList<CataloguePriceChange> PriceChanges)
{
    /// <summary>The top-level categories, in first-appearance order.</summary>
    public IReadOnlyList<string> Categories { get; } =
        [.. Subcategories.Select(subcategory => subcategory.Category).Distinct(StringComparer.Ordinal)];

    /// <summary>The products, in first-appearance order, each with its variants in file order.</summary>
    public IReadOnlyList<CatalogueProduct> Products { get; } =
        [.. Variants
            .GroupBy(variant => variant.ProductName, StringComparer.Ordinal)
            .Select(group => new CatalogueProduct(group.Key, group.First().Subcategory, group.First().SupplierCode, [.. group]))];
}

/// <summary>A unit of measure (<c>units.csv</c>).</summary>
internal sealed record CatalogueUnit(
    string Where,
    string Code,
    string NameFr,
    string NameAr,
    Dimension Dimension,
    string? BaseUnitCode,
    long FactorToBase,
    int DecimalPlaces);

/// <summary>A subcategory and the VAT class it carries (<c>categories.csv</c>).</summary>
internal sealed record CatalogueSubcategory(
    string Where,
    string Category,
    string Subcategory,
    BasisPoints Vat,
    bool Sensitive,
    string? SensitiveReason);

/// <summary>A supplier and when it delivers (<c>suppliers.csv</c>).</summary>
internal sealed record CatalogueSupplier(
    string Where,
    string Code,
    string CompanyName,
    IReadOnlySet<DayOfWeek> DeliveryDays,
    int StatedLeadTimeDays,
    int NetDays);

/// <summary>
/// One sellable variant (<c>catalogue.csv</c>), with prices as at commissioning and the
/// behavioural attributes demand needs (D-046 §7).
/// </summary>
internal sealed record CatalogueVariant(
    string Where,
    string Sku,
    string Barcode,
    string Category,
    string Subcategory,
    string ProductName,
    string VariantName,
    string NetContent,
    string SellingUnit,
    Money RetailPrice,
    Money PurchasePrice,
    string SupplierCode,
    string PurchaseUnit,
    int UnitsPerPurchaseUnit,
    int? ShelfLifeDays,
    double BaseDailyRate,
    string SeasonalityProfile,
    int SubstitutabilityTier);

/// <summary>A dated change of a variant's retail and purchase price (<c>price_changes.csv</c>).</summary>
internal sealed record CataloguePriceChange(
    string Where,
    string Sku,
    DateOnly EffectiveDate,
    Money RetailPrice,
    Money PurchasePrice);

/// <summary>A product: variants sharing a name, a subcategory and a supplier.</summary>
internal sealed record CatalogueProduct(
    string Name,
    string Subcategory,
    string SupplierCode,
    IReadOnlyList<CatalogueVariant> Variants);

/// <summary>Where a catalogue comes from. The simulation depends on this and nothing else (D-046 §5).</summary>
internal interface ICatalogueSource
{
    Catalogue Load();
}
