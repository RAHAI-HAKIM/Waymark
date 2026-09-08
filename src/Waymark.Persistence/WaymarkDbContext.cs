// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.
using Microsoft.EntityFrameworkCore;
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
/// This context does not own <c>waymark-identity.db</c> and never will. Only
/// <c>Waymark.Pseudonymisation</c> holds that path (CLAUDE.md §3.4).
/// </para>
/// </summary>
public sealed class WaymarkDbContext(DbContextOptions<WaymarkDbContext> options)
    : DbContext(options)
{
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Picks up every IEntityTypeConfiguration in this assembly. Adding an
        // entity means adding one file, never editing this method — which is
        // what keeps it short at 58 tables instead of 1,100 lines.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WaymarkDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
