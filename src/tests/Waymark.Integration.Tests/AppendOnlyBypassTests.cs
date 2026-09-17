using Microsoft.Data.Sqlite;

namespace Waymark.Integration.Tests;

/// <summary>
/// The append-only guards attacked by every statement SQLite offers besides a plain UPDATE or
/// DELETE (Phase 0 final test). <c>AppendOnlyTests</c> proves the obvious statements are
/// refused; this proves the guards cannot be walked round.
///
/// <para>
/// <c>INSERT OR REPLACE</c> resolves a key conflict by deleting the old row, and SQLite fires no
/// DELETE trigger for that unless <c>recursive_triggers</c> is on. Before the
/// <c>*_no_replace</c> triggers, every ledger could be rewritten in place with one statement
/// from any tool whose SQLite leaves that pragma off.
/// </para>
/// <para>
/// The SQLCipher bundle Waymark ships happens to be built with recursive triggers on by default,
/// which hid the hole inside the application. So every connection here turns them <b>off</b>
/// first: the guard must not depend on how a library was compiled, or on a pragma a future
/// connection path forgets.
/// </para>
/// </summary>
public sealed class AppendOnlyBypassTests : IClassFixture<MigratedDatabaseFixture>
{
    private readonly MigratedDatabaseFixture _database;

    public AppendOnlyBypassTests(MigratedDatabaseFixture database) => _database = database;

    /// <summary>A connection as another SQLite build would open it: recursive triggers off.</summary>
    private SqliteConnection Connect()
    {
        var connection = _database.Connect(enforceForeignKeys: false);
        Execute(connection, "PRAGMA recursive_triggers = OFF");
        Assert.Equal("0", Scalar(connection, "PRAGMA recursive_triggers"));
        return connection;
    }

    [Fact]
    public void The_shipped_SQLite_turns_recursive_triggers_on_by_default_which_is_why_nothing_may_rely_on_it()
    {
        // A pin, not a rule: if a bundle update changes this default, the tests below still hold.
        using var connection = _database.Connect(enforceForeignKeys: false);
        Assert.Contains("DEFAULT_RECURSIVE_TRIGGERS", Scalar(connection, "SELECT group_concat(compile_options, ',') FROM pragma_compile_options"), StringComparison.Ordinal);
    }

    /// <summary>One valid row per append-only table, keyed by <c>{0}</c>, with the column a rewrite would change.</summary>
    public static TheoryData<string, string, string, string> Ledgers() => new()
    {
        {
            "consent_events", "consent_event_id", "action",
            "INSERT {verb} INTO consent_events (consent_event_id, customer_id, occurred_at, action, consent_type, notice_version, method) "
            + "VALUES ('{0}', 'c', '2026-09-17 10:00:00', '{1}', 'marketing', 'v1', 'verbal')"
        },
        {
            "stock_movements", "movement_id", "quantity_changed",
            "INSERT {verb} INTO stock_movements (movement_id, store_id, variant_id, batch_id, movement_date, movement_type, quantity_changed, unit_code, created_at) "
            + "VALUES ('{0}', 's', 'v', 'b', '2026-09-17', 'sale', {2}, 'piece', '2026-09-17 10:00:00')"
        },
        {
            "loyalty_movements", "movement_id", "points",
            "INSERT {verb} INTO loyalty_movements (movement_id, customer_id, movement_type, points, balance_after, occurred_at) "
            + "VALUES ('{0}', 'c', 'earn', {2}, {2}, '2026-09-17 10:00:00')"
        },
        {
            "credit_movements", "movement_id", "amount",
            "INSERT {verb} INTO credit_movements (movement_id, customer_id, movement_type, amount, balance_after, occurred_at) "
            + "VALUES ('{0}', 'c', 'issue', {2}, {2}, '2026-09-17 10:00:00')"
        },
        {
            "receivable_movements", "movement_id", "amount",
            "INSERT {verb} INTO receivable_movements (movement_id, store_id, customer_id, movement_type, amount, occurred_at, payment_id) "
            + "VALUES ('{0}', 's', 'c', 'charge', {2}, '2026-09-17 10:00:00', '{0}-payment')"
        },
        {
            "recommendation_decisions", "decision_id", "decision",
            "INSERT {verb} INTO recommendation_decisions (decision_id, recommendation_id, decision, origin, decided_at, applied_at) "
            + "VALUES ('{0}', 'r', '{3}', 'store', '2026-09-17 10:00:00', '2026-09-17 10:00:00')"
        },
        {
            "erasure_ledger", "erasure_id", "subject_id",
            "INSERT {verb} INTO erasure_ledger (erasure_id, subject_type, subject_id, requested_at, status, created_at) "
            + "VALUES ('{0}', 'customer', 'subject-{1}', '2026-09-17 10:00:00', 'pending', '2026-09-17 10:00:00')"
        },
        {
            "processing_log", "log_id", "purpose",
            "INSERT {verb} INTO processing_log (log_id, occurred_at, operation, subject_type, actor_type, purpose, source_module, legal_basis) "
            + "VALUES ('{0}', '2026-09-17 10:00:00', 'consultation', 'customer', 'staff', 'purpose-{1}', 'tests', 'consent')"
        },
        {
            "rounding_variance", "variance_id", "amount",
            "INSERT {verb} INTO rounding_variance (variance_id, store_id, occurred_at, reference_type, reference_id, source, amount, policy, created_at) "
            + "VALUES ('{0}', 's', '2026-09-17 10:00:00', 'transaction', 't', 'cash_tender', {2}, 'half_up', '2026-09-17 10:00:00')"
        },
    };

    [Theory]
    [MemberData(nameof(Ledgers))]
    public void A_row_cannot_be_rewritten_with_insert_or_replace(string table, string key, string column, string insert)
    {
        using var connection = Connect();
        var id = $"replace-{table}";
        Execute(connection, Row(insert, "", id, first: true));
        var before = Scalar(connection, $"SELECT {column} FROM {table} WHERE {key} = '{id}'");

        var error = Assert.Throws<SqliteException>(() => Execute(connection, Row(insert, "OR REPLACE", id, first: false)));

        Assert.Contains("append-only", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, Scalar(connection, $"SELECT {column} FROM {table} WHERE {key} = '{id}'"));
        Assert.Equal("1", Scalar(connection, $"SELECT count(*) FROM {table} WHERE {key} = '{id}'"));
    }

    [Theory]
    [MemberData(nameof(Ledgers))]
    public void A_row_cannot_be_removed_by_replacing_it_either_way_round(string table, string key, string column, string insert)
    {
        // REPLACE as its own statement, and the upsert form: neither may change what is there.
        using var connection = Connect();
        var id = $"upsert-{table}";
        Execute(connection, Row(insert, "", id, first: true));
        var before = Scalar(connection, $"SELECT {column} FROM {table} WHERE {key} = '{id}'");

        Assert.ThrowsAny<SqliteException>(() => Execute(connection, Row(insert, "", id, first: false).Replace("INSERT  INTO", "REPLACE INTO", StringComparison.Ordinal)));
        Assert.ThrowsAny<SqliteException>(() => Execute(connection, Row(insert, "", id, first: false) + $" ON CONFLICT({key}) DO UPDATE SET {column} = excluded.{column}"));

        Assert.Equal(before, Scalar(connection, $"SELECT {column} FROM {table} WHERE {key} = '{id}'"));
    }

    [Fact]
    public void A_charge_cannot_be_replaced_through_its_unique_payment_link()
    {
        // A conflict on a unique index deletes the conflicting row too, under a new primary key.
        using var connection = Connect();
        Execute(connection, "INSERT INTO receivable_movements (movement_id, store_id, customer_id, movement_type, amount, occurred_at, payment_id) "
            + "VALUES ('by-link-1', 's', 'c', 'charge', 500, '2026-09-17 10:00:00', 'shared-payment')");

        Assert.Throws<SqliteException>(() => Execute(connection,
            "INSERT OR REPLACE INTO receivable_movements (movement_id, store_id, customer_id, movement_type, amount, occurred_at, payment_id) "
            + "VALUES ('by-link-2', 's', 'c', 'charge', 1, '2026-09-17 10:00:00', 'shared-payment')"));

        Assert.Equal("500", Scalar(connection, "SELECT amount FROM receivable_movements WHERE payment_id = 'shared-payment'"));
    }

    [Fact]
    public void Update_or_replace_and_insert_or_ignore_change_nothing()
    {
        using var connection = Connect();
        Execute(connection, "INSERT INTO rounding_variance (variance_id, store_id, occurred_at, reference_type, reference_id, source, amount, policy, created_at) "
            + "VALUES ('quiet', 's', '2026-09-17 10:00:00', 'transaction', 't', 'cash_tender', 200, 'half_up', '2026-09-17 10:00:00')");

        Assert.Throws<SqliteException>(() => Execute(connection, "UPDATE OR REPLACE rounding_variance SET amount = 1 WHERE variance_id = 'quiet'"));
        Assert.Throws<SqliteException>(() => Execute(connection,
            "INSERT OR IGNORE INTO rounding_variance (variance_id, store_id, occurred_at, reference_type, reference_id, source, amount, policy, created_at) "
            + "VALUES ('quiet', 's', '2026-09-17 10:00:00', 'transaction', 't', 'cash_tender', 1, 'half_up', '2026-09-17 10:00:00')"));

        Assert.Equal("200", Scalar(connection, "SELECT amount FROM rounding_variance WHERE variance_id = 'quiet'"));
    }

    [Fact]
    public void A_fresh_row_still_goes_in()
    {
        // The guard refuses a key that exists, not an insert.
        using var connection = Connect();
        foreach (var row in Ledgers())
        {
            Execute(connection, Row((string)row[3], "OR REPLACE", $"fresh-{row[0]}", first: true));
        }
    }

    /// <summary>The insert for <paramref name="id"/>: the first version of the row, or a changed one.</summary>
    private static string Row(string template, string verb, string id, bool first) => template
        .Replace("{verb}", verb, StringComparison.Ordinal)
        .Replace("{0}", id, StringComparison.Ordinal)
        .Replace("{1}", first ? "granted" : "withdrawn", StringComparison.Ordinal)
        .Replace("{2}", first ? "1000" : "-7", StringComparison.Ordinal)
        .Replace("{3}", first ? "accept" : "dismiss", StringComparison.Ordinal);

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString();
    }
}
