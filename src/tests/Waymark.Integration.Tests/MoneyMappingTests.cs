using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Integration.Tests;

/// <summary>
/// Money reaches SQLite as the <c>INTEGER</c> count of minor units the schema
/// declares, and comes back as <see cref="Money"/> (decisions.md D-031, D-035).
///
/// <para>
/// The conversion is applied centrally, by type, in
/// <c>WaymarkDbContext.OnModelCreating</c> — so the test that matters most here
/// is not the round trip but
/// <see cref="Every_money_column_is_mapped_to_the_Money_type"/>: a money column
/// left as a bare <c>long</c> keeps working, keeps passing, and quietly allows
/// the arithmetic the value object exists to prevent.
/// </para>
/// </summary>
public sealed class MoneyMappingTests : IClassFixture<MigratedDatabaseFixture>
{
    private readonly MigratedDatabaseFixture _database;

    public MoneyMappingTests(MigratedDatabaseFixture database) => _database = database;

    private static Money Dzd(long minorUnits) => new(minorUnits, Currency.Dzd);

    /// <summary>
    /// Every column the schema annotates as centimes, plus the ones in a money
    /// group whose comment sits on the first line only. Hand-verified against
    /// <c>schema_v7_1.sql</c>, because the comments alone are not a complete
    /// rule — <c>transactions.total_amount</c> carries no comment at all.
    /// </summary>
    private static readonly string[] MoneyColumns =
    [
        "cash_sessions.opening_float", "cash_sessions.counted_cash",
        "cash_sessions.expected_cash", "cash_sessions.variance",
        "cash_movements.amount",
        "prices.price",
        "suppliers.credit_limit",
        "supplier_variant.purchase_price",
        "purchase_orders.total_amount",
        "purchase_order_items.unit_cost", "purchase_order_items.discount",
        "purchase_order_items.line_total",
        "batch_items.unit_cost",
        "stock_movements.unit_cost",
        "stock_counts.total_variance_value",
        "stock_count_items.unit_cost", "stock_count_items.variance_value",
        "customers.credit",
        "transactions.subtotal", "transactions.discount_total",
        "transactions.tax_total", "transactions.total_amount",
        "transaction_items.sell_price", "transaction_items.unit_cost_at_sale",
        "transaction_items.discount_amount", "transaction_items.tax_amount",
        "transaction_items.line_total",
        "transaction_payments.amount",
        "returns.refund_amount",
        "credit_movements.amount", "credit_movements.balance_after",
        "rounding_variance.amount",
    ];

    [Fact]
    public void Every_money_column_is_mapped_to_the_Money_type()
    {
        using var context = _database.NewContext();

        var mapped = context.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties()
                .Where(property => property.ClrType == typeof(Money) || property.ClrType == typeof(Money?))
                .Select(property => $"{entityType.GetTableName()}.{property.GetColumnName()}"))
            .ToHashSet(StringComparer.Ordinal);

        var unmapped = MoneyColumns.Except(mapped, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            unmapped.Count == 0,
            "These money columns are still a bare long, so nothing stops them being added to "
            + "a quantity or a currency they do not belong to:\n  " + string.Join("\n  ", unmapped));
    }

    [Fact]
    public void Nothing_else_was_dragged_in_by_accident()
    {
        // The other half. A points balance or a basis-point rate wrapped as
        // Money would read as currency on a screen and sum with real money.
        using var context = _database.NewContext();

        var mapped = context.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties()
                .Where(property => property.ClrType == typeof(Money) || property.ClrType == typeof(Money?))
                .Select(property => $"{entityType.GetTableName()}.{property.GetColumnName()}"))
            .ToList();

        var unexpected = mapped.Except(MoneyColumns, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            unexpected.Count == 0,
            "These are typed as Money but are not on the verified list:\n  "
            + string.Join("\n  ", unexpected));
    }

    [Fact]
    public void A_money_column_is_still_an_INTEGER_on_disk()
    {
        // The value object must not have changed the storage. A REAL money
        // column is the canonical silent error (CLAUDE.md §3.1), and STRICT is
        // what makes the rule mechanical.
        var declared = _database.Query(
            "SELECT type FROM pragma_table_info('transactions') WHERE name = 'total_amount'");

        Assert.Equal(["INTEGER"], declared);
    }

    [Fact]
    public void Money_survives_a_round_trip_through_the_database()
    {
        var occurred = new DateTimeOffset(2026, 5, 2, 11, 30, 0, TimeSpan.Zero);

        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            context.CashMovements.Add(new CashMovement
            {
                MovementId = "01MONEYROUNDTRIP",
                SessionId = "session-money",
                MovementType = CashMovementType.PaidIn,
                Amount = Dzd(1_247_55),
                ReasonCode = "cash-in",
                StaffId = "staff-1",
                OccurredAt = occurred
            });
            context.SaveChanges();
        }

        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            var movement = context.CashMovements.Single(m => m.MovementId == "01MONEYROUNDTRIP");

            Assert.Equal(Dzd(1_247_55), movement.Amount);
            Assert.Equal(Currency.Dzd, movement.Amount.Currency);
        }

        // And the integer on disk is the minor units, not a formatted string.
        var stored = _database.Query(
            "SELECT amount FROM cash_movements WHERE movement_id = '01MONEYROUNDTRIP'");

        Assert.Equal(["124755"], stored);
    }

    [Fact]
    public void An_explicitly_set_zero_is_not_replaced_by_the_column_default()
    {
        // D-027's failure, re-checked now that the sentinel has changed.
        // transactions.discount_total is DEFAULT 0, so under the old long
        // sentinel an explicit zero and "unset" were the same value. Money
        // improves on that: default(Money) has no currency at all, so it cannot
        // be confused with a real zero.
        var moment = new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);

        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            context.Transactions.Add(new Transaction
            {
                TransactionId = "01MONEYZERO",
                StoreId = "store-money",
                TerminalId = "terminal-money",
                StaffId = "staff-1",
                OccurredAt = moment,
                RoundingPolicy = Rounding.HalfUp,
                Subtotal = Dzd(0),
                DiscountTotal = Dzd(0),
                TaxTotal = Dzd(0),
                TotalAmount = Dzd(0),
                CreatedAt = moment,
                UpdatedAt = moment
            });
            context.SaveChanges();
        }

        using var reader = _database.NewContext(enforceForeignKeys: false, storeId: "store-money");
        var transaction = reader.Transactions.Single(t => t.TransactionId == "01MONEYZERO");

        Assert.True(transaction.DiscountTotal.IsZero);
        Assert.Equal(Currency.Dzd, transaction.DiscountTotal.Currency);
    }

    [Fact]
    public void Every_currency_column_holds_the_ledger_currency()
    {
        // The guard on D-035's named limitation. The converter builds Money with
        // the ledger currency because the row does not carry one, so a row in
        // another currency would be read back with the right number and the
        // wrong label — the quietest kind of wrong.
        //
        // When this fails, it is not a bug to patch. It is the day Stage 2
        // begins: supplier documents in EUR need their own currency carried
        // through, and this test is the alarm that says so.
        string[] tablesWithCurrency =
        [
            "stores", "prices", "transactions", "transaction_payments",
            "purchase_orders", "suppliers", "supplier_variant", "batch_items",
        ];

        var offenders = new List<string>();

        foreach (var table in tablesWithCurrency)
        {
            foreach (var currency in _database.Query(
                $"SELECT DISTINCT currency FROM {table} WHERE currency <> 'DZD'"))
            {
                offenders.Add($"{table}: {currency}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Rows exist in a currency the ambient converter cannot represent:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nSee decisions.md D-035. This is the Stage 2 boundary, not a bug.");
    }
}
