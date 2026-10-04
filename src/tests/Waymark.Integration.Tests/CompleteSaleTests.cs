using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Waymark.Application.Commands;
using Waymark.Application.IdGenerator;
using Waymark.Application.Sales;
using Waymark.Application.Statistics;
using Waymark.Application.Sync;
using Waymark.Contracts.Sync;
using Waymark.Domain;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Enums;
using Waymark.Domain.Inventory;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;
using Waymark.Domain.Reference;
using Waymark.Domain.Statistics;
using Waymark.Domain.Sync;
using Waymark.Domain.Values;
using Waymark.Persistence;
using Waymark.Persistence.Catalogue;
using Waymark.Persistence.Privacy;
using Waymark.Persistence.Sales;
using Waymark.Persistence.Sync;
using SaleLimits = Waymark.Domain.Sales.SaleLimits;

namespace Waymark.Integration.Tests;

/// <summary>
/// A cash sale, through the real executor, on a real SQLite file (hop 2, D-070). The risky
/// rule: <b>every row a sale writes commits together or not at all, and the money is right</b>.
/// A half-written sale is the silent kind of wrong: a transaction with no stock movement, or
/// an invoice number used by a sale that never happened.
/// </summary>
public sealed partial class CompleteSaleTests(MigratedDatabaseFixture database) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly DateOnly Today = new(2026, 9, 19);
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 9, 30, 0, TimeSpan.Zero);

    private sealed class FixedCalendar : IStoreCalendar
    {
        /// <summary>10:30 in the store, on the test's day.</summary>
        public DateTimeOffset Now => new(CompleteSaleTests.Today, new TimeOnly(10, 30), TimeSpan.FromHours(1));

        public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

        public int HourOfDay => Now.Hour;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A store with a till, a cashier, and two products on the shelf. Unique ids per test.</summary>
    private sealed class Shop
    {
        private static readonly DateTimeOffset Moment = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

        private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];

        public Shop(MigratedDatabaseFixture database)
        {
            StoreId = $"store-{_suffix}";
            TerminalId = $"till-{_suffix}";
            StaffId = $"staff-{_suffix}";
            StoreCode = $"S{_suffix[..6]}";

            using var context = database.NewContext();

            // Roles are the tenant's, shared by every test in this database.
            if (!context.Roles.Any(role => role.RoleCode == "cashier"))
            {
                context.Roles.Add(new Domain.Engine.Role { RoleCode = "cashier", Rank = 1, LabelAr = "أمين الصندوق", LabelFr = "caissier", CreatedAt = Moment });
            }

            context.Stores.Add(new Store
            {
                StoreId = StoreId,
                StoreCode = StoreCode,
                StoreName = StoreId,
                StoreType = "grocery",
                RoundingPolicy = Rounding.HalfUp,
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
                UnitCode = $"pc-{_suffix}",
                NameAr = "قطعة",
                NameFr = "pièce",
                Dimension = Dimension.Count,
                CreatedAt = Moment,
            });
            context.SaveChanges();

            Milk = AddProduct(database, "milk", 900, price: 14_300);
            Bread = AddProduct(database, "bread", 1_900, price: 12_050);
        }

        public string StoreId { get; }

        public string TerminalId { get; }

        public string StaffId { get; }

        public string StoreCode { get; }

        public Product Milk { get; }

        public Product Bread { get; }

        public string Unit => $"pc-{_suffix}";

        private Product AddProduct(MigratedDatabaseFixture database, string name, long rate, long price)
        {
            var product = new Product($"{name}-{_suffix}", $"v-{name}-{_suffix}", $"2{_suffix}{name[0]}0"[..13]);
            using var context = database.NewContext();
            context.Categories.Add(new Category
            {
                CategoryId = $"cat-{product.Id}",
                CategoryName = name,
                Slug = $"cat-{product.Id}",
                TaxRate = rate,
                CreatedAt = Moment,
                UpdatedAt = Moment,
            });
            context.Products.Add(new Domain.Catalogue.Product { ProductId = product.Id, ProductName = name, CreatedAt = Moment, UpdatedAt = Moment });
            context.ProductCategory.Add(new ProductCategory { ProductId = product.Id, CategoryId = $"cat-{product.Id}", IsPrimary = true, AddedAt = Moment });
            context.Variants.Add(new Variant
            {
                VariantId = product.VariantId,
                ProductId = product.Id,
                VariantName = "1",
                Barcode = product.Barcode,
                SellingUnitCode = Unit,
                CreatedAt = Moment,
                UpdatedAt = Moment,
            });
            context.Prices.Add(new Price
            {
                VariantId = product.VariantId,
                StoreId = StoreId,
                ValidFrom = "2026-01-01",
                PriceValue = Money.FromMinorUnits(price, Currency.Dzd),
                CreatedAt = Moment,
            });
            context.SaveChanges();
            return product;
        }

        /// <summary>A batch of the product holding <paramref name="units"/>, received that many days ago.</summary>
        public string Receive(MigratedDatabaseFixture database, Product product, long units, int daysAgo, long unitCost = 10_000)
        {
            var batchId = $"batch-{Guid.NewGuid():N}";
            using var context = database.NewContext();
            context.Batches.Add(new Batch { BatchId = batchId, ProductId = product.Id, StoreId = StoreId, ReceivedDate = Today.AddDays(-daysAgo), CreatedAt = Moment });
            context.BatchItems.Add(new BatchItem
            {
                BatchId = batchId,
                VariantId = product.VariantId,
                QuantityReceived = units * Quantity.Scale,
                UnitCode = Unit,
                UnitCost = Money.FromMinorUnits(unitCost, Currency.Dzd),
                CreatedAt = Moment,
            });
            context.Inventories.Add(new Domain.Inventory.Inventory { StoreId = StoreId, VariantId = product.VariantId, BatchId = batchId, Quantity = units * Quantity.Scale, UpdatedAt = Moment });
            context.SaveChanges();
            return batchId;
        }
    }

    /// <summary>A product, its one variant, and the code on the shelf. Distinct ids on purpose:
    /// the basket carries the product, the sale's rows carry the variant.</summary>
    private sealed record Product(string Id, string VariantId, string Barcode);

    /// <summary>Tier 2's stub, remembering what it was handed (D-065).</summary>
    private sealed class RecordingTier2Writer : ITier2Writer
    {
        public List<Tier2Sale> Recorded { get; } = [];

        public void Record(Tier2Sale sale) => Recorded.Add(sale);
    }

    private async Task<CompletedSale> Sell(Shop shop, params (Product Product, int Count)[] lines) =>
        await Sell(shop, new NullTier2Writer(), lines);

    private async Task<CompletedSale> Sell(Shop shop, ITier2Writer tier2, params (Product Product, int Count)[] lines)
    {
        await using var context = database.NewContext(storeId: shop.StoreId);
        var clock = new FixedClock();
        var calendar = new FixedCalendar();
        var ids = new UlidGenerator();
        var unitOfWork = new WaymarkUnitOfWork(context);
        var executor = new CommandExecutor(unitOfWork, ids, new ProcessingLogWriter(context, ids, new FixedCurrentStore(shop.StoreId), clock));
        var handler = new CompleteSaleHandler(
            new ProductLookup(context, calendar),
            new SalesLedger(context),
            unitOfWork,
            new OutboxSequence(context),
            tier2,
            calendar,
            clock,
            new Waymark.Persistence.Reference.ReasonCodes(context), TabChargesFor.Context(context));

        return await executor.ExecuteAsync(
            handler,
            new CompleteSale(shop.TerminalId, shop.StaffId, [.. lines.Select(line => new SaleLineRequest(line.Product.Barcode, line.Count))]));
    }

    /// <summary>A sale of these lines, and a discount on the whole ticket if any (B4).</summary>
    private async Task<CompletedSale> Sell(Shop shop, GivenDiscount? ticket, params SaleLineRequest[] lines) =>
        await Execute(shop, new CompleteSale(shop.TerminalId, shop.StaffId, lines, ticket));

    /// <summary>A sale paid with card and BaridiMob parts, the rest in cash (B6).</summary>
    private async Task<CompletedSale> Paid(Shop shop, IReadOnlyList<GivenTender> tenders, params (Product Product, int Count)[] lines) =>
        await Execute(shop, new CompleteSale(
            shop.TerminalId, shop.StaffId, [.. lines.Select(line => new SaleLineRequest(line.Product.Barcode, line.Count))], Tenders: tenders));

    private async Task<CompletedSale> Execute(Shop shop, CompleteSale command)
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

        return await executor.ExecuteAsync(handler, command);
    }

    private WaymarkDbContext Read(Shop shop) => database.NewContext(storeId: shop.StoreId);

    private static Money Dzd(long minorUnits) => Money.FromMinorUnits(minorUnits, Currency.Dzd);

    // ------------------------------------------------------- all together

    [Fact]
    public async Task A_cash_sale_writes_every_row_it_needs()
    {
        var shop = new Shop(database);
        var milkBatch = shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);

        var sale = await Sell(shop, (shop.Milk, 2), (shop.Bread, 1));

        using var read = Read(shop);
        var transaction = await read.Transactions.SingleAsync(t => t.TransactionId == sale.TransactionId);
        Assert.Equal(TransactionStatus.Completed, transaction.Status);
        Assert.Equal(Rounding.HalfUp, transaction.RoundingPolicy);
        Assert.Equal(shop.TerminalId, transaction.TerminalId);
        Assert.Equal(sale.InvoiceNumber, transaction.InvoiceNumber);
        Assert.NotNull(transaction.CashSessionId);

        var items = await read.TransactionItems.Where(i => i.TransactionId == sale.TransactionId).ToListAsync();
        Assert.Equal(2, items.Count);
        var milk = items.Single(i => i.VariantId == shop.Milk.VariantId);
        Assert.Equal(2 * Quantity.Scale, milk.Quantity);
        Assert.Equal(Dzd(14_300), milk.SellPrice);
        Assert.Equal(Dzd(10_000), milk.UnitCostAtSale);
        Assert.Equal(milkBatch, milk.BatchId);

        var movements = await read.StockMovements.Where(m => m.ReferenceId == sale.TransactionId).ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.All(movements, m => Assert.Equal(StockMovementType.Sale, m.MovementType));
        Assert.Equal(-2 * Quantity.Scale, movements.Single(m => m.VariantId == shop.Milk.VariantId).QuantityChanged);

        var level = await read.Inventories.SingleAsync(i => i.BatchId == milkBatch);
        Assert.Equal(8 * Quantity.Scale, level.Quantity);

        var payment = await read.TransactionPayments.SingleAsync(p => p.TransactionId == sale.TransactionId);
        Assert.Equal(PaymentMethod.Cash, payment.PaymentMethod);
        Assert.Equal(transaction.TotalAmount, payment.Amount);
    }

    [Fact]
    public async Task A_refused_line_writes_nothing_at_all()
    {
        // The second line's code is unknown. The first line was already priced, its batch
        // found, maybe its session opened: none of that may reach the database.
        var shop = new Shop(database);
        var milkBatch = shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        await Assert.ThrowsAsync<SaleRefusedException>(() =>
            Sell(shop, (shop.Milk, 2), (new Product("nothing", "nothing", "0000000000000"), 1)));

        using var read = Read(shop);
        Assert.Equal(0, await read.Transactions.CountAsync());
        Assert.Equal(0, await read.TransactionItems.CountAsync(i => i.VariantId == shop.Milk.VariantId));
        Assert.Equal(0, await read.StockMovements.CountAsync());
        Assert.Equal(0, await read.CashSessions.CountAsync());
        Assert.Equal(0, await read.RoundingVariances.CountAsync());
        Assert.Equal(10 * Quantity.Scale, (await read.Inventories.SingleAsync(i => i.BatchId == milkBatch)).Quantity);
    }

    [Theory]
    [InlineData(SaleLimits.MaxUnitsPerLine + 1)]
    [InlineData(int.MaxValue)]
    public async Task A_line_of_more_units_than_a_till_can_hold_is_refused_and_nothing_is_written(int count)
    {
        // Block B review: the till stops at 9 999 and the server did not, so one request sold
        // 2 147 483 647 units of milk for 307 thousand million dinars, and took the level that far below zero.
        var shop = new Shop(database);
        var batch = shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        await Assert.ThrowsAsync<SaleRefusedException>(() => Sell(shop, (shop.Milk, count)));

        using var read = Read(shop);
        Assert.Equal(0, await read.Transactions.CountAsync());
        Assert.Equal(10 * Quantity.Scale, (await read.Inventories.SingleAsync(i => i.BatchId == batch)).Quantity);
    }

    [Fact]
    public async Task The_most_a_till_can_hold_is_still_sold()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        var sale = await Sell(shop, (shop.Milk, SaleLimits.MaxUnitsPerLine));

        Assert.Equal(Dzd(14_300L * SaleLimits.MaxUnitsPerLine), sale.Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(SaleLimits.MaxUnitsPerLine + 1)]
    public async Task A_struck_line_of_no_unit_or_too_many_is_refused_as_a_line_would_be(int count)
    {
        // A struck line is a row too: a count of nothing would reach the CHECK on quantity as an
        // error at commit, mid-sale, rather than as an answer.
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        await Assert.ThrowsAsync<SaleRefusedException>(() => Execute(shop, new CompleteSale(
            shop.TerminalId, shop.StaffId, [new SaleLineRequest(shop.Milk.Barcode, 1)],
            Removed: [new RemovedLine(shop.Bread.Barcode, count, null, Now)])));

        using var read = Read(shop);
        Assert.Equal(0, await read.Transactions.CountAsync());
    }

    // ------------------------------------------------------------ the money

    [Fact]
    public async Task The_totals_are_the_lines_and_each_line_splits_its_own_tva()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);

        var sale = await Sell(shop, (shop.Milk, 2), (shop.Bread, 1));

        using var read = Read(shop);
        var transaction = await read.Transactions.SingleAsync(t => t.TransactionId == sale.TransactionId);
        var items = await read.TransactionItems.Where(i => i.TransactionId == sale.TransactionId).ToListAsync();

        // 2 × 143.00 at 9% and 1 × 120.50 at 19%, TVA extracted from the TTC line (D-033).
        var milk = items.Single(i => i.VariantId == shop.Milk.VariantId);
        var bread = items.Single(i => i.VariantId == shop.Bread.VariantId);
        Assert.Equal(Dzd(28_600), milk.LineTotal);
        Assert.Equal(Dzd(28_600).SplitTaxInclusive(BasisPoints.ReducedVat, Rounding.HalfUp).Tax, milk.TaxAmount);
        Assert.Equal(Dzd(12_050), bread.LineTotal);
        Assert.Equal(Dzd(12_050).SplitTaxInclusive(BasisPoints.StandardVat, Rounding.HalfUp).Tax, bread.TaxAmount);

        Assert.Equal(Dzd(40_650), transaction.TotalAmount);
        Assert.Equal(milk.TaxAmount + bread.TaxAmount, transaction.TaxTotal);
        Assert.Equal(transaction.TotalAmount, transaction.Subtotal + transaction.TaxTotal);
        Assert.Equal(sale.Total, transaction.TotalAmount);
    }

    [Fact]
    public async Task The_cash_step_goes_to_rounding_variance_and_the_payment_stays_exact()
    {
        // 406.50 DZD is not on the 5 DZD step: the customer hands over a rounded amount, and
        // the difference is recorded, never hidden in the payment or the drawer (D-034).
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);

        var sale = await Sell(shop, (shop.Milk, 2), (shop.Bread, 1));

        var expected = Dzd(40_650).ToCashTender();
        Assert.True(expected.HasVariance);
        Assert.Equal(expected, sale.Cash);

        using var read = Read(shop);
        var variance = await read.RoundingVariances.SingleAsync(v => v.ReferenceId == sale.TransactionId);
        Assert.Equal(expected.Variance, variance.Amount);
        Assert.Equal(VarianceSource.CashTender, variance.Source);
        Assert.Equal(Dzd(40_650), (await read.TransactionPayments.SingleAsync(p => p.TransactionId == sale.TransactionId)).Amount);
    }

    // ------------------------------------------------------- split tender (B6, D-095)

    [Fact]
    public async Task A_split_writes_each_part_then_the_cash_rest_and_only_the_rest_rounds()
    {
        // 406,50: 200,00 by card and 100,00 by BaridiMob leave 106,50, and 105,00 is collected. The
        // parts are never rounded: the terminal took 200,00, and the row says 200,00.
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);

        var sale = await Paid(shop,
            [new GivenTender(PaymentMethod.Card, 20_000, "4417"), new GivenTender(PaymentMethod.MobileWallet, 10_000, " 88213 ")],
            (shop.Milk, 2), (shop.Bread, 1));

        using var read = Read(shop);
        var rows = await read.TransactionPayments.Where(p => p.TransactionId == sale.TransactionId).OrderBy(p => p.Sequence).ToListAsync();
        Assert.Equal(
            [(1, PaymentMethod.Card, Dzd(20_000), "4417"), (2, PaymentMethod.MobileWallet, Dzd(10_000), "88213"), (3, PaymentMethod.Cash, Dzd(10_650), null)],
            rows.Select(row => (row.Sequence, row.PaymentMethod, row.Amount, row.Reference)));
        Assert.Equal(new CashTender(Dzd(10_500), Dzd(-150)), sale.Cash);
        Assert.Equal(Dzd(-150), (await read.RoundingVariances.SingleAsync(v => v.ReferenceId == sale.TransactionId)).Amount);
    }

    [Fact]
    public async Task Parts_that_pay_the_whole_ticket_leave_no_cash_row_and_no_variance()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);

        var sale = await Paid(shop, [new GivenTender(PaymentMethod.Card, 40_650, null)], (shop.Milk, 2), (shop.Bread, 1));

        using var read = Read(shop);
        Assert.Equal(PaymentMethod.Card, (await read.TransactionPayments.SingleAsync(p => p.TransactionId == sale.TransactionId)).PaymentMethod);
        Assert.False(await read.RoundingVariances.AnyAsync(v => v.ReferenceId == sale.TransactionId));
    }

    [Fact]
    public async Task A_card_for_more_than_the_ticket_is_refused_and_spends_no_invoice_number()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        var before = await Sell(shop, (shop.Milk, 1));
        await Assert.ThrowsAsync<SaleRefusedException>(() => Paid(shop, [new GivenTender(PaymentMethod.Card, 14_301, null)], (shop.Milk, 1)));
        var after = await Sell(shop, (shop.Milk, 1));

        Assert.Equal(NumberOf(before.InvoiceNumber) + 1, NumberOf(after.InvoiceNumber));
        using var read = Read(shop);
        Assert.Equal(2, await read.Transactions.CountAsync());
    }

    private static long NumberOf(string invoice) => long.Parse(invoice[(invoice.LastIndexOf('-') + 1)..], System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_reference_that_reads_as_a_card_number_refuses_the_sale_and_is_written_nowhere()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        await Assert.ThrowsAsync<SaleRefusedException>(() =>
            Paid(shop, [new GivenTender(PaymentMethod.Card, 10_000, "4970 1012 3456 7893")], (shop.Milk, 1)));

        using var read = Read(shop);
        Assert.False(await read.TransactionPayments.AnyAsync());
        Assert.False(await read.Transactions.AnyAsync());
    }

    [Fact]
    public async Task A_total_on_the_cash_step_records_no_variance()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        // 5 × 143.00 = 715.00, on the 5 DZD step.
        var sale = await Sell(shop, (shop.Milk, 5));

        Assert.Equal(Dzd(71_500), sale.Total);
        Assert.False(sale.Cash.HasVariance);
        using var read = Read(shop);
        Assert.Equal(0, await read.RoundingVariances.CountAsync());
    }

    // ---------------------------------------------------- invoice, session

    [Fact]
    public async Task Invoice_numbers_have_no_gap_even_after_a_refused_sale()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        var first = await Sell(shop, (shop.Milk, 1));
        await Assert.ThrowsAsync<SaleRefusedException>(() => Sell(shop, (new Product("x", "x", "0000000000000"), 1)));
        var second = await Sell(shop, (shop.Milk, 1));

        Assert.Equal($"{shop.StoreCode}-2026-000001", first.InvoiceNumber);
        Assert.Equal($"{shop.StoreCode}-2026-000002", second.InvoiceNumber);
    }

    [Fact]
    public async Task The_first_sale_opens_the_terminals_session_and_later_ones_reuse_it()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        var first = await Sell(shop, (shop.Milk, 1));
        var second = await Sell(shop, (shop.Milk, 1));

        using var read = Read(shop);
        var session = await read.CashSessions.SingleAsync();
        Assert.Equal(CashSessionStatus.Open, session.Status);
        Assert.Equal(shop.StaffId, session.OpenedBy);
        var sessions = await read.Transactions.Select(t => t.CashSessionId).Distinct().ToListAsync();
        Assert.Equal([session.SessionId], sessions);
        Assert.NotEqual(first.TransactionId, second.TransactionId);
    }

    // ------------------------------------------------------------ batches

    [Fact]
    public async Task A_line_takes_the_oldest_batch_first_and_splits_when_it_runs_out()
    {
        var shop = new Shop(database);
        var old = shop.Receive(database, shop.Milk, 1, daysAgo: 20);
        var fresh = shop.Receive(database, shop.Milk, 5, daysAgo: 2);

        var sale = await Sell(shop, (shop.Milk, 3));

        using var read = Read(shop);
        var items = await read.TransactionItems.Where(i => i.TransactionId == sale.TransactionId).ToListAsync();
        Assert.Equal(1 * Quantity.Scale, items.Single(i => i.BatchId == old).Quantity);
        Assert.Equal(2 * Quantity.Scale, items.Single(i => i.BatchId == fresh).Quantity);
        Assert.Equal(0, (await read.Inventories.SingleAsync(i => i.BatchId == old)).Quantity);
        Assert.Equal(3 * Quantity.Scale, (await read.Inventories.SingleAsync(i => i.BatchId == fresh)).Quantity);
        Assert.Equal(Dzd(42_900), sale.Total);
    }

    [Fact]
    public async Task Selling_more_than_recorded_takes_the_level_below_zero_rather_than_refusing()
    {
        var shop = new Shop(database);
        var batch = shop.Receive(database, shop.Milk, 1, daysAgo: 5);

        await Sell(shop, (shop.Milk, 3));

        using var read = Read(shop);
        Assert.Equal(-2 * Quantity.Scale, (await read.Inventories.SingleAsync(i => i.BatchId == batch)).Quantity);
    }

    [Fact]
    public async Task A_product_never_received_is_refused_with_nothing_written()
    {
        var shop = new Shop(database);

        var refusal = await Assert.ThrowsAsync<SaleRefusedException>(() => Sell(shop, (shop.Milk, 1)));

        Assert.Contains("never been received", refusal.Message, StringComparison.Ordinal);
        using var read = Read(shop);
        Assert.Equal(0, await read.Transactions.CountAsync());
    }

    // ----------------------------------------------------------- the outbox

    /// <summary>
    /// The stub cloud of hop 5: it reads what the store put out, parses it as the contract
    /// says, and acknowledges it by deleting the row (sync-design §2.3: only after the ack).
    /// The real transport is Phase 4; what this proves is that what leaves can be read by the
    /// other side and carries nothing it should not.
    ///
    /// <para>
    /// <b>The outbox is the database's, not a store's</b> — the table has no <c>store_id</c>,
    /// because one store database is one store's. These tests share a database, so this drain
    /// takes only the messages of the shop that asked; a real drain takes everything pending.
    /// </para>
    /// </summary>
    private sealed class StubCloud(MigratedDatabaseFixture database, Shop shop)
    {
        public List<AnonymousBasketRecord> Received { get; } = [];

        public async Task DrainAsync()
        {
            await using var context = database.NewContext(storeId: shop.StoreId);
            var pending = await context.Outbox
                .Where(message => message.PayloadJson.Contains(shop.StoreId))
                .OrderBy(message => message.SequenceNumber)
                .ToListAsync();

            foreach (var message in pending)
            {
                Assert.Equal(OutboxMessageChannel.AStatistics, message.Channel);
                Assert.Equal(AnonymousBasket.MessageType, message.MessageType);
                Received.Add(JsonSerializer.Deserialize<AnonymousBasketRecord>(message.PayloadJson)
                    ?? throw new InvalidOperationException("The payload could not be read."));
                context.Outbox.Remove(message);
            }

            await context.SaveChangesAsync();
        }
    }

    /// <summary>The sequence numbers this shop's baskets were given, in order.</summary>
    private async Task<List<long>> SequencesOf(Shop shop)
    {
        await using var context = database.NewContext(storeId: shop.StoreId);
        return await context.Outbox
            .Where(message => message.PayloadJson.Contains(shop.StoreId))
            .OrderBy(message => message.SequenceNumber)
            .Select(message => message.SequenceNumber)
            .ToListAsync();
    }

    [Fact]
    public async Task The_sale_puts_one_basket_in_the_outbox_and_the_stub_cloud_can_read_it()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        shop.Receive(database, shop.Bread, 10, daysAgo: 5);

        var sale = await Sell(shop, (shop.Milk, 2), (shop.Bread, 1));

        var cloud = new StubCloud(database, shop);
        await cloud.DrainAsync();

        var basket = Assert.Single(cloud.Received);
        Assert.Equal(shop.StoreId, basket.StoreId);
        Assert.Equal(Today, basket.Date);
        Assert.Equal(10, basket.HourBucket);
        Assert.Equal((int)Today.DayOfWeek, basket.DayOfWeek);
        Assert.Equal("cash", basket.PaymentClass);
        Assert.False(basket.HasDiscount);
        Assert.Equal(
            [(shop.Bread.Id, "1", "120.50"), (shop.Milk.Id, "2", "286.00")],
            basket.Lines.Select(line => (line.ProductId, line.Quantity, line.LineValue)).Order());

        // Nothing joins the basket back to the sale.
        Assert.NotEqual(sale.TransactionId, basket.BasketId);

        using var read = Read(shop);
        Assert.Equal(0, await read.Outbox.CountAsync(message => message.PayloadJson.Contains(shop.StoreId)));
    }

    [Fact]
    public async Task Tier_2_is_handed_the_sale_even_though_the_skeleton_keeps_nothing()
    {
        // The hop is wired (D-065): Phase 2 replaces the writer, not the call.
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        var tier2 = new RecordingTier2Writer();

        var sale = await Sell(shop, tier2, (shop.Milk, 2));

        var recorded = Assert.Single(tier2.Recorded);
        Assert.Equal(sale.TransactionId, recorded.TransactionId);
        Assert.Equal(Today, recorded.Date);
        Assert.Equal(10, recorded.HourOfDay);
        var line = Assert.Single(recorded.Lines);
        Assert.Equal(shop.Milk.VariantId, line.VariantId);
        Assert.Equal(2 * Quantity.Scale, line.Quantity.Thousandths);
    }

    [Fact]
    public async Task The_basket_names_nothing_of_the_till_that_sold_it()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        var sale = await Sell(shop, (shop.Milk, 1));

        using var read = Read(shop);
        var message = await read.Outbox.SingleAsync(m => m.PayloadJson.Contains(shop.StoreId));
        foreach (var identifier in new[] { sale.TransactionId, sale.InvoiceNumber, shop.StaffId, shop.TerminalId, shop.Milk.VariantId })
        {
            Assert.DoesNotContain(identifier, message.PayloadJson, StringComparison.Ordinal);
        }

        // The row itself names no entity either: entity_id would be the transaction.
        Assert.Null(message.EntityId);
        Assert.Null(message.EntityType);
    }

    [Fact]
    public async Task A_refused_sale_puts_nothing_out_and_spends_no_sequence_number()
    {
        // A gap would look to the cloud like a message it never received.
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        await Sell(shop, (shop.Milk, 1));
        await Assert.ThrowsAsync<SaleRefusedException>(() => Sell(shop, (new Product("x", "x", "0000000000000"), 1)));
        await Sell(shop, (shop.Milk, 1));

        // The counter is the database's, shared with the other tests' shops, so what this
        // asserts is that the two sales are consecutive: the refusal spent nothing between them.
        var sequences = await SequencesOf(shop);
        Assert.Equal(2, sequences.Count);
        Assert.Equal(sequences[0] + 1, sequences[1]);
    }

    [Fact]
    public async Task The_basket_and_the_sale_are_written_together_or_not_at_all()
    {
        // The outbox row is staged in the sale's own unit of work (CLAUDE.md §3.6): the two
        // counts move together, and a refused sale moves neither.
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        await Sell(shop, (shop.Milk, 1));
        await Assert.ThrowsAsync<SaleRefusedException>(() => Sell(shop, (shop.Bread, 1)));

        using var read = Read(shop);
        Assert.Equal(1, await read.Transactions.CountAsync());
        Assert.Single(await SequencesOf(shop));
    }

    [Fact]
    public async Task A_products_two_batches_are_one_basket_line()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 1, daysAgo: 20);
        shop.Receive(database, shop.Milk, 5, daysAgo: 2);

        await Sell(shop, (shop.Milk, 3));

        var cloud = new StubCloud(database, shop);
        await cloud.DrainAsync();

        var line = Assert.Single(Assert.Single(cloud.Received).Lines);
        Assert.Equal("3", line.Quantity);
        Assert.Equal("429.00", line.LineValue);
    }

    // ------------------------------------------------------- discounts given at the counter (B4, D-091)

    private const string Gesture = "geste_commercial";

    /// <summary>The shop's discount reasons: the vocabulary is the tenant's, so once per database.</summary>
    private void Reasons()
    {
        using var context = database.NewContext();
        if (!context.ReasonCodes.Any(reason => reason.ReasonCodeValue == Gesture))
        {
            context.ReasonCodes.Add(new ReasonCode
            {
                ReasonCodeValue = Gesture,
                AppliesTo = ReasonCodeAppliesTo.Discount,
                LabelAr = "لفتة تجارية",
                LabelFr = "Geste commercial",
                CreatedAt = Now,
            });
            context.SaveChanges();
        }

        if (!context.ReasonCodes.Any(reason => reason.ReasonCodeValue == Damaged))
        {
            context.ReasonCodes.Add(new ReasonCode
            {
                ReasonCodeValue = Damaged,
                AppliesTo = ReasonCodeAppliesTo.Discount,
                LabelAr = "منتج تالف",
                LabelFr = "Article abîmé",
                CreatedAt = Now,
            });
            context.SaveChanges();
        }
    }

    private const string Damaged = "article_abime";

    private const string ShelfLabel = "etiquette_rayon";

    private const string Other = "autre_precise";

    /// <summary>A price override reason, and a discount reason that asks for a note (B5).</summary>
    private void MoreReasons()
    {
        Reasons();
        using var context = database.NewContext();
        foreach (var (code, kind, note) in new[] { (ShelfLabel, ReasonCodeAppliesTo.PriceOverride, false), (Other, ReasonCodeAppliesTo.Discount, true) })
        {
            if (!context.ReasonCodes.Any(reason => reason.ReasonCodeValue == code))
            {
                context.ReasonCodes.Add(new ReasonCode
                {
                    ReasonCodeValue = code,
                    AppliesTo = kind,
                    LabelAr = code,
                    LabelFr = code,
                    RequiresNote = note,
                    CreatedAt = Now,
                });
            }
        }

        context.SaveChanges();
    }

    private static GivenDiscount Percent(int basisPoints, string by, string reason = Gesture) => new(DiscountForm.Percent, basisPoints, reason, by);

    private static GivenDiscount Amount(long centimes, string by, string reason = Gesture) => new(DiscountForm.Amount, centimes, reason, by);

    private async Task<List<Domain.Sales.TransactionItem>> ItemsOf(Shop shop, CompletedSale sale)
    {
        using var read = Read(shop);
        return await read.TransactionItems.Where(item => item.TransactionId == sale.TransactionId).ToListAsync();
    }

    [Fact]
    public async Task A_line_percent_comes_off_that_line_and_the_row_says_why_and_who()
    {
        Reasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);

        // 3 x 143,00 = 429,00; 10 % is 42,90.
        var sale = await Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 3, Discount: Percent(1_000, shop.StaffId)));

        var item = Assert.Single(await ItemsOf(shop, sale));
        Assert.Equal((Dzd(4_290), Dzd(38_610)), (item.DiscountAmount, item.LineTotal));
        Assert.Equal((Gesture, shop.StaffId), (item.DiscountReasonCode, item.AuthorisedBy));
        Assert.Equal(Dzd(38_610).SplitTaxInclusive(new BasisPoints(900), Rounding.HalfUp).Tax, item.TaxAmount);
        Assert.Equal(Dzd(38_610), sale.Total);

        using var read = Read(shop);
        var row = await read.Transactions.SingleAsync(t => t.TransactionId == sale.TransactionId);
        Assert.Equal(Dzd(4_290), row.DiscountTotal);
    }

    [Fact]
    public async Task A_line_amount_over_two_batches_is_spread_by_their_gross_and_sums_back()
    {
        Reasons();
        var shop = new Shop(database);
        var old = shop.Receive(database, shop.Milk, 1, daysAgo: 20);
        var fresh = shop.Receive(database, shop.Milk, 5, daysAgo: 2);

        // 10,00 over 143,00 and 286,00: 3,33 and 6,67.
        var sale = await Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 3, Discount: Amount(1_000, shop.StaffId)));

        var items = await ItemsOf(shop, sale);
        Assert.Equal(Dzd(333), items.Single(i => i.BatchId == old).DiscountAmount);
        Assert.Equal(Dzd(667), items.Single(i => i.BatchId == fresh).DiscountAmount);
        Assert.Equal(Dzd(41_900), sale.Total);
    }

    [Fact]
    public async Task A_ticket_percent_is_worked_out_on_the_total_and_split_by_line()
    {
        Reasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);
        shop.Receive(database, shop.Bread, 5, daysAgo: 5);

        // 143,00 + 120,50 = 263,50; 10 % is 26,35: 14,30 and 12,05.
        var sale = await Sell(shop, Percent(1_000, shop.StaffId),
            new SaleLineRequest(shop.Milk.Barcode, 1), new SaleLineRequest(shop.Bread.Barcode, 1));

        var items = await ItemsOf(shop, sale);
        Assert.Equal(Dzd(1_430), items.Single(i => i.VariantId == shop.Milk.VariantId).DiscountAmount);
        Assert.Equal(Dzd(1_205), items.Single(i => i.VariantId == shop.Bread.VariantId).DiscountAmount);
        Assert.All(items, item => Assert.Equal(Gesture, item.DiscountReasonCode));
        Assert.Equal(Dzd(23_715), sale.Total);
    }

    [Fact]
    public async Task A_ticket_discount_comes_after_the_lines_own_on_what_they_come_to()
    {
        Reasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);
        shop.Receive(database, shop.Bread, 5, daysAgo: 5);

        // Milk 143,00 less 43,00 is 100,00; bread 120,50. The ticket's 10,00 splits 100,00 : 120,50,
        // so 4,54 and 5,46.
        var sale = await Sell(shop, Amount(1_000, shop.StaffId),
            new SaleLineRequest(shop.Milk.Barcode, 1, Discount: Amount(4_300, shop.StaffId)),
            new SaleLineRequest(shop.Bread.Barcode, 1));

        var items = await ItemsOf(shop, sale);
        Assert.Equal(Dzd(4_300 + 454), items.Single(i => i.VariantId == shop.Milk.VariantId).DiscountAmount);
        Assert.Equal(Dzd(546), items.Single(i => i.VariantId == shop.Bread.VariantId).DiscountAmount);
        Assert.Equal(Dzd(26_350 - 5_300), sale.Total);
    }

    [Fact]
    public async Task A_discounted_sale_is_flagged_in_its_basket_and_a_plain_one_is_not()
    {
        Reasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);
        var cloud = new StubCloud(database, shop);

        await Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 1, Discount: Percent(1_000, shop.StaffId)));
        await Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 1));
        await cloud.DrainAsync();

        Assert.Equal([true, false], cloud.Received.Select(basket => basket.HasDiscount));
    }

    [Theory]
    [InlineData("not_a_reason", true)]
    [InlineData(Gesture, false)]
    public async Task A_discount_with_no_accepted_reason_or_nobody_allowing_it_is_refused_and_nothing_written(string reason, bool allowed)
    {
        Reasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);

        await Assert.ThrowsAsync<SaleRefusedException>(() =>
            Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 1, Discount: Percent(1_000, allowed ? shop.StaffId : string.Empty, reason))));

        using var read = Read(shop);
        Assert.False(await read.Transactions.AnyAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_001)]
    public async Task A_percent_outside_zero_to_a_hundred_is_a_refused_sale(int basisPoints)
    {
        Reasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);

        await Assert.ThrowsAsync<SaleRefusedException>(() =>
            Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 1, Discount: Percent(basisPoints, shop.StaffId))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public async Task A_discount_of_nothing_is_refused_in_words_a_cashier_can_be_shown(long centimes)
    {
        // Block B review: the reason reached the till as the exception's own text, "(Parameter
        // 'discount')" and "Actual value was Amount { Value = 0.00 DZD }" on a second line.
        Reasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);

        var refusal = await Assert.ThrowsAsync<SaleRefusedException>(() =>
            Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 1, Discount: Amount(centimes, shop.StaffId))));

        Assert.DoesNotContain("Parameter", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Actual value", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', refusal.Message);
    }

    [Fact]
    public async Task A_row_with_its_own_discount_and_a_share_of_the_tickets_keeps_its_own_reason()
    {
        Reasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);
        shop.Receive(database, shop.Bread, 5, daysAgo: 5);

        var sale = await Sell(shop, Percent(1_000, shop.StaffId, Gesture),
            new SaleLineRequest(shop.Milk.Barcode, 1, Discount: Amount(1_000, shop.StaffId, Damaged)),
            new SaleLineRequest(shop.Bread.Barcode, 1));

        var items = await ItemsOf(shop, sale);
        Assert.Equal(Damaged, items.Single(i => i.VariantId == shop.Milk.VariantId).DiscountReasonCode);
        Assert.Equal(Gesture, items.Single(i => i.VariantId == shop.Bread.VariantId).DiscountReasonCode);
    }

    // ------------------------------------------------------- price overrides (B5, D-092) and notes (F-28)

    [Fact]
    public async Task An_override_sells_at_the_new_price_and_keeps_the_price_it_replaced()
    {
        MoreReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);

        var sale = await Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 2, Override: new GivenOverride(12_000, ShelfLabel, shop.StaffId)));

        var item = Assert.Single(await ItemsOf(shop, sale));
        Assert.Equal((Dzd(12_000), Dzd(14_300)), (item.SellPrice, item.ListPrice!.Value));
        Assert.Equal((ShelfLabel, shop.StaffId), (item.OverrideReasonCode, item.OverrideAuthorisedBy));
        Assert.Equal(Dzd(24_000), sale.Total);
    }

    [Fact]
    public async Task An_override_up_within_the_band_is_charged()
    {
        MoreReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);

        // 143,00 + 20 % is 171,60.
        var sale = await Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 1, Override: new GivenOverride(17_160, ShelfLabel, shop.StaffId)));

        Assert.Equal(Dzd(17_160), sale.Total);
    }

    [Theory]
    [InlineData(17_161)] // a centime above the band
    [InlineData(0)]
    [InlineData(14_300)] // unchanged
    public async Task An_override_the_band_refuses_is_a_refused_sale_with_nothing_written(long price)
    {
        MoreReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);

        await Assert.ThrowsAsync<SaleRefusedException>(() =>
            Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 1, Override: new GivenOverride(price, ShelfLabel, shop.StaffId))));

        using var read = Read(shop);
        Assert.False(await read.Transactions.AnyAsync());
    }

    [Fact]
    public async Task A_discount_after_an_override_comes_off_the_new_price()
    {
        MoreReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);

        var sale = await Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 1,
            Discount: Percent(1_000, shop.StaffId), Override: new GivenOverride(12_000, ShelfLabel, shop.StaffId)));

        Assert.Equal(Dzd(10_800), sale.Total);
    }

    [Theory]
    [InlineData(Gesture, true)]   // a discount reason is not an override reason
    [InlineData(ShelfLabel, false)] // nobody allowed it
    public async Task An_override_with_no_accepted_reason_or_nobody_allowing_it_is_refused(string reason, bool allowed)
    {
        MoreReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);

        await Assert.ThrowsAsync<SaleRefusedException>(() => Sell(shop, null,
            new SaleLineRequest(shop.Milk.Barcode, 1, Override: new GivenOverride(12_000, reason, allowed ? shop.StaffId : string.Empty))));
    }

    [Fact]
    public async Task A_ticket_discount_is_recorded_on_the_ticket_with_who_allowed_it()
    {
        MoreReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);

        var sale = await Sell(shop, Percent(1_000, shop.StaffId), new SaleLineRequest(shop.Milk.Barcode, 1));

        using var read = Read(shop);
        var row = await read.Transactions.SingleAsync(t => t.TransactionId == sale.TransactionId);
        Assert.Equal((Gesture, shop.StaffId), (row.DiscountReasonCode, row.DiscountAuthorisedBy));
    }

    [Fact]
    public async Task A_reason_that_asks_for_a_note_is_refused_without_one_and_keeps_it_when_written()
    {
        MoreReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);

        await Assert.ThrowsAsync<SaleRefusedException>(() =>
            Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 1, Discount: Percent(1_000, shop.StaffId, Other))));

        var sale = await Sell(shop, null, new SaleLineRequest(shop.Milk.Barcode, 1,
            Discount: Percent(1_000, shop.StaffId, Other) with { Note = "client fidèle" }));
        Assert.Equal("client fidèle", Assert.Single(await ItemsOf(shop, sale)).DiscountNote);
    }
}
