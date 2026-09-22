using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Enums;
using Waymark.Domain.Pricing;
using Waymark.Domain.Values;

namespace Waymark.Persistence.Catalogue;

/// <summary>
/// <see cref="IProductLookup"/> over the store database (D-066).
///
/// <para>
/// Store scoping is the context's global filter: a context built for store A
/// sees A's prices and inventories and nothing of B's, so this class never
/// filters by store itself (CLAUDE.md §3.3).
/// </para>
/// </summary>
public sealed class ProductLookup(WaymarkDbContext context, IStoreCalendar calendar) : IProductLookup
{
    /// <summary>
    /// The five steps of D-066, in order. Every early return is an answer the
    /// till shows, not an error: an exception here would reach the cashier as
    /// "server failed" for what is really "this product has no price".
    /// </summary>
    public async Task<ProductLookupResult> FindForSaleAsync(string barcode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);

        // 1. The variant. `variants` has no store_id (the catalogue is the
        //    tenant's), so no filter applies and every store sees the same one.
        //    The barcode is unique (IX_variants_barcode), so at most one row.
        var variant = await context.Variants
            .FirstOrDefaultAsync(v => v.Barcode == barcode, cancellationToken);

        if (variant is null)
        {
            return new ProductLookupResult.UnknownBarcode();
        }

        if (variant.Status == VariantStatus.Archived)
        {
            return new ProductLookupResult.NotSellable(NotSellableReason.Archived);
        }

        if (variant.IsWeighted)
        {
            return new ProductLookupResult.NotSellable(NotSellableReason.Weighted);
        }

        // 2. Its product and selling unit. Single, not FirstOrDefault: the
        //    foreign keys guarantee both exist, so a missing one is a broken
        //    database and should throw, not be read as "not sellable".
        var product = await context.Products
            .SingleAsync(p => p.ProductId == variant.ProductId, cancellationToken);

        var unit = await context.UnitsOfMeasure
            .SingleAsync(u => u.UnitCode == variant.SellingUnitCode, cancellationToken);

        // 3. The TVA rate. product_category links the product to its categories;
        //    the join follows that link and keeps each category's rate, nulls
        //    and all. A null is not dropped here the way the skeleton dropped
        //    it: under D-075 a category that states no rate is itself a
        //    catch-all case, so the rule has to see it (TvaRate.Resolve, case 3).
        //
        //    Distinct is safe and is only a size reduction: SQL keeps one null
        //    and one of each distinct value, so "is a null present" and "how
        //    many distinct rates" both survive it, and two categories that
        //    agree still arrive as agreement.
        //
        //    EF turns this whole chain into one SQL query:
        //      SELECT DISTINCT c.tax_rate FROM product_category pc
        //      JOIN categories c ON c.category_id = pc.category_id
        //      WHERE pc.product_id = @p
        //
        //    Deciding is Domain's job, not this query's (D-075): an empty list,
        //    a null, or two rates are three different facts about the catalogue
        //    and one function weighs them, where a test can argue with it.
        var categoryRates = await context.ProductCategory
            .Where(link => link.ProductId == variant.ProductId)
            .Join(
                context.Categories,
                link => link.CategoryId,
                category => category.CategoryId,
                (link, category) => category.TaxRate)
            .Distinct()
            .ToListAsync(cancellationToken);

        var tva = TvaRate.Resolve(
            [.. categoryRates.Select(rate => rate is null ? (BasisPoints?)null : new BasisPoints(checked((int)rate.Value)))]);

        // 4. The price in force today. `prices` IS store-scoped: the global
        //    filter adds `store_id = <current store>` to this query without
        //    being asked, which is why another store's price never appears and
        //    why nothing here mentions a store. That is the whole point of a
        //    filter over a `where` someone might forget (CLAUDE.md §3.3).
        //
        //    valid_from / valid_to are TEXT dates, 'yyyy-MM-dd'. That format
        //    sorts as text in date order, so string.Compare (which EF turns into
        //    SQL's < and >) compares dates correctly. valid_to is exclusive: it
        //    is the first day of the next price.
        //
        //    Two price types are read, not one (D-076). Both in-force sets come
        //    back and the choice is made here rather than in an ORDER BY,
        //    because the rule — promotional beats retail, then the later
        //    valid_from wins within a type — is worth reading as two lines
        //    rather than as a sort key. A variant has at most a handful of rows
        //    in force on any day.
        var today = calendar.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var inForce = await context.Prices
            .Where(p => p.VariantId == variant.VariantId)
            .Where(p => p.PriceType == PriceType.Retail || p.PriceType == PriceType.Promotional)
            .Where(p => string.Compare(p.ValidFrom, today) <= 0)
            .Where(p => p.ValidTo == null || string.Compare(p.ValidTo, today) > 0)
            .ToListAsync(cancellationToken);

        var price = Latest(inForce, PriceType.Promotional) ?? Latest(inForce, PriceType.Retail);

        if (price is null)
        {
            return new ProductLookupResult.NotSellable(NotSellableReason.NoCurrentPrice);
        }

        // Checked on whichever row won, and there is no falling back to retail
        // when a promotional row is HT. A promotion the shop advertised, sold
        // at the retail price instead because of a flag nobody sees, charges
        // the customer more than the shelf edge says. Refusing is loud, and a
        // manager fixes the row.
        if (!price.IsTaxInclusive)
        {
            return new ProductLookupResult.NotSellable(NotSellableReason.PriceNotTaxInclusive);
        }

        // 5. Stock on hand: one inventories row per batch, each in thousandths
        //    of the unit, summed in SQL. Also store-scoped by the filter. With
        //    no rows at all, EF's SUM comes back as 0, which here is true: no
        //    batch means nothing on the shelf, and the till warns but sells.
        var stockThousandths = await context.Inventories
            .Where(i => i.VariantId == variant.VariantId)
            .SumAsync(i => i.Quantity, cancellationToken);

        return new ProductLookupResult.Found(new ProductForSale(
            variant.VariantId,
            product.ProductId,
            product.ProductName,
            variant.VariantName,
            UnitPrecision.For(unit.UnitCode, checked((int)unit.DecimalPlaces)),
            tva.Rate,
            tva.Source,
            price.PriceValue,
            price.PriceType == PriceType.Promotional,
            Quantity.FromThousandths(stockThousandths, unit.UnitCode)));
    }

    /// <summary>
    /// The latest-starting row of one price type among those already in force, or null if
    /// this type has none. Ordinal on purpose: these are 'yyyy-MM-dd' strings, and a
    /// culture-aware comparison of dates-as-text is a bug waiting for a different machine.
    /// </summary>
    private static Price? Latest(List<Price> inForce, PriceType type) => inForce
        .Where(p => p.PriceType == type)
        .OrderByDescending(p => p.ValidFrom, StringComparer.Ordinal)
        .FirstOrDefault();
}
