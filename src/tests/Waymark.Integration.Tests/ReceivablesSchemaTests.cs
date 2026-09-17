using Microsoft.Data.Sqlite;

namespace Waymark.Integration.Tests;

/// <summary>
/// The on-account ledger and the return-to-refund link (F-16, F-17), asserted against a migrated
/// database.
///
/// <para>
/// Foreign keys are off except where a test is about one: these are about the CHECKs and the
/// triggers, and satisfying every reference would mean seeding a store. The table is
/// append-only, so every test writes under its own ids and nothing is cleaned up.
/// </para>
/// </summary>
public sealed class ReceivablesSchemaTests : IClassFixture<MigratedDatabaseFixture>
{
    private readonly MigratedDatabaseFixture _database;

    public ReceivablesSchemaTests(MigratedDatabaseFixture database) => _database = database;

    [Theory]
    [InlineData("charge", 1500, "'pay-a'", "NULL", "NULL")]
    [InlineData("charge", -1500, "'pay-b'", "NULL", "NULL")]
    [InlineData("payment", -1500, "NULL", "'cash-a'", "NULL")]
    [InlineData("payment", -1500, "NULL", "NULL", "NULL")]
    [InlineData("write_off", -20, "NULL", "NULL", "'CREANCE-ARRONDI'")]
    [InlineData("adjustment", 300, "NULL", "NULL", "'AJUST-ERREUR'")]
    [InlineData("adjustment", -300, "NULL", "NULL", "'AJUST-ERREUR'")]
    public void A_well_formed_movement_is_accepted(string type, long amount, string paymentId, string cashMovementId, string reason)
    {
        using var connection = _database.Connect(enforceForeignKeys: false);

        Insert(connection, type, amount, paymentId, cashMovementId, reason);
    }

    [Theory]
    [InlineData("payment", 1500, "NULL", "NULL", "NULL", "ck_receivable_movements_sign")]
    [InlineData("write_off", 20, "NULL", "NULL", "'X'", "ck_receivable_movements_sign")]
    [InlineData("charge", 0, "'pay-zero'", "NULL", "NULL", "ck_receivable_movements_amount")]
    [InlineData("charge", 1500, "NULL", "NULL", "NULL", "ck_receivable_movements_payment_id")]
    [InlineData("payment", -1500, "'pay-c'", "NULL", "NULL", "ck_receivable_movements_payment_id")]
    [InlineData("charge", 1500, "'pay-d'", "'cash-b'", "NULL", "ck_receivable_movements_cash_movement_id")]
    [InlineData("write_off", -20, "NULL", "NULL", "NULL", "ck_receivable_movements_reason_code")]
    [InlineData("adjustment", 300, "NULL", "NULL", "NULL", "ck_receivable_movements_reason_code")]
    [InlineData("interest", 300, "NULL", "NULL", "NULL", "ck_receivable_movements_movement_type")]
    public void A_malformed_movement_is_refused(string type, long amount, string paymentId, string cashMovementId, string reason, string constraint)
    {
        using var connection = _database.Connect(enforceForeignKeys: false);

        var error = Assert.Throws<SqliteException>(() => Insert(connection, type, amount, paymentId, cashMovementId, reason));

        Assert.True(
            error.Message.Contains(constraint, StringComparison.Ordinal),
            $"Expected {constraint} to refuse a {type} of {amount}, but the error was: {error.Message}");
    }

    [Fact]
    public void One_payment_row_is_charged_once()
    {
        // A charge mirrors one on-account payment row. Two would double the debt, and the sum of
        // charges would no longer equal the sum of on-account payments.
        using var connection = _database.Connect(enforceForeignKeys: false);
        Insert(connection, "charge", 900, "'pay-twice'", "NULL", "NULL");

        Assert.Throws<SqliteException>(() => Insert(connection, "charge", 900, "'pay-twice'", "NULL", "NULL"));

        // The no-replace trigger refuses first; the unique index stays as the second line.
        Assert.Contains("1:payment_id IS NOT NULL", UniqueIndexes());
    }

    [Fact]
    public void One_cash_movement_repays_once()
    {
        using var connection = _database.Connect(enforceForeignKeys: false);
        Insert(connection, "payment", -900, "NULL", "'cash-twice'", "NULL");

        Assert.Throws<SqliteException>(() => Insert(connection, "payment", -900, "NULL", "'cash-twice'", "NULL"));

        Assert.Contains("1:cash_movement_id IS NOT NULL", UniqueIndexes());
    }

    [Fact]
    public void The_ledger_refuses_an_update_and_a_delete()
    {
        using var connection = _database.Connect(enforceForeignKeys: false);
        var id = Insert(connection, "charge", 700, "'pay-frozen'", "NULL", "NULL");

        var update = Assert.Throws<SqliteException>(() => Execute(connection, $"UPDATE receivable_movements SET amount = 1 WHERE movement_id = '{id}'"));
        var delete = Assert.Throws<SqliteException>(() => Execute(connection, $"DELETE FROM receivable_movements WHERE movement_id = '{id}'"));

        Assert.Contains("append-only", update.Message, StringComparison.Ordinal);
        Assert.Contains("append-only", delete.Message, StringComparison.Ordinal);
        Assert.Equal("700", _database.Scalar($"SELECT amount FROM receivable_movements WHERE movement_id = '{id}'"));
    }

    [Fact]
    public void A_charge_must_point_at_a_real_payment_row()
    {
        using var connection = _database.Connect(enforceForeignKeys: true);

        var error = Assert.Throws<SqliteException>(() => Execute(connection,
            "INSERT INTO receivable_movements (movement_id, store_id, customer_id, movement_type, amount, occurred_at, payment_id) "
            + "VALUES ('fk-case', 'no-store', 'no-customer', 'charge', 100, '2026-09-16T10:00:00Z', 'no-payment')"));

        Assert.Contains("FOREIGN KEY", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_return_can_be_refunded_on_account_and_names_its_refund_line()
    {
        var columns = _database.Query("SELECT name FROM pragma_table_info('returns')");
        var foreignKeys = _database.Query("SELECT \"from\" || '->' || \"table\" || '.' || \"to\" FROM pragma_foreign_key_list('returns')");
        var customerColumns = _database.Query("SELECT name || ':' || \"notnull\" FROM pragma_table_info('customers')");

        Assert.Contains("refund_transaction_item_id", columns);
        Assert.Contains("refund_transaction_item_id->transaction_items.transaction_item_id", foreignKeys);
        Assert.Contains("credit_limit:0", customerColumns);

        using var connection = _database.Connect(enforceForeignKeys: false);
        Execute(connection, """
            INSERT INTO returns (return_id, transaction_item_id, refund_transaction_item_id, store_id, staff_id,
                                 quantity_returned, refund_amount, refund_method, restock_flag, reason_code, created_at)
            VALUES ('on-account-return', 'item-sold', 'item-refunded', 'store', 'staff',
                    1000, 1500, 'on_account', 0, 'RETOUR-AVIS', '2026-09-16T10:00:00Z')
            """);
    }

    /// <summary>Each index on the ledger as <c>unique:filter</c>.</summary>
    private IReadOnlyList<string> UniqueIndexes() => _database.Query("""
        SELECT l."unique" || ':' || substr(s.sql, instr(s.sql, 'WHERE ') + 6)
        FROM pragma_index_list('receivable_movements') l JOIN sqlite_schema s ON s.name = l.name
        WHERE s.sql IS NOT NULL
        """);

    private static string Insert(SqliteConnection connection, string type, long amount, string paymentId, string cashMovementId, string reason)
    {
        var id = "rm-" + Guid.NewGuid().ToString("N");
        Execute(connection, $"""
            INSERT INTO receivable_movements
                (movement_id, store_id, customer_id, movement_type, amount, occurred_at, payment_id, cash_movement_id, reason_code)
            VALUES ('{id}', 'store', 'customer', '{type}', {amount}, '2026-09-16T10:00:00Z', {paymentId}, {cashMovementId}, {reason})
            """);
        return id;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
