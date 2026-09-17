using Waymark.Domain.Values;
using Waymark.Generator.Catalogues;

namespace Waymark.Generator.Simulation;

/// <summary>
/// The ids and facts the simulation needs about the store it created: who works there, what
/// it sells and at what price on which day. Built once by <see cref="ReferenceData"/>; the
/// day loop reads it and never queries the database for reference data.
/// </summary>
internal sealed class GeneratedStore
{
    public required string StoreId { get; init; }

    /// <summary>The store's short code, which prefixes its invoice numbers.</summary>
    public required string StoreCode { get; init; }

    public required Rounding RoundingPolicy { get; init; }

    public required Currency Currency { get; init; }

    public required IReadOnlyList<string> TerminalIds { get; init; }

    /// <summary>Staff ids in store.json order.</summary>
    public required IReadOnlyList<GeneratedStaff> Staff { get; init; }

    /// <summary>The staff member recorded as <c>stores.manager_id</c>; receives deliveries and posts adjustments.</summary>
    public required GeneratedStaff Manager { get; init; }

    public required IReadOnlyDictionary<string, string> SupplierIds { get; init; }

    /// <summary>Suppliers in suppliers.csv order; the index is the supplier's coordinate in every supply stream.</summary>
    public required IReadOnlyList<GeneratedSupplier> Suppliers { get; init; }

    /// <summary>Subcategories in categories.csv order, with their category ids: what a cycle count is scoped to.</summary>
    public required IReadOnlyList<(string Name, string CategoryId)> Subcategories { get; init; }

    /// <summary>store.json's reason codes by code, for whether one needs a manager or a note.</summary>
    public required IReadOnlyDictionary<string, ReasonCodeDefinition> ReasonCodes { get; init; }

    public required IReadOnlyDictionary<string, string> ProductIds { get; init; }

    /// <summary>Variants in catalogue order; the index is the variant's coordinate in every random stream.</summary>
    public required IReadOnlyList<GeneratedVariant> Variants { get; init; }

    public required IReadOnlyList<GeneratedCustomer> Customers { get; init; }

    /// <summary>Notice codes by type, for consent written later in the run.</summary>
    public required IReadOnlyDictionary<Domain.Enums.NoticeType, string> Notices { get; init; }
}

internal sealed record GeneratedStaff(string StaffId, string Name, string Role);

internal sealed record GeneratedSupplier(int Index, string Code, string SupplierId, IReadOnlySet<DayOfWeek> DeliveryDays, int StatedLeadTimeDays);

/// <param name="CreditLimit">The tab's limit; null means no tab (F-16).</param>
/// <param name="NeverSettles">A tab the customer will never pay back.</param>
internal sealed record GeneratedCustomer(string CustomerId, int Number, bool MarketingConsent, bool ObjectionFlag, Money? CreditLimit = null, bool NeverSettles = false);

/// <summary>A variant's quantity from one batch, as it left the shelf into a basket.</summary>
internal sealed record ServedLine(GeneratedVariant Variant, StockLot Lot, Quantity Quantity);

/// <summary>A variant, the ids it hangs off, its VAT class and its price history.</summary>
internal sealed class GeneratedVariant
{
    public required int Index { get; init; }

    public required string VariantId { get; init; }

    public required string ProductId { get; init; }

    public required string SupplierId { get; init; }

    public required CatalogueVariant Catalogue { get; init; }

    public required BasisPoints Vat { get; init; }

    public required UnitPrecision SellingUnit { get; init; }

    /// <summary>Retail and purchase prices in effect from each date, earliest first.</summary>
    public required IReadOnlyList<PricePoint> Prices { get; init; }

    /// <summary>The prices in effect on <paramref name="date"/>.</summary>
    public PricePoint PriceOn(DateOnly date)
    {
        var current = Prices[0];
        foreach (var point in Prices)
        {
            if (point.From > date)
            {
                break;
            }

            current = point;
        }

        return current;
    }
}

/// <summary>A retail price (TTC, per selling unit) and a purchase price (HT, per selling unit) from a date.</summary>
internal sealed record PricePoint(DateOnly From, Money Retail, Money Purchase);
