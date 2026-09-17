// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Waymark.Domain;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Customers;
using Waymark.Domain.Engine;
using Waymark.Domain.Inventory;
using Waymark.Domain.Ledgers;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;
using Waymark.Domain.Purchasing;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;
using Waymark.Domain.Sync;
using Waymark.Domain.Values;
using Waymark.Persistence.Configurations;

namespace Waymark.Persistence;

/// <summary>
/// The operational store database, <c>waymark-store.db</c>.
///
/// <para>
/// Options arrive through the constructor rather than being built in
/// <c>OnConfiguring</c>. The database path is configuration, not a constant —
/// <c>ProgramData\Waymark\data</c> on a till and a temporary directory under
/// test (D-013) — and a context that builds its own connection string cannot be
/// pointed anywhere else.
/// </para>
/// <para>
/// This context never holds the tenant key and never computes a pseudonym.
/// Only <c>Waymark.Pseudonymisation</c> does (CLAUDE.md §3.5, D-039).
/// </para>
/// </summary>
public sealed class WaymarkDbContext(
    DbContextOptions<WaymarkDbContext> options,
    ICurrentStore currentStore,
    ILedgerCurrency ledgerCurrency)
    : DbContext(options)
{
    /// <summary>
    /// Read by the global query filters. A property rather than a captured
    /// local, because EF compiles the filter expression once and re-reads this
    /// on every query — capturing the value would freeze whichever store was
    /// current when the model was first built.
    /// </summary>
    private string? CurrentStoreId => currentStore.StoreId;

    /// <summary>
    /// The ledger currency's code. Read by <see cref="WaymarkModelCacheKeyFactory"/>
    /// so a model built for one currency is never handed to a context using
    /// another.
    /// </summary>
    internal string LedgerCurrencyCode => ledgerCurrency.Currency.Code;

    // Catalogue
    public DbSet<AttributeDefinition> AttributeDefinitions => Set<AttributeDefinition>();
    public DbSet<AttributeOption> AttributeOptions => Set<AttributeOption>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryAttributeLink> CategoryAttributes => Set<CategoryAttributeLink>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<ProductCategory> ProductCategory => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<VariantAttributeValue> VariantAttributeValues => Set<VariantAttributeValue>();
    public DbSet<Variant> Variants => Set<Variant>();

    // Customers
    public DbSet<ConsentEvent> ConsentEvents => Set<ConsentEvent>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<DataSubjectRequest> DataSubjectRequests => Set<DataSubjectRequest>();
    public DbSet<ErasureLedgerEntry> ErasureLedger => Set<ErasureLedgerEntry>();
    public DbSet<ProcessingLogEntry> ProcessingLog => Set<ProcessingLogEntry>();

    public DbSet<ProcessingCounter> ProcessingCounters => Set<ProcessingCounter>();

    // Engine
    public DbSet<ParameterRegistryEntry> ParameterRegistry => Set<ParameterRegistryEntry>();
    public DbSet<RecommendationDecision> RecommendationDecisions => Set<RecommendationDecision>();
    public DbSet<RecommendationOption> RecommendationOptions => Set<RecommendationOption>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<Role> Roles => Set<Role>();

    // Inventory
    public DbSet<BatchItem> BatchItems => Set<BatchItem>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<StockCountItem> StockCountItems => Set<StockCountItem>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    // Ledgers
    public DbSet<CreditMovement> CreditMovements => Set<CreditMovement>();
    public DbSet<LoyaltyMovement> LoyaltyMovements => Set<LoyaltyMovement>();
    public DbSet<ReceivableMovement> ReceivableMovements => Set<ReceivableMovement>();

    // Organisation
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Terminal> Terminals => Set<Terminal>();

    // Pricing
    public DbSet<Price> Prices => Set<Price>();
    public DbSet<ProductBundleItem> ProductBundleItems => Set<ProductBundleItem>();
    public DbSet<ProductBundle> ProductBundles => Set<ProductBundle>();
    public DbSet<PromotionProduct> PromotionProduct => Set<PromotionProduct>();
    public DbSet<PromotionVariant> PromotionVariant => Set<PromotionVariant>();
    public DbSet<Promotion> Promotions => Set<Promotion>();

    public DbSet<RoundingVariance> RoundingVariances => Set<RoundingVariance>();

    // Purchasing
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<SupplierVariant> SupplierVariant => Set<SupplierVariant>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    // Reference
    public DbSet<NoticeVersion> NoticeVersions => Set<NoticeVersion>();
    public DbSet<ReasonCode> ReasonCodes => Set<ReasonCode>();
    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();
    public DbSet<SchemaMigration> SchemaMigrations => Set<SchemaMigration>();
    public DbSet<StoreEntitlement> StoreEntitlements => Set<StoreEntitlement>();
    public DbSet<SystemConfigEntry> SystemConfig => Set<SystemConfigEntry>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    // Sales
    public DbSet<SalesReturn> Returns => Set<SalesReturn>();
    public DbSet<TransactionItem> TransactionItems => Set<TransactionItem>();
    public DbSet<TransactionPayment> TransactionPayments => Set<TransactionPayment>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    // Sync
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();
    public DbSet<Intent> Intents => Set<Intent>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<SyncState> SyncState => Set<SyncState>();

    /// <summary>
    /// <para>
    /// Removing the convention also makes the rule uniform: every index in this
    /// database is declared, and none appears by itself. If a foreign key turns
    /// out to need one, it is added with <c>HasIndex</c> like any other.
    /// </para>
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Conventions.Remove<ForeignKeyIndexConvention>();

        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Picks up every IEntityTypeConfiguration in this assembly. Adding an
        // entity means adding one file, never editing this method — which is
        // what keeps it short at sixty tables instead of 1,100 lines.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WaymarkDbContext).Assembly);

        ApplyMoney(modelBuilder);
        ApplyStoreScope(modelBuilder);
        ApplyParentScope(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Gives every <see cref="Money"/> property its converter to the
    /// <c>INTEGER</c> count of minor units the schema stores.
    ///
    /// <para>
    /// Central rather than named in each configuration, for the same reason the
    /// store filter is: one rule thirty-odd times is a rule that will be missed
    /// once, and the one it is missed on is a money column stored unwrapped
    /// with nothing to notice.
    /// </para>
    /// <para>
    /// The currency comes from <see cref="ILedgerCurrency"/> because the column
    /// does not carry one, which is also why
    /// <c>WaymarkModelCacheKeyFactory</c> exists: two stores on different
    /// currencies in one process must not share a model built for the first.
    /// </para>
    /// </summary>
    private void ApplyMoney(ModelBuilder modelBuilder)
    {
        var money = new MoneyConverter(ledgerCurrency.Currency);
        var nullableMoney = new NullableMoneyConverter(ledgerCurrency.Currency);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(Money))
                {
                    property.SetValueConverter(money);
                }
                else if (property.ClrType == typeof(Money?))
                {
                    property.SetValueConverter(nullableMoney);
                }
            }
        }
    }

    /// <summary>
    /// Puts every <see cref="IStoreScoped"/> entity behind a store filter
    /// (CLAUDE.md §3.3). A filter rather than a <c>where</c> clause because a
    /// caller can forget a <c>where</c> clause, and cross-tenant leakage is
    /// DPIA risk R9.
    ///
    /// <para>
    /// Applied here by reflection rather than a line in each of the seventeen
    /// configurations. The filter is the same rule seventeen times, and a rule
    /// repeated by hand is a rule that will eventually be missed once.
    /// </para>
    /// </summary>
    private void ApplyStoreScope(ModelBuilder modelBuilder)
    {
        var open = typeof(WaymarkDbContext).GetMethod(
            nameof(FilterByStore), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(IStoreScoped).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var storeId = entityType.FindProperty(nameof(IStoreScoped.StoreId))
                ?? throw new InvalidOperationException(
                    $"{entityType.ClrType.Name} is IStoreScoped but has no mapped StoreId.");

            open.MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [modelBuilder, storeId.IsNullable]);
        }
    }

    /// <summary>
    /// Puts every row that belongs to a store through its parent, behind the same filter (F-19,
    /// D-062).
    ///
    /// <para>
    /// A transaction line has no <c>store_id</c> of its own; it belongs to whichever store its
    /// transaction does. Before this, <c>TransactionItems</c> read through its own set showed
    /// every store's lines. Each child now reads only where its parent is visible, and the
    /// parent's own store filter applies inside that test, so the rule stays in one place.
    /// </para>
    /// <para>
    /// The tables left unfiltered are shared by the whole tenant (the catalogue, suppliers,
    /// customers with their consent and ledgers, reason codes, roles, notices) or belong to the
    /// database itself (the outbox, inbox and sync state). <c>StoreScopingTests</c> lists every
    /// one with its reason, so a new table has to be placed on one side or the other.
    /// </para>
    /// </summary>
    private void ApplyParentScope(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransactionItem>()
            .HasQueryFilter(x => Set<Transaction>().Any(parent => parent.TransactionId == x.TransactionId));
        modelBuilder.Entity<TransactionPayment>()
            .HasQueryFilter(x => Set<Transaction>().Any(parent => parent.TransactionId == x.TransactionId));
        modelBuilder.Entity<CashMovement>()
            .HasQueryFilter(x => Set<CashSession>().Any(parent => parent.SessionId == x.SessionId));
        modelBuilder.Entity<BatchItem>()
            .HasQueryFilter(x => Set<Batch>().Any(parent => parent.BatchId == x.BatchId));
        modelBuilder.Entity<PurchaseOrderItem>()
            .HasQueryFilter(x => Set<PurchaseOrder>().Any(parent => parent.OrderId == x.OrderId));
        modelBuilder.Entity<StockCountItem>()
            .HasQueryFilter(x => Set<StockCount>().Any(parent => parent.CountId == x.CountId));
        modelBuilder.Entity<RecommendationOption>()
            .HasQueryFilter(x => Set<Recommendation>().Any(parent => parent.RecommendationId == x.RecommendationId));
        modelBuilder.Entity<RecommendationDecision>()
            .HasQueryFilter(x => Set<Recommendation>().Any(parent => parent.RecommendationId == x.RecommendationId));

        // A promotion with no store is every store's, and its parent filter already says so.
        modelBuilder.Entity<PromotionProduct>()
            .HasQueryFilter(x => Set<Promotion>().Any(parent => parent.PromotionId == x.PromotionId));
        modelBuilder.Entity<PromotionVariant>()
            .HasQueryFilter(x => Set<Promotion>().Any(parent => parent.PromotionId == x.PromotionId));
    }

    private void FilterByStore<TEntity>(ModelBuilder modelBuilder, bool storeIdIsNullable)
        where TEntity : class, IStoreScoped
    {
        if (storeIdIsNullable)
        {
            // promotions and processing_log allow no store, and those rows mean
            // "every store" — a promotion that is not store-specific, or
            // processing that happened outside one. They stay visible.
            modelBuilder.Entity<TEntity>()
                .HasQueryFilter(e => e.StoreId == null || e.StoreId == CurrentStoreId);
            return;
        }

        // Plain equality where the column is NOT NULL. The nullable form above
        // would read `store_id IS NULL OR store_id = @p`, which costs an index
        // seek on tables like transactions for a branch that can never be true.
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.StoreId == CurrentStoreId);
    }
}
