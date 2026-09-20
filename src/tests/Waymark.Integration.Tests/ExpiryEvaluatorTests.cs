using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Waymark.Application.Commands;
using Waymark.Application.Engine;
using Waymark.Application.IdGenerator;
using Waymark.Domain;
using Waymark.Domain.Engine;
using Waymark.Domain.Enums;
using Waymark.Domain.Inventory;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;
using Waymark.Domain.Values;
using Waymark.Persistence;
using Waymark.Persistence.Engine;
using Waymark.Persistence.Privacy;
using Wire = Waymark.Contracts.Recommendations;

namespace Waymark.Integration.Tests;

/// <summary>
/// The expiry evaluator on a real SQLite file (hop 6, D-073). The risky rule: <b>it compares
/// and nothing more</b>, and what it writes is a card a human can argue with — a Because block
/// whose every reason is a figure, the window it was judged against, and when it was computed.
/// A card that cannot say why it exists is the one thing this product may not ship.
/// </summary>
public sealed class ExpiryEvaluatorTests(MigratedDatabaseFixture database) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly DateOnly Today = new(2026, 9, 20);
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 6, 15, 0, TimeSpan.Zero);

    private sealed class FixedCalendar : IStoreCalendar
    {
        public DateTimeOffset Now => new(ExpiryEvaluatorTests.Today, new TimeOnly(7, 15), TimeSpan.FromHours(1));

        public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

        public int HourOfDay => Now.Hour;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A store with one product and a shelf to put batches on. Unique ids per test.</summary>
    private sealed class Shop
    {
        private static readonly DateTimeOffset Moment = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

        private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];

        public Shop(MigratedDatabaseFixture database)
        {
            StoreId = $"store-{_suffix}";
            ProductId = $"product-{_suffix}";
            VariantId = $"variant-{_suffix}";

            using var context = database.NewContext();

            // A card names the role allowed to decide it, and that is a foreign key. Both rungs,
            // because which one a card names is read off their ranks (D-073).
            if (!context.Roles.Any())
            {
                context.Roles.Add(new Role { RoleCode = "cashier", Rank = 1, LabelAr = "أمين الصندوق", LabelFr = "Caissier", CreatedAt = Moment });
                context.Roles.Add(new Role { RoleCode = "manager", Rank = 2, LabelAr = "المسؤول", LabelFr = "Responsable", CreatedAt = Moment });
            }

            context.Stores.Add(new Store
            {
                StoreId = StoreId,
                StoreCode = $"S{_suffix[..6]}",
                StoreName = StoreId,
                StoreType = "grocery",
                RoundingPolicy = Rounding.HalfUp,
                CreatedAt = Moment,
                UpdatedAt = Moment,
            });
            context.UnitsOfMeasure.Add(new UnitOfMeasure
            {
                UnitCode = Unit,
                NameAr = "قطعة",
                NameFr = "pièce",
                Dimension = Dimension.Count,
                CreatedAt = Moment,
            });
            context.Products.Add(new Domain.Catalogue.Product
            {
                ProductId = ProductId,
                ProductName = "Lait UHT Candia",
                CreatedAt = Moment,
                UpdatedAt = Moment,
            });
            context.Variants.Add(new Domain.Catalogue.Variant
            {
                VariantId = VariantId,
                ProductId = ProductId,
                VariantName = "1L",
                SellingUnitCode = Unit,
                CreatedAt = Moment,
                UpdatedAt = Moment,
            });
            context.SaveChanges();

            // The window the evaluator compares against, as a store gets it on its first day.
            ColdStartParameters.EnsureNearExpiryWindow(context, Moment);
        }

        public string StoreId { get; }

        public string ProductId { get; }

        public string VariantId { get; }

        public string Unit => $"pc-{_suffix}";

        /// <summary>A batch holding <paramref name="units"/>, expiring <paramref name="expiresInDays"/> from today.</summary>
        public string Receive(
            MigratedDatabaseFixture database,
            long units,
            int? expiresInDays,
            long unitCost = 10_000,
            string? variantId = null,
            BatchStatus status = BatchStatus.Active)
        {
            var batchId = $"batch-{Guid.NewGuid():N}";
            using var context = database.NewContext();
            context.Batches.Add(new Batch
            {
                BatchId = batchId,
                ProductId = ProductId,
                StoreId = StoreId,
                ReceivedDate = Today.AddDays(-30),
                ExpirationDate = expiresInDays is { } days ? Today.AddDays(days) : null,
                Status = status,
                CreatedAt = Moment,
            });
            context.BatchItems.Add(new BatchItem
            {
                BatchId = batchId,
                VariantId = variantId ?? VariantId,
                QuantityReceived = Math.Max(units, 1) * Quantity.Scale,
                UnitCode = Unit,
                UnitCost = Money.FromMinorUnits(unitCost, Currency.Dzd),
                CreatedAt = Moment,
            });
            context.Inventories.Add(new Domain.Inventory.Inventory
            {
                StoreId = StoreId,
                VariantId = variantId ?? VariantId,
                BatchId = batchId,
                Quantity = units * Quantity.Scale,
                UpdatedAt = Moment,
            });
            context.SaveChanges();
            return batchId;
        }
    }

    private async Task<ExpiryEvaluation> Evaluate(Shop shop)
    {
        await using var context = database.NewContext(storeId: shop.StoreId);
        var clock = new FixedClock();
        var ids = new UlidGenerator();
        var store = new FixedCurrentStore(shop.StoreId);
        var unitOfWork = new WaymarkUnitOfWork(context);
        var executor = new CommandExecutor(unitOfWork, ids, new ProcessingLogWriter(context, ids, store, clock));
        var handler = new EvaluateExpiryHandler(
            new ExpiryLedger(context), unitOfWork, store, new FixedCalendar(), clock);

        return await executor.ExecuteAsync(handler, new EvaluateExpiry());
    }

    private WaymarkDbContext Read(Shop shop) => database.NewContext(storeId: shop.StoreId);

    private async Task<List<Recommendation>> Cards(Shop shop)
    {
        using var read = Read(shop);
        return await read.Recommendations.OrderBy(card => card.RecommendationId).ToListAsync();
    }

    // ------------------------------------------------------- what it flags

    [Fact]
    public async Task A_batch_inside_the_window_becomes_a_card_that_explains_itself()
    {
        var shop = new Shop(database);
        var batch = shop.Receive(database, units: 12, expiresInDays: 3, unitCost: 10_000);

        var run = await Evaluate(shop);

        Assert.Equal(1, run.Flagged);
        Assert.Equal(0, run.Superseded);
        Assert.Equal(ColdStartParameters.NearExpiryWindowDays, run.WindowDays);

        var card = Assert.Single(await Cards(shop));
        Assert.Equal(shop.StoreId, card.StoreId);
        Assert.Equal(NearExpiry.RecommendationType, card.RecommendationType);
        Assert.Equal(RecommendationSubjectType.Batch, card.SubjectType);
        Assert.Equal(batch, card.SubjectId);
        Assert.Equal(Department.Inventory, card.Department);
        Assert.Equal(Urgency.Warning, card.Urgency);
        Assert.Equal(RecommendationStatus.Pending, card.Status);
        // The rung above the shop floor, not a role code written into the evaluator (D-073).
        Assert.Equal("manager", card.MinimumRequiredRole);
        Assert.Equal(EvaluateExpiryHandler.Source, card.Source);

        // Which window judged it, and when. A card that cannot be aged is shown as fresh.
        Assert.Equal(1, card.ParameterVersion);
        Assert.Equal(Now, card.ComputedAt);

        // No interval: days on a calendar are a count, not an estimate (D-044).
        Assert.Null(card.IntervalLow);
        Assert.Null(card.IntervalHigh);
    }

    [Fact]
    public async Task Every_reason_on_the_card_is_a_figure_with_its_unit()
    {
        var shop = new Shop(database);
        shop.Receive(database, units: 12, expiresInDays: 3, unitCost: 10_000);

        await Evaluate(shop);

        var card = Assert.Single(await Cards(shop));
        var because = JsonSerializer.Deserialize<Wire.BecauseBlock>(card.BecauseJson);

        Assert.NotNull(because);
        Assert.Equal(EvaluateExpiryHandler.BecauseKey, because.Key);
        Assert.True(because.Factors.Count <= Wire.BecauseBlock.MaxFactors, "A Because block carries at most three reasons (CLAUDE.md §5).");
        Assert.All(because.Factors, factor =>
        {
            Assert.NotEmpty(factor.Value);
            Assert.NotEmpty(factor.Unit);
        });

        Assert.Equal(
            [("days_to_expiry", "3", "days"), ("units_on_hand", "12", shop.Unit), ("value_at_cost", "1200.00", "DZD")],
            because.Factors.Select(factor => (factor.LabelKey, factor.Value, factor.Unit)));

        // The sentence is a rendering of the reasoning, never a substitute for it.
        Assert.Contains("Lait UHT Candia", card.Headline, StringComparison.Ordinal);
        Assert.Contains("3 jours", card.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_card_offers_one_thing_to_do_and_does_none_of_it()
    {
        var shop = new Shop(database);
        var batch = shop.Receive(database, units: 12, expiresInDays: 3);

        await Evaluate(shop);

        using var read = Read(shop);
        var card = Assert.Single(await read.Recommendations.ToListAsync());
        Assert.Equal(ActionType.Binary, card.ActionType);

        var option = Assert.Single(await read.RecommendationOptions.Where(o => o.RecommendationId == card.RecommendationId).ToListAsync());
        var payload = JsonSerializer.Deserialize<Wire.IntentPayload>(option.PayloadJson);
        Assert.NotNull(payload);
        Assert.Equal("apply_markdown", payload.Command);
        Assert.Equal(batch, payload.Arguments["batch_id"]);

        // Nothing decided and nothing applied: no decision row, and the price and the stock
        // are exactly as they were (CLAUDE.md §4).
        Assert.Empty(await read.RecommendationDecisions.ToListAsync());
        Assert.Empty(await read.Prices.ToListAsync());
        Assert.Equal(12 * Quantity.Scale, (await read.Inventories.SingleAsync(level => level.BatchId == batch)).Quantity);
        Assert.Empty(await read.StockMovements.ToListAsync());
    }

    [Fact]
    public async Task A_batch_past_its_date_is_critical_and_is_offered_a_write_off_instead()
    {
        var shop = new Shop(database);
        shop.Receive(database, units: 4, expiresInDays: -2);

        await Evaluate(shop);

        using var read = Read(shop);
        var card = Assert.Single(await read.Recommendations.ToListAsync());
        Assert.Equal(Urgency.Critical, card.Urgency);

        var option = await read.RecommendationOptions.SingleAsync(o => o.RecommendationId == card.RecommendationId);
        var payload = JsonSerializer.Deserialize<Wire.IntentPayload>(option.PayloadJson);
        Assert.NotNull(payload);
        Assert.Equal("write_off_batch", payload.Command);
    }

    // ------------------------------------------------------- what it stays quiet about

    [Fact]
    public async Task A_shelf_with_nothing_wrong_with_it_produces_no_card_at_all()
    {
        // There is no positive state (CLAUDE.md §6): a batch that is fine, one with no shelf
        // life, one already sold out and one withdrawn are all silence, not four green cards.
        var shop = new Shop(database);
        shop.Receive(database, units: 10, expiresInDays: 30);
        shop.Receive(database, units: 10, expiresInDays: null);
        shop.Receive(database, units: 0, expiresInDays: 1);
        shop.Receive(database, units: 10, expiresInDays: 1, status: BatchStatus.Quarantined);

        var run = await Evaluate(shop);

        Assert.Equal(0, run.Flagged);
        Assert.Empty(await Cards(shop));
    }

    [Fact]
    public async Task It_never_looks_at_another_stores_shelf()
    {
        // Cross-tenant leakage is DPIA risk R9, and an engine that judged the neighbour's stock
        // would both leak it and be wrong about this shop (CLAUDE.md §3.3, D-071).
        var mine = new Shop(database);
        var theirs = new Shop(database);
        mine.Receive(database, units: 5, expiresInDays: 2);
        theirs.Receive(database, units: 5, expiresInDays: 2);

        var run = await Evaluate(mine);

        Assert.Equal(1, run.BatchesRead);
        var card = Assert.Single(await Cards(mine));
        Assert.Equal(mine.StoreId, card.StoreId);
        Assert.Empty(await Cards(theirs));
    }

    // ------------------------------------------------------- running it twice

    [Fact]
    public async Task Running_it_again_replaces_the_batchs_card_rather_than_adding_a_second()
    {
        // The board is what a shopkeeper reads in the morning. Two cards about one batch is a
        // reconciliation job nobody asked for (D-044).
        var shop = new Shop(database);
        var batch = shop.Receive(database, units: 6, expiresInDays: 4);

        await Evaluate(shop);
        var second = await Evaluate(shop);

        Assert.Equal(1, second.Flagged);
        Assert.Equal(1, second.Superseded);

        var cards = await Cards(shop);
        Assert.Equal(2, cards.Count);
        var live = Assert.Single(cards, card => card.Status == RecommendationStatus.Pending);
        Assert.Equal(batch, live.SubjectId);
        Assert.Single(cards, card => card.Status == RecommendationStatus.Superseded);
    }

    [Fact]
    public async Task A_batch_that_has_since_sold_out_keeps_its_card_and_gets_no_new_one()
    {
        // Provisional (D-073): the evaluator writes cards, it does not retire them. Whether a
        // card that no longer applies expires or is withdrawn is Phase 1's, and silently
        // deleting a suggestion a human has not answered is the one thing it must not do.
        var shop = new Shop(database);
        var batch = shop.Receive(database, units: 6, expiresInDays: 4);
        await Evaluate(shop);

        using (var sell = database.NewContext(storeId: shop.StoreId))
        {
            var level = await sell.Inventories.SingleAsync(row => row.BatchId == batch);
            sell.Entry(level).Property(row => row.Quantity).CurrentValue = 0;
            await sell.SaveChangesAsync();
        }

        var second = await Evaluate(shop);

        Assert.Equal(0, second.BatchesRead);
        Assert.Equal(0, second.Flagged);
        var card = Assert.Single(await Cards(shop));
        Assert.Equal(RecommendationStatus.Pending, card.Status);
    }

    // ------------------------------------------------------- the placeholder window

    [Fact]
    public void The_cold_start_window_is_installed_once_and_never_repaired()
    {
        // A window the engine or a shopkeeper has since set is theirs. Overwriting it at every
        // start would undo their decision with no error anywhere.
        using var context = database.NewContext();
        ColdStartParameters.EnsureNearExpiryWindow(context, Now);

        Assert.False(ColdStartParameters.EnsureNearExpiryWindow(context, Now));

        var window = context.ParameterRegistry.Single(row =>
            row.ParameterCode == NearExpiry.ParameterCode && row.IsCurrent);
        Assert.Equal(ColdStartParameters.NearExpiryWindowDays, window.ValueNumber);
        Assert.Equal(ParameterRegistryEntrySource.ColdStartDefault, window.Source);

        // It says in words that nobody measured it, so a card judged by it can be read honestly.
        Assert.Contains("placeholder", window.Method, StringComparison.Ordinal);
    }
}

/// <summary>
/// The evaluator on a store that has not been prepared for it. Its own class, so it gets its
/// own database: <c>parameter_registry</c> and <c>roles</c> are the tenant's, not a store's,
/// and what one test installs would be there for every other one.
/// </summary>
public sealed class ExpiryEvaluatorOnAnUnpreparedStoreTests(MigratedDatabaseFixture database)
    : IClassFixture<MigratedDatabaseFixture>
{
    private sealed class FixedCalendar : IStoreCalendar
    {
        public DateTimeOffset Now => new(new DateOnly(2026, 9, 20), new TimeOnly(7, 15), TimeSpan.FromHours(1));

        public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

        public int HourOfDay => Now.Hour;
    }

    [Fact]
    public async Task With_no_window_in_the_registry_it_refuses_and_says_which_parameter_is_missing()
    {
        // Absence is never zero (D-037). A missing window must not be read as "flag nothing",
        // which is indistinguishable from a shop where everything is fresh.
        await using var context = database.NewContext(storeId: "store-without-parameters");
        var clock = new FixedClock();
        var ids = new UlidGenerator();
        var store = new FixedCurrentStore("store-without-parameters");
        var unitOfWork = new WaymarkUnitOfWork(context);
        var executor = new CommandExecutor(unitOfWork, ids, new ProcessingLogWriter(context, ids, store, clock));
        var handler = new EvaluateExpiryHandler(
            new ExpiryLedger(context), unitOfWork, store, new FixedCalendar(), clock);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(handler, new EvaluateExpiry()));

        Assert.Contains(NearExpiry.ParameterCode, refusal.Message, StringComparison.Ordinal);

        using var read = database.NewContext(storeId: "store-without-parameters");
        Assert.Empty(read.Recommendations.ToList());
    }

    [Fact]
    public async Task A_shop_with_two_roles_addresses_its_cards_to_the_owner()
    {
        // "Manager" is a role code one shop uses and another does not. What the card needs is
        // the rung above the shop floor, whatever that shop calls it (D-073) — and naming a role
        // the store has never heard of would fail on the foreign key, which is how this was found.
        var moment = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        await using var context = database.NewContext();
        context.Roles.Add(new Role { RoleCode = "cashier", Rank = 1, LabelAr = "بائع", LabelFr = "Vendeur", CreatedAt = moment });
        context.Roles.Add(new Role { RoleCode = "owner", Rank = 2, LabelAr = "المسير", LabelFr = "Gérant", CreatedAt = moment });
        await context.SaveChangesAsync();

        Assert.Equal("owner", await new ExpiryLedger(context).DecidingRoleAsync());
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 20, 6, 15, 0, TimeSpan.Zero);
    }
}
