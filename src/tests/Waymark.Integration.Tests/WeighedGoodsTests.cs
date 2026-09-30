using Microsoft.EntityFrameworkCore;
using Waymark.Application.Commands;
using Waymark.Application.IdGenerator;
using Waymark.Application.Organisation;
using Waymark.Application.Sales;
using Waymark.Application.Statistics;
using QuantitySources = Waymark.Contracts.Pos.QuantitySources;
using Waymark.Domain;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Enums;
using Waymark.Domain.Inventory;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.Persistence;
using Waymark.Persistence.Catalogue;
using Waymark.Persistence.Organisation;
using Waymark.Persistence.Privacy;
using Waymark.Persistence.Sales;
using Waymark.Persistence.Sync;
using DomainReason = Waymark.Domain.Catalogue.NotSellableReason;

namespace Waymark.Integration.Tests;

/// <summary>
/// Weighed goods, from the code to the rows (session B3, D-090), on a real SQLite file. The silent
/// failures: a milk barcode in the 20 range read as a scale label; a label's price charged as
/// weight × price; a label split over two batches that loses a centime; a typed weight recorded as
/// a count; a PLU missed because the label prints it with zeros in front.
/// </summary>
public sealed class WeighedGoodsTests(MigratedDatabaseFixture database) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly DateOnly Today = new(2026, 9, 27);
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Moment = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>PLUs are unique across the database every test here shares.</summary>
    private static int s_nextPlu = 100;

    private sealed class FixedCalendar : IStoreCalendar
    {
        public DateTimeOffset Now => new(WeighedGoodsTests.Today, new TimeOnly(10, 30), TimeSpan.FromHours(1));

        public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

        public int HourOfDay => Now.Hour;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// A store with a till and a cashier, a kilo weighed to the gram, and tomatoes at 180,00/kg sold
    /// by weight under a short PLU, so every label prints it with zeros in front.
    /// </summary>
    private sealed class Shop
    {
        private readonly MigratedDatabaseFixture _database;
        private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];

        public Shop(
            MigratedDatabaseFixture database,
            BarcodeType labels = BarcodeType.WeightEmbedded,
            long pricePerKg = 18_000,
            string? scaleFormat = null,
            Rounding policy = Rounding.HalfUp,
            bool weighted = true)
        {
            _database = database;
            StoreId = $"store-{_suffix}";
            TerminalId = $"till-{_suffix}";
            StaffId = $"staff-{_suffix}";
            Kg = $"kg-{_suffix}";
            Plu = Interlocked.Increment(ref s_nextPlu).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Tomatoes = $"v-tomatoes-{_suffix}";
            ProductId = $"tomatoes-{_suffix}";

            using var context = database.NewContext();
            if (!context.Roles.Any(role => role.RoleCode == "cashier"))
            {
                context.Roles.Add(new Domain.Engine.Role { RoleCode = "cashier", Rank = 1, LabelAr = "أمين الصندوق", LabelFr = "caissier", CreatedAt = Moment });
            }

            context.Stores.Add(new Store
            {
                StoreId = StoreId,
                StoreCode = $"S{_suffix[..6]}",
                StoreName = StoreId,
                StoreType = "grocery",
                RoundingPolicy = policy,
                ScaleLabelFormat = scaleFormat,
                CreatedAt = Moment,
                UpdatedAt = Moment,
            });
            context.Terminals.Add(new Terminal { TerminalId = TerminalId, StoreId = StoreId, TerminalName = "Caisse 1", CreatedAt = Moment, UpdatedAt = Moment });
            context.Staff.Add(new Staff
            {
                StaffId = StaffId,
                StoreId = StoreId,
                StaffName = "Caissier",
                Role = "cashier",
                PinHash = "-",
                JoinDate = new DateOnly(2026, 1, 1),
                CreatedAt = Moment,
                UpdatedAt = Moment,
            });
            context.UnitsOfMeasure.Add(new UnitOfMeasure
            {
                UnitCode = Kg,
                NameAr = "كيلوغرام",
                NameFr = "kilogramme",
                Dimension = Dimension.Weight,
                DecimalPlaces = 3,
                CreatedAt = Moment,
            });
            context.Categories.Add(new Category
            {
                CategoryId = $"cat-{_suffix}",
                CategoryName = "Fruits et légumes",
                Slug = $"cat-{_suffix}",
                TaxRate = 900,
                CreatedAt = Moment,
                UpdatedAt = Moment,
            });
            context.Products.Add(new Product { ProductId = ProductId, ProductName = "Tomates", CreatedAt = Moment, UpdatedAt = Moment });
            context.ProductCategory.Add(new ProductCategory { ProductId = ProductId, CategoryId = $"cat-{_suffix}", IsPrimary = true, AddedAt = Moment });
            context.Variants.Add(new Variant
            {
                VariantId = Tomatoes,
                ProductId = ProductId,
                VariantName = "Vrac",
                Plu = Plu,
                BarcodeType = labels,
                SellingUnitCode = Kg,
                IsWeighted = weighted,
                CreatedAt = Moment,
                UpdatedAt = Moment,
            });
            context.Prices.Add(new Price
            {
                VariantId = Tomatoes,
                StoreId = StoreId,
                ValidFrom = "2026-01-01",
                PriceValue = Money.FromMinorUnits(pricePerKg, Currency.Dzd),
                CreatedAt = Moment,
            });
            context.SaveChanges();
        }

        public string StoreId { get; }

        public string TerminalId { get; }

        public string StaffId { get; }

        public string Kg { get; }

        public string Plu { get; }

        public string ProductId { get; }

        public string Tomatoes { get; }

        /// <summary>A batch of tomatoes holding this many grams, received that many days ago.</summary>
        public string Receive(long grams, int daysAgo)
        {
            var batchId = $"batch-{Guid.NewGuid():N}";
            using var context = _database.NewContext();
            context.Batches.Add(new Batch { BatchId = batchId, ProductId = ProductId, StoreId = StoreId, ReceivedDate = Today.AddDays(-daysAgo), CreatedAt = Moment });
            context.BatchItems.Add(new BatchItem
            {
                BatchId = batchId,
                VariantId = Tomatoes,
                QuantityReceived = grams,
                UnitCode = Kg,
                UnitCost = Money.FromMinorUnits(9_000, Currency.Dzd),
                CreatedAt = Moment,
            });
            context.Inventories.Add(new Inventory { StoreId = StoreId, VariantId = Tomatoes, BatchId = batchId, Quantity = grams, UpdatedAt = Moment });
            context.SaveChanges();
            return batchId;
        }

        /// <summary>A second product with its own code: whatever the test needs beside the tomatoes.</summary>
        public void AddCounted(string code, string? plu = null, bool weighted = false, BarcodeType labels = BarcodeType.Standard)
        {
            var id = $"other-{Guid.NewGuid():N}";
            using var context = _database.NewContext();
            context.Products.Add(new Product { ProductId = id, ProductName = "Autre", CreatedAt = Moment, UpdatedAt = Moment });
            context.Variants.Add(new Variant
            {
                VariantId = id,
                ProductId = id,
                VariantName = "1",
                Barcode = code,
                Plu = plu,
                BarcodeType = labels,
                IsWeighted = weighted,
                SellingUnitCode = Kg,
                CreatedAt = Moment,
                UpdatedAt = Moment,
            });
            context.Prices.Add(new Price
            {
                VariantId = id,
                StoreId = StoreId,
                ValidFrom = "2026-01-01",
                PriceValue = Money.FromMinorUnits(14_300, Currency.Dzd),
                CreatedAt = Moment,
            });
            context.SaveChanges();
        }

        /// <summary>A scale label for the tomatoes in the standard layout: prefix, five item digits, five value digits.</summary>
        public string Label(long value, string prefix = "21") => WithCheckDigit($"{prefix}{Plu.PadLeft(5, '0')}{value:D5}");

        public Task<ProductLookupResult> Lookup(string code, long? typedWeight = null)
        {
            var context = _database.NewContext(storeId: StoreId);
            return new ProductLookup(context, new FixedCalendar()).FindForSaleAsync(code, typedWeight);
        }
    }

    private static string WithCheckDigit(string twelve)
    {
        var sum = twelve.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return twelve + ((10 - (sum % 10)) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Money Dzd(long minorUnits) => Money.FromMinorUnits(minorUnits, Currency.Dzd);

    private async Task<CompletedSale> Sell(Shop shop, params SaleLineRequest[] lines)
    {
        await using var context = database.NewContext(storeId: shop.StoreId);
        var clock = new FixedClock();
        var calendar = new FixedCalendar();
        var ids = new UlidGenerator();
        var unitOfWork = new WaymarkUnitOfWork(context);
        var executor = new CommandExecutor(unitOfWork, ids, new ProcessingLogWriter(context, ids, new FixedCurrentStore(shop.StoreId), clock));
        var handler = new CompleteSaleHandler(
            new ProductLookup(context, calendar), new SalesLedger(context), unitOfWork, new OutboxSequence(context),
            new NullTier2Writer(), calendar, clock, new Waymark.Persistence.Reference.ReasonCodes(context), TabChargesFor.Context(context));

        return await executor.ExecuteAsync(handler, new CompleteSale(shop.TerminalId, shop.StaffId, lines));
    }

    private async Task<List<TransactionItem>> ItemsOf(Shop shop, string transactionId)
    {
        await using var read = database.NewContext(storeId: shop.StoreId);
        return await read.TransactionItems.Where(item => item.TransactionId == transactionId).ToListAsync();
    }

    private static WeighedQuantity Weighed(ProductLookupResult result) =>
        Assert.IsType<ProductLookupResult.Found>(result).Weighed ?? throw new Xunit.Sdk.XunitException("Found, but with no weight.");

    private static DomainReason Refused(ProductLookupResult result) =>
        Assert.IsType<ProductLookupResult.NotSellable>(result).Reason;

    // ------------------------------------------------------------------ the order of the lookup

    [Fact]
    public async Task A_barcode_that_is_also_a_valid_label_is_sold_as_its_own_product()
    {
        // A count product whose barcode happens to read, in the standard layout, as a 0,556 kg label
        // of the tomatoes. The exact barcode wins (D-090): every seed-42 barcode starts with 20.
        var shop = new Shop(database);
        var code = shop.Label(556);
        shop.AddCounted(code);

        var found = Assert.IsType<ProductLookupResult.Found>(await shop.Lookup(code));

        Assert.NotEqual(shop.Tomatoes, found.Product.VariantId);
        Assert.Null(found.Weighed);
    }

    [Fact]
    public async Task A_weighed_products_plu_typed_alone_asks_for_a_weight()
    {
        var shop = new Shop(database);

        var found = Assert.IsType<ProductLookupResult.Found>(await shop.Lookup(shop.Plu));

        Assert.True(found.Product.IsWeighted);
        Assert.Null(found.Weighed);
    }

    // ------------------------------------------------------------------ a typed weight

    [Fact]
    public async Task A_typed_weight_is_priced_by_the_server_and_marked_typed()
    {
        var shop = new Shop(database);

        var weighed = Weighed(await shop.Lookup(shop.Plu, typedWeight: 556));

        Assert.Equal(Quantity.FromThousandths(556, shop.Kg), weighed.Quantity);
        Assert.Equal(QuantitySource.TypedWeight, weighed.Source);
        Assert.Equal(Dzd(10_008), weighed.Amounts.LineTotal);
    }

    [Fact]
    public async Task A_weight_typed_for_a_product_sold_by_count_is_refused()
    {
        var shop = new Shop(database, weighted: false, labels: BarcodeType.Standard);

        Assert.Equal(DomainReason.NotSoldByWeight, Refused(await shop.Lookup(shop.Plu, typedWeight: 556)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_typed_weight_not_above_zero_is_refused(long thousandths)
    {
        var shop = new Shop(database);

        Assert.Equal(DomainReason.WeightInvalid, Refused(await shop.Lookup(shop.Plu, typedWeight: thousandths)));
    }

    // ------------------------------------------------------------------ labels

    [Fact]
    public async Task A_weight_label_reads_grams_and_names_its_product_by_plu_with_the_zeros_off()
    {
        var shop = new Shop(database, labels: BarcodeType.WeightEmbedded);

        var weighed = Weighed(await shop.Lookup(shop.Label(556)));

        Assert.Equal(Quantity.FromThousandths(556, shop.Kg), weighed.Quantity);
        Assert.Equal(QuantitySource.LabelWeight, weighed.Source);
        Assert.Equal(Dzd(10_008), weighed.Amounts.LineTotal);
    }

    [Fact]
    public async Task A_price_label_is_charged_its_price_and_the_weight_is_worked_back()
    {
        // O-26: 100 DA of tomatoes at 180,00/kg. The label's price, never 100,08.
        var shop = new Shop(database, labels: BarcodeType.PriceEmbedded);

        var weighed = Weighed(await shop.Lookup(shop.Label(100)));

        Assert.Equal(QuantitySource.LabelPrice, weighed.Source);
        Assert.Equal(Quantity.FromThousandths(556, shop.Kg), weighed.Quantity);
        Assert.Equal(Dzd(10_000), weighed.Amounts.LineTotal);
    }

    [Fact]
    public async Task A_store_set_to_centimes_reads_a_price_label_in_centimes()
    {
        var shop = new Shop(database, labels: BarcodeType.PriceEmbedded, scaleFormat: "standard-centimes");

        var weighed = Weighed(await shop.Lookup(shop.Label(10_000)));

        Assert.Equal(Dzd(10_000), weighed.Amounts.LineTotal);
    }

    [Fact]
    public async Task A_label_outside_the_stores_prefixes_is_no_product()
    {
        var shop = new Shop(database, scaleFormat: "PPIIIIIVVVVVC;prefixes=27");

        Assert.IsType<ProductLookupResult.UnknownBarcode>(await shop.Lookup(shop.Label(556, prefix: "21")));
        Assert.Equal(QuantitySource.LabelWeight, Weighed(await shop.Lookup(shop.Label(556, prefix: "27"))).Source);
    }

    [Fact]
    public async Task A_label_whose_check_digit_fails_is_no_product()
    {
        var shop = new Shop(database);
        var label = shop.Label(556);
        var smudged = label[..^1] + (char)('0' + ((label[^1] - '0' + 1) % 10));

        Assert.IsType<ProductLookupResult.UnknownBarcode>(await shop.Lookup(smudged));
    }

    [Fact]
    public async Task A_label_for_a_product_not_set_up_for_labels_says_so()
    {
        var shop = new Shop(database, labels: BarcodeType.Standard);

        Assert.Equal(DomainReason.LabelNotSetUp, Refused(await shop.Lookup(shop.Label(556))));
    }

    [Fact]
    public async Task Two_plus_that_read_as_one_item_code_are_refused_rather_than_guessed()
    {
        var shop = new Shop(database);
        shop.AddCounted($"x{Guid.NewGuid():N}"[..13], plu: "00" + shop.Plu, weighted: true, labels: BarcodeType.WeightEmbedded);

        Assert.Equal(DomainReason.LabelNotSetUp, Refused(await shop.Lookup(shop.Label(556))));
    }

    [Theory]
    [InlineData(BarcodeType.WeightEmbedded, 0)]
    [InlineData(BarcodeType.PriceEmbedded, 0)]
    public async Task A_label_worth_nothing_is_refused(BarcodeType labels, long value)
    {
        var shop = new Shop(database, labels: labels);

        Assert.Equal(DomainReason.LabelValueInvalid, Refused(await shop.Lookup(shop.Label(value))));
    }

    // ------------------------------------------------------------------ the sale's rows

    [Fact]
    public async Task A_typed_weight_is_sold_as_that_weight_and_recorded_as_typed()
    {
        var shop = new Shop(database);
        var batch = shop.Receive(grams: 2_000, daysAgo: 3);

        var sale = await Sell(shop, new SaleLineRequest(shop.Plu, 1, WeightThousandths: 556));

        var item = Assert.Single(await ItemsOf(shop, sale.TransactionId));
        Assert.Equal(556, item.Quantity);
        Assert.Equal(QuantitySource.TypedWeight, item.QuantitySource);
        Assert.Equal(Dzd(10_008), item.LineTotal);
        Assert.Equal(Dzd(10_008), sale.Total);

        await using var read = database.NewContext(storeId: shop.StoreId);
        Assert.Equal(2_000 - 556, (await read.Inventories.SingleAsync(level => level.BatchId == batch)).Quantity);
        Assert.Equal(-556, (await read.StockMovements.SingleAsync(move => move.ReferenceId == sale.TransactionId)).QuantityChanged);
    }

    [Fact]
    public async Task A_price_label_over_two_batches_is_split_so_the_rows_sum_to_the_label()
    {
        var shop = new Shop(database, labels: BarcodeType.PriceEmbedded);
        var older = shop.Receive(grams: 300, daysAgo: 5);
        var newer = shop.Receive(grams: 1_000, daysAgo: 1);

        var sale = await Sell(shop, new SaleLineRequest(shop.Label(100), 1));

        var items = await ItemsOf(shop, sale.TransactionId);
        Assert.Equal(Dzd(5_396), items.Single(item => item.BatchId == older).LineTotal);
        Assert.Equal(Dzd(4_604), items.Single(item => item.BatchId == newer).LineTotal);
        Assert.Equal(Dzd(10_000), sale.Total);
        Assert.All(items, item =>
        {
            Assert.Equal(QuantitySource.LabelPrice, item.QuantitySource);
            Assert.True(WeighedLine.Recomputes(
                item.QuantitySource, item.SellPrice, Quantity.FromThousandths(item.Quantity, item.UnitCode), item.DiscountAmount,
                item.LineTotal, UnitPrecision.For(item.UnitCode, 3), Rounding.HalfUp));
        });
    }

    [Fact]
    public async Task A_weight_label_over_two_batches_prices_each_batch_as_its_own_row()
    {
        // 0,300 kg and 0,256 kg at 179,99/kg: 54,00 + 46,08 = 100,08, where the line priced once
        // would be 100,07. Every row is its own weight × price, and recomputes from itself (D-090).
        var shop = new Shop(database, labels: BarcodeType.WeightEmbedded, pricePerKg: 17_999);
        shop.Receive(grams: 300, daysAgo: 5);
        shop.Receive(grams: 1_000, daysAgo: 1);

        var sale = await Sell(shop, new SaleLineRequest(shop.Label(556), 1));

        var items = await ItemsOf(shop, sale.TransactionId);
        Assert.Equal(Dzd(10_008), sale.Total);
        Assert.All(items, item =>
        {
            Assert.Equal(QuantitySource.LabelWeight, item.QuantitySource);
            Assert.True(WeighedLine.Recomputes(
                item.QuantitySource, item.SellPrice, Quantity.FromThousandths(item.Quantity, item.UnitCode), item.DiscountAmount,
                item.LineTotal, UnitPrecision.For(item.UnitCode, 3), Rounding.HalfUp));
        });
    }

    [Fact]
    public async Task A_weighed_product_sent_without_a_weight_is_refused_and_nothing_is_written()
    {
        var shop = new Shop(database);
        shop.Receive(grams: 2_000, daysAgo: 3);

        await Assert.ThrowsAsync<SaleRefusedException>(() => Sell(shop, new SaleLineRequest(shop.Plu, 1)));

        await using var read = database.NewContext(storeId: shop.StoreId);
        Assert.False(await read.Transactions.AnyAsync());
    }

    [Fact]
    public async Task A_weighed_line_sent_with_a_count_is_refused()
    {
        var shop = new Shop(database);
        shop.Receive(grams: 2_000, daysAgo: 3);

        await Assert.ThrowsAsync<SaleRefusedException>(() => Sell(shop, new SaleLineRequest(shop.Plu, 2, WeightThousandths: 556)));
    }

    // ------------------------------------------------------------------ the column and the wire

    [Fact]
    public async Task The_wire_names_every_quantity_source_the_column_accepts_and_no_other()
    {
        await using var context = database.NewContext();
        var sql = await context.Database.SqlQueryRaw<string>(
            "SELECT sql AS \"Value\" FROM sqlite_schema WHERE type = 'table' AND name = 'transaction_items'").SingleAsync();

        string[] wire = [QuantitySources.Count, QuantitySources.TypedWeight, QuantitySources.LabelWeight, QuantitySources.LabelPrice];
        Assert.Contains("quantity_source IN ('count','typed_weight','label_weight','label_price')", sql, StringComparison.Ordinal);
        Assert.Equal(Enum.GetValues<QuantitySource>().Length, wire.Length);
    }

    // ------------------------------------------------------------------ the store's format

    private async Task<ScaleLabelFormat> SetFormat(Shop shop, string? format)
    {
        await using var context = database.NewContext(storeId: shop.StoreId);
        var clock = new FixedClock();
        var ids = new UlidGenerator();
        var unitOfWork = new WaymarkUnitOfWork(context);
        var executor = new CommandExecutor(unitOfWork, ids, new ProcessingLogWriter(context, ids, new FixedCurrentStore(shop.StoreId), clock));
        return await executor.ExecuteAsync(new SetScaleLabelFormatHandler(new StoreSettings(context), clock), new SetScaleLabelFormat(format));
    }

    private async Task<string?> StoredFormat(Shop shop)
    {
        await using var read = database.NewContext(storeId: shop.StoreId);
        return (await read.Stores.SingleAsync()).ScaleLabelFormat;
    }

    [Fact]
    public async Task A_format_set_by_name_is_stored_and_the_lookup_reads_by_it()
    {
        var shop = new Shop(database, labels: BarcodeType.PriceEmbedded);

        await SetFormat(shop, "standard-centimes");

        Assert.Equal("standard-centimes", await StoredFormat(shop));
        Assert.Equal(Dzd(10_000), Weighed(await shop.Lookup(shop.Label(10_000))).Amounts.LineTotal);
    }

    [Fact]
    public async Task The_default_format_is_stored_as_nothing()
    {
        var shop = new Shop(database, scaleFormat: "item6");

        await SetFormat(shop, "standard");

        Assert.Null(await StoredFormat(shop));
    }

    [Fact]
    public async Task A_mask_that_cannot_be_read_is_refused_and_the_stores_format_stays()
    {
        var shop = new Shop(database, scaleFormat: "item6");

        await Assert.ThrowsAsync<ScaleFormatRefusedException>(() => SetFormat(shop, "PPIIIIIVVVVV"));

        Assert.Equal("item6", await StoredFormat(shop));
    }
}
