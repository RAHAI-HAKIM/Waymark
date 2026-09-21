using Waymark.Domain;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Enums;
using Waymark.Domain.Inventory;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;
using Waymark.Domain.Reference;
using Waymark.Domain.Values;
using Waymark.Persistence.Catalogue;

namespace Waymark.Integration.Tests;

/// <summary>
/// What the till may sell for a barcode, and at what price: hop 1's rules
/// (D-066), each one a test.
///
/// <para>
/// The failures here are the silent kind: a till selling at yesterday's price,
/// at another store's price, or at zero because a price was missing. Every
/// receipt would print, every total would add up, and every figure would be
/// wrong. The database is a real migrated SQLite file, and each test builds its
/// own shop in it with unique ids, so the tests share nothing but the schema.
/// </para>
/// </summary>
public sealed class ProductLookupTests(MigratedDatabaseFixture database) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly DateOnly Today = new(2026, 9, 18);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);
    private static readonly DateOnly Tomorrow = Today.AddDays(1);
    private static readonly DateOnly LastMonth = Today.AddMonths(-1);

    private sealed class FixedCalendar(DateOnly today) : IStoreCalendar
    {
        public DateTimeOffset Now { get; } = new(today, new TimeOnly(10, 0), TimeSpan.FromHours(1));

        public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

        public int HourOfDay => Now.Hour;
    }

    // ----------------------------------------------------------- the shop

    /// <summary>
    /// One store with one variant for sale, and a second store beside it that
    /// must stay invisible. Prices and stock are added per test.
    /// </summary>
    private sealed class Shop
    {
        private static readonly DateTimeOffset Moment = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

        private readonly MigratedDatabaseFixture _database;
        private readonly string _suffix = Guid.NewGuid().ToString("N")[..12];

        public Shop(
            MigratedDatabaseFixture database,
            VariantStatus status = VariantStatus.Active,
            bool weighted = false,
            long?[]? categoryRates = null)
        {
            _database = database;
            categoryRates ??= [1_900];

            StoreId = $"store-{_suffix}";
            OtherStoreId = $"other-{_suffix}";
            UnitCode = $"pc-{_suffix}";
            ProductId = $"product-{_suffix}";
            VariantId = $"variant-{_suffix}";
            Barcode = $"2{_suffix}"[..13];

            using var context = database.NewContext();
            context.Stores.Add(NewStore(StoreId));
            context.Stores.Add(NewStore(OtherStoreId));
            context.UnitsOfMeasure.Add(new UnitOfMeasure
            {
                UnitCode = UnitCode,
                NameAr = "قطعة",
                NameFr = "pièce",
                Dimension = Dimension.Count,
                DecimalPlaces = 0,
                CreatedAt = Moment,
            });
            context.Products.Add(new Product
            {
                ProductId = ProductId,
                ProductName = "Lait UHT Candia",
                CreatedAt = Moment,
                UpdatedAt = Moment,
            });

            for (var i = 0; i < categoryRates.Length; i++)
            {
                var categoryId = $"category-{i}-{_suffix}";
                context.Categories.Add(new Category
                {
                    CategoryId = categoryId,
                    CategoryName = $"Category {i}",
                    Slug = categoryId,
                    TaxRate = categoryRates[i],
                    CreatedAt = Moment,
                    UpdatedAt = Moment,
                });
                context.ProductCategory.Add(new ProductCategory
                {
                    ProductId = ProductId,
                    CategoryId = categoryId,
                    IsPrimary = i == 0,
                    AddedAt = Moment,
                });
            }

            context.Variants.Add(new Variant
            {
                VariantId = VariantId,
                ProductId = ProductId,
                VariantName = "Brique 1L",
                Barcode = Barcode,
                SellingUnitCode = UnitCode,
                IsWeighted = weighted,
                Status = status,
                CreatedAt = Moment,
                UpdatedAt = Moment,
            });

            context.SaveChanges();
        }

        public string StoreId { get; }

        public string OtherStoreId { get; }

        public string UnitCode { get; }

        public string ProductId { get; }

        public string VariantId { get; }

        public string Barcode { get; }

        private static Store NewStore(string storeId) => new()
        {
            StoreId = storeId,
            StoreCode = storeId,
            StoreName = storeId,
            StoreType = "grocery",
            RoundingPolicy = Rounding.HalfUp,
            CreatedAt = Moment,
            UpdatedAt = Moment,
        };

        public Shop Price(
            DateOnly from,
            long minorUnits,
            DateOnly? to = null,
            PriceType type = PriceType.Retail,
            bool taxInclusive = true,
            string? storeId = null)
        {
            using var context = _database.NewContext();
            context.Prices.Add(new Price
            {
                VariantId = VariantId,
                StoreId = storeId ?? StoreId,
                ValidFrom = from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ValidTo = to?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                PriceType = type,
                PriceValue = Money.FromMinorUnits(minorUnits, Currency.Dzd),
                Currency = "DZD",
                IsTaxInclusive = taxInclusive,
                CreatedAt = Moment,
            });
            context.SaveChanges();
            return this;
        }

        /// <summary>One batch holding this many whole units.</summary>
        public Shop Stock(long units, string? storeId = null)
        {
            var batchId = $"batch-{Guid.NewGuid():N}";
            using var context = _database.NewContext();
            context.Batches.Add(new Batch
            {
                BatchId = batchId,
                ProductId = ProductId,
                StoreId = storeId ?? StoreId,
                ReceivedDate = LastMonth,
                CreatedAt = Moment,
            });
            context.Inventories.Add(new Inventory
            {
                StoreId = storeId ?? StoreId,
                VariantId = VariantId,
                BatchId = batchId,
                Quantity = units * Quantity.Scale,
                UpdatedAt = Moment,
            });
            context.SaveChanges();
            return this;
        }

        public Task<ProductLookupResult> Lookup(string? barcode = null, DateOnly? today = null)
        {
            var context = _database.NewContext(storeId: StoreId);
            var lookup = new ProductLookup(context, new FixedCalendar(today ?? Today));
            return lookup.FindForSaleAsync(barcode ?? Barcode);
        }

        public async Task<ProductForSale> Found(DateOnly? today = null)
        {
            var result = await Lookup(today: today);
            return Assert.IsType<ProductLookupResult.Found>(result).Product;
        }

        public async Task<NotSellableReason> Refused(DateOnly? today = null)
        {
            var result = await Lookup(today: today);
            return Assert.IsType<ProductLookupResult.NotSellable>(result).Reason;
        }
    }

    private static Money Dzd(long minorUnits) => Money.FromMinorUnits(minorUnits, Currency.Dzd);

    // ------------------------------------------------------------ found

    [Fact]
    public async Task A_known_barcode_with_a_price_is_found_with_everything_the_till_shows()
    {
        var shop = new Shop(database).Price(LastMonth, 12_000).Stock(24);

        var product = await shop.Found();

        Assert.Equal(shop.VariantId, product.VariantId);
        Assert.Equal(shop.ProductId, product.ProductId);
        Assert.Equal("Lait UHT Candia", product.ProductName);
        Assert.Equal("Brique 1L", product.VariantName);
        Assert.Equal(UnitPrecision.For(shop.UnitCode, 0), product.Unit);
        Assert.Equal(BasisPoints.StandardVat, product.TvaRate);
        Assert.Equal(Dzd(12_000), product.PriceTtc);
        Assert.Equal(Quantity.FromThousandths(24 * Quantity.Scale, shop.UnitCode), product.StockOnHand);
    }

    [Fact]
    public async Task A_barcode_no_variant_carries_is_unknown()
    {
        var shop = new Shop(database).Price(LastMonth, 12_000);

        var result = await shop.Lookup(barcode: "0000000000000");

        Assert.IsType<ProductLookupResult.UnknownBarcode>(result);
    }

    // ------------------------------------------------- the price in force

    [Fact]
    public async Task A_price_that_starts_tomorrow_is_not_yet_in_force()
    {
        var shop = new Shop(database)
            .Price(LastMonth, 12_000)
            .Price(Tomorrow, 13_000);

        Assert.Equal(Dzd(12_000), (await shop.Found()).PriceTtc);
    }

    [Fact]
    public async Task A_price_that_starts_today_is_in_force_today()
    {
        var shop = new Shop(database)
            .Price(LastMonth, 12_000, to: Today)
            .Price(Today, 13_000);

        Assert.Equal(Dzd(13_000), (await shop.Found()).PriceTtc);
    }

    [Fact]
    public async Task A_price_whose_valid_to_is_today_has_ended()
    {
        // valid_to is exclusive: it is the day the next price starts, as the
        // generator writes it. A price that ends today is yesterday's price.
        var shop = new Shop(database).Price(LastMonth, 12_000, to: Today);

        Assert.Equal(NotSellableReason.NoCurrentPrice, await shop.Refused());
    }

    [Fact]
    public async Task A_price_whose_valid_to_is_tomorrow_is_still_in_force()
    {
        var shop = new Shop(database).Price(LastMonth, 12_000, to: Tomorrow);

        Assert.Equal(Dzd(12_000), (await shop.Found()).PriceTtc);
    }

    [Fact]
    public async Task Of_two_prices_in_force_the_latest_start_wins()
    {
        // Overlapping rows, both open-ended: the newer one is the price.
        var shop = new Shop(database)
            .Price(LastMonth, 12_000)
            .Price(Yesterday, 12_500);

        Assert.Equal(Dzd(12_500), (await shop.Found()).PriceTtc);
    }

    [Fact]
    public async Task The_price_follows_the_store_calendar_not_a_fixed_date()
    {
        var shop = new Shop(database)
            .Price(LastMonth, 12_000, to: Tomorrow)
            .Price(Tomorrow, 13_000);

        Assert.Equal(Dzd(12_000), (await shop.Found(today: Today)).PriceTtc);
        Assert.Equal(Dzd(13_000), (await shop.Found(today: Tomorrow)).PriceTtc);
    }

    [Fact]
    public async Task A_promotional_price_is_ignored_in_the_skeleton()
    {
        var shop = new Shop(database)
            .Price(LastMonth, 12_000)
            .Price(Yesterday, 9_000, type: PriceType.Promotional);

        Assert.Equal(Dzd(12_000), (await shop.Found()).PriceTtc);
    }

    [Fact]
    public async Task A_promotional_price_alone_is_no_price()
    {
        var shop = new Shop(database).Price(Yesterday, 9_000, type: PriceType.Promotional);

        Assert.Equal(NotSellableReason.NoCurrentPrice, await shop.Refused());
    }

    [Fact]
    public async Task No_price_at_all_is_not_sellable_and_never_zero()
    {
        // Absence is never zero (D-037): a missing price sold at 0 DZD is a
        // receipt that prints, adds up, and gives the product away.
        var shop = new Shop(database).Stock(24);

        Assert.Equal(NotSellableReason.NoCurrentPrice, await shop.Refused());
    }

    [Fact]
    public async Task An_ht_price_in_force_is_refused()
    {
        var shop = new Shop(database).Price(LastMonth, 10_000, taxInclusive: false);

        Assert.Equal(NotSellableReason.PriceNotTaxInclusive, await shop.Refused());
    }

    [Fact]
    public async Task An_old_ht_price_superseded_by_a_ttc_one_does_not_matter()
    {
        var shop = new Shop(database)
            .Price(LastMonth, 10_000, to: Yesterday, taxInclusive: false)
            .Price(Yesterday, 12_000);

        Assert.Equal(Dzd(12_000), (await shop.Found()).PriceTtc);
    }

    // ------------------------------------------------------- the store

    [Fact]
    public async Task Another_stores_price_is_invisible()
    {
        var shop = new Shop(database);
        shop.Price(LastMonth, 12_000, storeId: shop.OtherStoreId);

        Assert.Equal(NotSellableReason.NoCurrentPrice, await shop.Refused());
    }

    [Fact]
    public async Task Another_stores_newer_price_does_not_replace_this_stores()
    {
        var shop = new Shop(database).Price(LastMonth, 12_000);
        shop.Price(Yesterday, 99_000, storeId: shop.OtherStoreId);

        Assert.Equal(Dzd(12_000), (await shop.Found()).PriceTtc);
    }

    // ----------------------------------------------------- the variant

    [Fact]
    public async Task A_discontinued_variant_still_sells_its_remaining_stock()
    {
        var shop = new Shop(database, status: VariantStatus.Discontinued).Price(LastMonth, 12_000).Stock(3);

        Assert.Equal(Dzd(12_000), (await shop.Found()).PriceTtc);
    }

    [Fact]
    public async Task An_archived_variant_is_not_sellable()
    {
        var shop = new Shop(database, status: VariantStatus.Archived).Price(LastMonth, 12_000).Stock(3);

        Assert.Equal(NotSellableReason.Archived, await shop.Refused());
    }

    [Fact]
    public async Task A_weighted_variant_waits_for_phase_1()
    {
        var shop = new Shop(database, weighted: true).Price(LastMonth, 12_000);

        Assert.Equal(NotSellableReason.Weighted, await shop.Refused());
    }

    // ------------------------------------------------------- the TVA rate

    [Fact]
    public async Task The_rate_comes_from_the_products_category()
    {
        var shop = new Shop(database, categoryRates: [900]).Price(LastMonth, 12_000);

        Assert.Equal(BasisPoints.ReducedVat, (await shop.Found()).TvaRate);
    }

    [Fact]
    public async Task A_category_without_a_rate_is_not_sellable_rather_than_untaxed()
    {
        // Absence is not zero: a null tax_rate read as 0% would sell a taxed
        // product with no TVA on the receipt.
        var shop = new Shop(database, categoryRates: [null]).Price(LastMonth, 12_000);

        Assert.Equal(NotSellableReason.NoTaxRate, await shop.Refused());
    }

    [Fact]
    public async Task A_product_in_no_category_has_no_rate()
    {
        var shop = new Shop(database, categoryRates: []).Price(LastMonth, 12_000);

        Assert.Equal(NotSellableReason.NoTaxRate, await shop.Refused());
    }

    [Fact]
    public async Task Two_categories_with_different_rates_conflict()
    {
        // Which one is right is O-24's question. Until it is answered, the till
        // refuses rather than picks.
        var shop = new Shop(database, categoryRates: [1_900, 900]).Price(LastMonth, 12_000);

        Assert.Equal(NotSellableReason.ConflictingTaxRates, await shop.Refused());
    }

    [Fact]
    public async Task Two_categories_with_the_same_rate_agree()
    {
        var shop = new Shop(database, categoryRates: [1_900, 1_900]).Price(LastMonth, 12_000);

        Assert.Equal(BasisPoints.StandardVat, (await shop.Found()).TvaRate);
    }

    [Fact]
    public async Task A_rated_category_beside_an_unrated_one_gives_the_rate()
    {
        // Only a category that says something about TVA can disagree. A second
        // category with no rate (a shelf grouping, say) is silent, not zero.
        var shop = new Shop(database, categoryRates: [1_900, null]).Price(LastMonth, 12_000);

        Assert.Equal(BasisPoints.StandardVat, (await shop.Found()).TvaRate);
    }

    // ---------------------------------------------------------- the stock

    [Fact]
    public async Task Stock_on_hand_is_the_sum_over_this_stores_batches()
    {
        var shop = new Shop(database).Price(LastMonth, 12_000).Stock(10).Stock(5);
        shop.Stock(100, storeId: shop.OtherStoreId);

        Assert.Equal(
            Quantity.FromThousandths(15 * Quantity.Scale, shop.UnitCode),
            (await shop.Found()).StockOnHand);
    }

    [Fact]
    public async Task No_stock_rows_is_zero_stock_and_still_sellable()
    {
        // Hakim, 18/09: an empty shelf is a warning for the cashier, never a
        // refusal. The product is in the customer's hand.
        var shop = new Shop(database).Price(LastMonth, 12_000);

        var product = await shop.Found();

        Assert.True(product.StockOnHand.IsZero);
        Assert.Equal(shop.UnitCode, product.StockOnHand.Unit);
    }

    [Fact]
    public async Task Negative_stock_is_still_sellable()
    {
        // A level going negative is not a bug (CLAUDE.md §3.8): the count was
        // wrong, not the sale.
        var shop = new Shop(database).Price(LastMonth, 12_000).Stock(2).Stock(-5);

        Assert.Equal(
            Quantity.FromThousandths(-3 * Quantity.Scale, shop.UnitCode),
            (await shop.Found()).StockOnHand);
    }
}
