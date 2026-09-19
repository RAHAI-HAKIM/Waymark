using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Enums;
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
        //    the join follows that link and keeps only each category's rate. A
        //    null rate says nothing about TVA, so it is dropped before counting,
        //    and Distinct collapses two categories that agree into one rate.
        //    EF turns this whole chain into one SQL query:
        //      SELECT DISTINCT c.tax_rate FROM product_category pc
        //      JOIN categories c ON c.category_id = pc.category_id
        //      WHERE pc.product_id = @p AND c.tax_rate IS NOT NULL
        var rates = await context.ProductCategory
            .Where(link => link.ProductId == variant.ProductId)
            .Join(
                context.Categories,
                link => link.CategoryId,
                category => category.CategoryId,
                (link, category) => category.TaxRate)
            .Where(rate => rate != null)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (rates.Count == 0)
        {
            return new ProductLookupResult.NotSellable(NotSellableReason.NoTaxRate);
        }

        if (rates.Count > 1)
        {
            return new ProductLookupResult.NotSellable(NotSellableReason.ConflictingTaxRates);
        }

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
        var today = calendar.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var price = await context.Prices
            .Where(p => p.VariantId == variant.VariantId && p.PriceType == PriceType.Retail)
            .Where(p => string.Compare(p.ValidFrom, today) <= 0)
            .Where(p => p.ValidTo == null || string.Compare(p.ValidTo, today) > 0)
            .OrderByDescending(p => p.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken);

        if (price is null)
        {
            return new ProductLookupResult.NotSellable(NotSellableReason.NoCurrentPrice);
        }

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
            new BasisPoints(checked((int)rates[0]!.Value)),
            price.PriceValue,
            Quantity.FromThousandths(stockThousandths, unit.UnitCode)));
    }
}
