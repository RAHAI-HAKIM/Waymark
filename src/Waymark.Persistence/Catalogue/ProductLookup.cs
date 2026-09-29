using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;
using Waymark.Domain.Sales;
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
public sealed class ProductLookup(WaymarkDbContext context, IStoreCalendar calendar) : IProductLookup, IProductSearch
{
    /// <summary>
    /// The five steps of D-066, in order, after the code is matched: an exact barcode, else an exact
    /// PLU, else a scale label in the store's format (D-090). Every early return is an answer the
    /// till shows, not an error: an exception here would reach the cashier as "server failed" for
    /// what is really "this product has no price".
    /// </summary>
    public async Task<ProductLookupResult> FindForSaleAsync(
        string code, long? typedWeightThousandths = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        // 1. The variant. `variants` has no store_id (the catalogue is the
        //    tenant's), so no filter applies and every store sees the same one.
        //    The barcode is unique (IX_variants_barcode), and so is the PLU
        //    (IX_variants_plu), so each finds at most one row; the barcode is
        //    asked first, because a scan is one (D-088).
        var variant = await context.Variants.FirstOrDefaultAsync(v => v.Barcode == code, cancellationToken)
            ?? await context.Variants.FirstOrDefaultAsync(v => v.Plu == code, cancellationToken);

        if (variant is not null)
        {
            return await ByCodeAsync(variant, typedWeightThousandths, cancellationToken);
        }

        // Only then a scale label (D-090). Every in-store barcode starts with 2, as labels do, so a
        // label is read only once no product answers to the code itself.
        return await ByLabelAsync(code, cancellationToken);
    }

    /// <summary>A product named by its own barcode or PLU, with a weight typed for it or not.</summary>
    private async Task<ProductLookupResult> ByCodeAsync(Variant variant, long? typedWeightThousandths, CancellationToken cancellationToken)
    {
        if (typedWeightThousandths is null)
        {
            return await EvaluateAsync(variant, cancellationToken);
        }

        if (!variant.IsWeighted)
        {
            return new ProductLookupResult.NotSellable(NotSellableReason.NotSoldByWeight);
        }

        var answer = await EvaluateAsync(variant, cancellationToken);
        if (answer is not ProductLookupResult.Found found)
        {
            return answer;
        }

        var unit = found.Product.Unit;
        var weight = typedWeightThousandths.Value;
        if (weight <= 0 || !unit.Allows(weight))
        {
            return new ProductLookupResult.NotSellable(NotSellableReason.WeightInvalid);
        }

        var policy = (await CurrentStoreAsync(cancellationToken)).RoundingPolicy;
        var quantity = unit.Quantity(weight);
        var amounts = WeighedLine.ByWeight(found.Product.PriceTtc, quantity, unit, found.Product.TvaRate, policy);
        return found with { Weighed = new WeighedQuantity(quantity, QuantitySource.TypedWeight, amounts) };
    }

    /// <summary>
    /// A scale label, read by the store's format: its item code is a PLU, leading zeros aside, and
    /// the product's <c>barcode_type</c> says whether the value is a weight or a price (D-090).
    /// </summary>
    private async Task<ProductLookupResult> ByLabelAsync(string code, CancellationToken cancellationToken)
    {
        // The store's format was checked when it was set (--scale-format), so a format that no
        // longer reads is a broken store and throws rather than turning every label away quietly.
        var store = await CurrentStoreAsync(cancellationToken);
        var format = ScaleLabelFormat.Parse(store.ScaleLabelFormat);
        var label = format.Read(code);
        if (label is null)
        {
            return new ProductLookupResult.UnknownBarcode();
        }

        // PLUs are few and short; compared here so "00537" on the label finds PLU "537".
        var withPlu = await context.Variants.Where(v => v.Plu != null).ToListAsync(cancellationToken);
        var named = withPlu.Where(v => ScaleLabelFormat.ItemCodeOf(v.Plu!) == label.ItemCode).ToList();
        if (named.Count == 0)
        {
            return new ProductLookupResult.UnknownBarcode();
        }

        // PLUs "12" and "0012" are two rows to the database and one item code to a label. Picking one
        // would sell the other's weight at the wrong price, so the catalogue is told to fix it.
        var variant = named[0];
        if (named.Count > 1 || !variant.IsWeighted || variant.BarcodeType == BarcodeType.Standard)
        {
            return new ProductLookupResult.NotSellable(NotSellableReason.LabelNotSetUp);
        }

        var answer = await EvaluateAsync(variant, cancellationToken);
        if (answer is not ProductLookupResult.Found found)
        {
            return answer;
        }

        var product = found.Product;
        var value = label.Value;
        if (variant.BarcodeType == BarcodeType.WeightEmbedded)
        {
            // The value is thousandths of the selling unit: grams, for a product sold by the kilo.
            if (value <= 0 || !product.Unit.Allows(value))
            {
                return new ProductLookupResult.NotSellable(NotSellableReason.LabelValueInvalid);
            }

            var weight = product.Unit.Quantity(value);
            var amounts = WeighedLine.ByWeight(product.PriceTtc, weight, product.Unit, product.TvaRate, store.RoundingPolicy);
            return found with { Weighed = new WeighedQuantity(weight, QuantitySource.LabelWeight, amounts) };
        }

        // A price label: its price is exact and the weight is worked back from it (O-26).
        var declared = Money.FromMinorUnits(
            format.PriceUnit == LabelPriceUnit.Dinars ? checked(value * 100) : value, product.PriceTtc.Currency);
        var line = declared.IsPositive && product.PriceTtc.IsPositive
            ? WeighedLine.ByDeclaredTotal(product.PriceTtc, declared, product.Unit, product.TvaRate, store.RoundingPolicy)
            : null;
        return line is { } declaredLine
            ? found with { Weighed = new WeighedQuantity(declaredLine.Quantity, QuantitySource.LabelPrice, declaredLine.Amounts) }
            : new ProductLookupResult.NotSellable(NotSellableReason.LabelValueInvalid);
    }

    /// <summary>The store this context is filtered to: its rounding policy and its label format.</summary>
    private async Task<Store> CurrentStoreAsync(CancellationToken cancellationToken) =>
        await context.Stores.FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException("This store has no row in stores; it is not commissioned.");

    /// <summary>
    /// The search (B1, D-088): every variant's name, ranked by <see cref="NameSearch"/>, and each
    /// of the best answered as a scan of it would be. The catalogue is read whole and ranked here,
    /// not in SQL: a shop has hundreds of variants, and SQLite's LIKE neither folds accents nor
    /// knows where a word starts.
    /// </summary>
    public async Task<IReadOnlyList<ProductSearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var names = await context.Variants
            .Join(context.Products, v => v.ProductId, p => p.ProductId, (v, p) => new { v.VariantId, p.ProductName, v.VariantName })
            .ToListAsync(cancellationToken);

        var best = NameSearch.Rank(query, names, row => $"{row.ProductName} {row.VariantName}");
        var hits = new List<ProductSearchHit>(best.Count);
        foreach (var row in best)
        {
            var variant = await context.Variants.SingleAsync(v => v.VariantId == row.VariantId, cancellationToken);
            var code = variant.Barcode ?? variant.Plu;
            var result = code is null
                ? new ProductLookupResult.NotSellable(NotSellableReason.NoCode)
                : await EvaluateAsync(variant, cancellationToken);
            hits.Add(new ProductSearchHit(row.VariantId, row.ProductName, row.VariantName, code, result));
        }

        return hits;
    }

    /// <summary>What the till may sell of one variant: D-066's steps after the variant is found.</summary>
    private async Task<ProductLookupResult> EvaluateAsync(Variant variant, CancellationToken cancellationToken)
    {
        if (variant.Status == VariantStatus.Archived)
        {
            return new ProductLookupResult.NotSellable(NotSellableReason.Archived);
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
            Quantity.FromThousandths(stockThousandths, unit.UnitCode),
            variant.IsWeighted));
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
