using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Engine;
using Waymark.Domain.Inventory;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;
using Waymark.Domain.Purchasing;
using Waymark.Domain.Sales;
using Waymark.Persistence;

namespace Waymark.Integration.Tests;

/// <summary>
/// A row with no <c>store_id</c> of its own belongs to its parent's store, and is filtered
/// through it (F-19, D-062). Every table without <c>store_id</c> is placed here on one side or
/// the other, with its reason, so a new table cannot fall through unclassified.
/// </summary>
public sealed class ParentScopeTests : IClassFixture<MigratedDatabaseFixture>
{
    private const string StoreA = "parent-store-a";
    private const string StoreB = "parent-store-b";

    /// <summary>The children filtered through their parent, keyed by table.</summary>
    private static readonly Dictionary<string, Type> ThroughParent = new(StringComparer.Ordinal)
    {
        ["transaction_items"] = typeof(TransactionItem),
        ["transaction_payments"] = typeof(TransactionPayment),
        ["cash_movements"] = typeof(CashMovement),
        ["batch_items"] = typeof(BatchItem),
        ["purchase_order_items"] = typeof(PurchaseOrderItem),
        ["stock_count_items"] = typeof(StockCountItem),
        ["recommendation_options"] = typeof(RecommendationOption),
        ["recommendation_decisions"] = typeof(RecommendationDecision),
        ["promotion_product"] = typeof(PromotionProduct),
        ["promotion_variant"] = typeof(PromotionVariant),
    };

    /// <summary>Tables every store of the tenant shares, or that belong to the database itself.</summary>
    private static readonly Dictionary<string, string> Unscoped = new(StringComparer.Ordinal)
    {
        ["attribute_definitions"] = "catalogue, shared by the tenant",
        ["attribute_options"] = "catalogue",
        ["category_attributes"] = "catalogue",
        ["categories"] = "catalogue",
        ["product_attribute_values"] = "catalogue",
        ["product_bundle_items"] = "catalogue",
        ["product_bundles"] = "catalogue",
        ["product_category"] = "catalogue",
        ["products"] = "catalogue",
        ["variant_attribute_values"] = "catalogue",
        ["variants"] = "catalogue",
        ["units_of_measure"] = "catalogue",
        ["suppliers"] = "suppliers serve the tenant",
        ["supplier_variant"] = "supplier terms, per tenant",
        ["customers"] = "a customer is the tenant's, served at any of its stores",
        ["consent_events"] = "a customer's consent, per tenant",
        ["credit_movements"] = "store credit is spent at any store of the tenant",
        ["loyalty_movements"] = "loyalty is the tenant's",
        ["data_subject_requests"] = "a person's rights run against the tenant, the controller",
        ["erasure_ledger"] = "erasure is of the person, across the tenant",
        ["notice_versions"] = "the tenant's privacy notices",
        ["reason_codes"] = "vocabulary",
        ["roles"] = "vocabulary",
        ["retention_policies"] = "the tenant's retention rules",
        ["parameter_registry"] = "engine parameters",
        ["system_config"] = "installation settings",
        ["store_entitlements"] = "licensing, read before a store is chosen",
        ["schema_migrations"] = "the database's own history",
        ["outbox"] = "this database's queue to the cloud",
        ["inbox"] = "this database's queue from the cloud",
        ["sync_state"] = "this database's sync cursor",
    };

    private readonly MigratedDatabaseFixture _database;

    public ParentScopeTests(MigratedDatabaseFixture database)
    {
        _database = database;
        Seed();
    }

    private void Seed()
    {
        using var connection = _database.Connect(enforceForeignKeys: false);
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"SELECT count(*) FROM transactions WHERE store_id = '{StoreA}'";
            if (Convert.ToInt64(check.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0)
            {
                return;
            }
        }

        const string at = "2026-09-17 10:00:00";
        foreach (var store in new[] { StoreA, StoreB })
        {
            Execute(connection, $"""
                INSERT INTO transactions (transaction_id, created_at, occurred_at, staff_id, store_id, terminal_id, updated_at)
                VALUES ('tx-{store}', '{at}', '{at}', 'staff', '{store}', 'till', '{at}');
                INSERT INTO transaction_items (transaction_item_id, transaction_id, variant_id, quantity, unit_code, sell_price, line_total, created_at)
                VALUES ('item-{store}', 'tx-{store}', 'v', 1000, 'piece', 100, 100, '{at}');
                INSERT INTO transaction_payments (payment_id, transaction_id, sequence, payment_method, amount, created_at)
                VALUES ('pay-{store}', 'tx-{store}', 1, 'cash', 100, '{at}');
                INSERT INTO cash_sessions (session_id, store_id, terminal_id, opened_by, opened_at, created_at, updated_at)
                VALUES ('session-{store}', '{store}', 'till', 'staff', '{at}', '{at}', '{at}');
                INSERT INTO cash_movements (movement_id, session_id, movement_type, amount, reason_code, staff_id, occurred_at)
                VALUES ('cash-{store}', 'session-{store}', 'paid_in', 100, 'R', 'staff', '{at}');
                INSERT INTO batches (batch_id, product_id, store_id, received_date, created_at)
                VALUES ('batch-{store}', 'p', '{store}', '2026-09-17', '{at}');
                INSERT INTO batch_items (batch_id, variant_id, quantity_received, unit_code, unit_cost, created_at)
                VALUES ('batch-{store}', 'v', 1000, 'piece', 50, '{at}');
                INSERT INTO purchase_orders (order_id, store_id, supplier_id, order_date, created_at, updated_at)
                VALUES ('order-{store}', '{store}', 'sup', '2026-09-17', '{at}', '{at}');
                INSERT INTO purchase_order_items (order_id, variant_id, quantity_ordered, purchase_unit_code, unit_cost, line_total)
                VALUES ('order-{store}', 'v', 1000, 'piece', 50, 50);
                INSERT INTO stock_counts (count_id, store_id, count_type, started_by, started_at, created_at, updated_at)
                VALUES ('count-{store}', '{store}', 'spot', 'staff', '{at}', '{at}', '{at}');
                INSERT INTO stock_count_items (count_item_id, count_id, variant_id, batch_id, expected_quantity)
                VALUES ('count-item-{store}', 'count-{store}', 'v', 'batch-{store}', 1000);
                INSERT INTO recommendations (recommendation_id, store_id, department, urgency, action_type, subject_type, subject_id,
                                             headline, because_json, computed_at, source, minimum_required_role, issued_at)
                VALUES ('rec-{store}', '{store}', 'inventory', 'standard', 'menu', 'variant', 'v', 'h', '[]', '{at}', 'cloud', 'cashier', '{at}');
                INSERT INTO recommendation_options (option_id, recommendation_id, label, payload_json)
                VALUES ('option-{store}', 'rec-{store}', 'l', '[]');
                INSERT INTO recommendation_decisions (decision_id, recommendation_id, decision, origin, decided_at)
                VALUES ('decision-{store}', 'rec-{store}', 'dismiss', 'store', '{at}');
                INSERT INTO promotions (promotion_id, promotion_name, promotion_type, store_id, created_at, updated_at)
                VALUES ('promo-{store}', 'p', 'discount', '{store}', '{at}', '{at}');
                INSERT INTO promotion_product (promotion_id, product_id, valid_from, value_type, promotion_value)
                VALUES ('promo-{store}', 'p', '2026-09-17', 'percent', 10);
                INSERT INTO promotion_variant (promotion_id, variant_id, valid_from, value_type, promotion_value)
                VALUES ('promo-{store}', 'v', '2026-09-17', 'percent', 10);
                """);
        }

        // A second child of each kind in store A only, so a filter that swapped the stores, or
        // matched the wrong parent, shows in the counts rather than cancelling out.
        Execute(connection, $"""
            INSERT INTO transaction_items (transaction_item_id, transaction_id, variant_id, quantity, unit_code, sell_price, line_total, created_at)
            VALUES ('item2-{StoreA}', 'tx-{StoreA}', 'v2', 1000, 'piece', 100, 100, '{at}');
            INSERT INTO transaction_payments (payment_id, transaction_id, sequence, payment_method, amount, created_at)
            VALUES ('pay2-{StoreA}', 'tx-{StoreA}', 2, 'card', 100, '{at}');
            INSERT INTO cash_movements (movement_id, session_id, movement_type, amount, reason_code, staff_id, occurred_at)
            VALUES ('cash2-{StoreA}', 'session-{StoreA}', 'drop', 100, 'R', 'staff', '{at}');
            INSERT INTO batch_items (batch_id, variant_id, quantity_received, unit_code, unit_cost, created_at)
            VALUES ('batch-{StoreA}', 'v2', 1000, 'piece', 50, '{at}');
            INSERT INTO purchase_order_items (order_id, variant_id, quantity_ordered, purchase_unit_code, unit_cost, line_total)
            VALUES ('order-{StoreA}', 'v2', 1000, 'piece', 50, 50);
            INSERT INTO stock_count_items (count_item_id, count_id, variant_id, batch_id, expected_quantity)
            VALUES ('count-item2-{StoreA}', 'count-{StoreA}', 'v2', 'batch-{StoreA}', 1000);
            INSERT INTO recommendation_options (option_id, recommendation_id, label, payload_json, display_order)
            VALUES ('option2-{StoreA}', 'rec-{StoreA}', 'l2', '[]', 1);
            INSERT INTO recommendation_decisions (decision_id, recommendation_id, decision, origin, decided_at)
            VALUES ('decision2-{StoreA}', 'rec-{StoreA}', 'accept', 'store', '{at}');
            INSERT INTO promotion_product (promotion_id, product_id, valid_from, value_type, promotion_value)
            VALUES ('promo-{StoreA}', 'p2', '2026-09-17', 'percent', 10);
            INSERT INTO promotion_variant (promotion_id, variant_id, valid_from, value_type, promotion_value)
            VALUES ('promo-{StoreA}', 'v2', '2026-09-17', 'percent', 10);
            """);

        // A promotion for every store: its links are everyone's.
        Execute(connection, $"""
            INSERT INTO promotions (promotion_id, promotion_name, promotion_type, store_id, created_at, updated_at)
            VALUES ('promo-all', 'p', 'discount', NULL, '{at}', '{at}');
            INSERT INTO promotion_product (promotion_id, product_id, valid_from, value_type, promotion_value)
            VALUES ('promo-all', 'p', '2026-09-17', 'percent', 10);
            INSERT INTO promotion_variant (promotion_id, variant_id, valid_from, value_type, promotion_value)
            VALUES ('promo-all', 'v', '2026-09-17', 'percent', 10);
            """);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static TheoryData<string> Children() => [.. ThroughParent.Keys];

    /// <summary>What <paramref name="context"/> sees of <paramref name="entity"/>, and what is there.</summary>
    private static (int Visible, int All) Counts(WaymarkDbContext context, Type entity) =>
        ((int, int))typeof(ParentScopeTests).GetMethod(nameof(CountsOf), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(entity)
            .Invoke(null, [context])!;

    private static (int Visible, int All) CountsOf<T>(WaymarkDbContext context)
        where T : class =>
        (context.Set<T>().Count(), context.Set<T>().IgnoreQueryFilters().Count());

    [Theory]
    [MemberData(nameof(Children))]
    public void A_store_sees_its_own_children_and_no_other_stores(string table)
    {
        var entity = ThroughParent[table];
        var shared = table.StartsWith("promotion_", StringComparison.Ordinal) ? 1 : 0;

        using var a = _database.NewContext(enforceForeignKeys: false, storeId: StoreA);
        using var b = _database.NewContext(enforceForeignKeys: false, storeId: StoreB);
        using var none = _database.NewContext(enforceForeignKeys: false, storeId: null);

        // Store A has two of each, store B one.
        Assert.Equal((2 + shared, 3 + shared), Counts(a, entity));
        Assert.Equal((1 + shared, 3 + shared), Counts(b, entity));

        // Fails closed: with no store chosen, only rows that belong to every store show.
        Assert.Equal((shared, 3 + shared), Counts(none, entity));
    }

    [Fact]
    public void A_line_of_another_store_cannot_be_found_even_by_its_key()
    {
        using var a = _database.NewContext(enforceForeignKeys: false, storeId: StoreA);

        Assert.Null(a.Set<TransactionItem>().SingleOrDefault(i => i.TransactionItemId == $"item-{StoreB}"));
        Assert.NotNull(a.Set<TransactionItem>().SingleOrDefault(i => i.TransactionItemId == $"item-{StoreA}"));
        Assert.Empty(a.Set<TransactionPayment>().Where(p => p.TransactionId == $"tx-{StoreB}"));
    }

    [Fact]
    public void Every_table_without_a_store_is_filtered_through_its_parent_or_declared_shared()
    {
        using var context = _database.NewContext(enforceForeignKeys: false);

        var withoutStore = context.Model.GetEntityTypes()
            .Where(e => e.FindProperty("StoreId") is null)
            .ToDictionary(e => e.GetTableName()!, e => e, StringComparer.Ordinal);

        var unplaced = withoutStore.Keys
            .Where(t => !ThroughParent.ContainsKey(t) && !Unscoped.ContainsKey(t))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(unplaced.Count == 0,
            "These tables have no store_id and nobody has said whose they are. Filter them through their "
            + "parent in WaymarkDbContext.ApplyParentScope, or list them here as shared, with the reason:\n  "
            + string.Join("\n  ", unplaced));

        Assert.All(ThroughParent, entry => Assert.NotEmpty(withoutStore[entry.Key].GetDeclaredQueryFilters()));
        Assert.All(Unscoped.Keys.Where(withoutStore.ContainsKey), table => Assert.Empty(withoutStore[table].GetDeclaredQueryFilters()));
        Assert.Equal(withoutStore.Count, ThroughParent.Count + Unscoped.Keys.Count(withoutStore.ContainsKey));
    }
}
