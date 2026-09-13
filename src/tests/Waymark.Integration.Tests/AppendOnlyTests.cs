using Microsoft.Data.Sqlite;

namespace Waymark.Integration.Tests;

/// <summary>
/// The append-only guarantees, asserted as behaviour.
///
/// <para>
/// <c>consent_events</c> being append-only is a commitment in
/// <c>Waymark_DPIA_v1</c>: withdrawal is a new event, never an update
/// (CLAUDE.md §4). It is enforced by a SQLite trigger, and a trigger is exactly
/// the kind of thing that disappears quietly — <c>DROP TABLE</c> takes a
/// table's triggers with it, and an EF Core table rebuild drops and recreates
/// the table. Reproduced during the D-016 work: a table went through one
/// rebuild migration and came out with its trigger gone, while EF reported
/// success and warned about nothing.
/// </para>
/// <para>
/// So this suite asserts twice over: that each trigger exists by name, and
/// that it actually refuses the write. The second is the one that matters.
/// </para>
/// <para>
/// Note the shape of these tests. The table cannot be cleaned up between them —
/// that is the whole point of it — so each test works under its own customer id
/// and never deletes anything. A test suite against an append-only table has to
/// be append-only too.
/// </para>
/// </summary>
public sealed class AppendOnlyTests : IClassFixture<MigratedDatabaseFixture>
{
    private readonly MigratedDatabaseFixture _schema;

    public AppendOnlyTests(MigratedDatabaseFixture schema) => _schema = schema;

    [Theory]
    [InlineData("trg_consent_events_no_update")]
    [InlineData("trg_consent_events_no_delete")]
    [InlineData("trg_stock_movements_no_update")]
    [InlineData("trg_stock_movements_no_delete")]
    [InlineData("trg_loyalty_movements_no_update")]
    [InlineData("trg_loyalty_movements_no_delete")]
    [InlineData("trg_credit_movements_no_update")]
    [InlineData("trg_credit_movements_no_delete")]
    [InlineData("trg_rec_decisions_no_update")]
    [InlineData("trg_rec_decisions_no_delete")]
    [InlineData("trg_erasure_ledger_no_delete")]
    [InlineData("trg_processing_log_no_update")]
    [InlineData("trg_rounding_variance_no_update")]
    [InlineData("trg_rounding_variance_no_delete")]
    public void Append_only_trigger_is_present(string triggerName)
    {
        var found = _schema.Scalar(
            $"SELECT count(*) FROM sqlite_schema WHERE type = 'trigger' AND name = '{triggerName}'");

        Assert.True(found == "1",
            $"The append-only trigger '{triggerName}' is missing. An EF table "
            + "rebuild drops triggers it does not know about (decisions.md D-016, "
            + "cost 5). If a migration has just run, that is where to look.");
    }

    [Fact]
    public void Consent_events_refuses_an_update()
    {
        using var connection = Grant("update-case");

        var error = Assert.Throws<SqliteException>(() => Execute(connection,
            "UPDATE consent_events SET action = 'withdrawn' WHERE customer_id = 'update-case'"));

        Assert.Contains("append-only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Consent_events_refuses_a_delete()
    {
        using var connection = Grant("delete-case");

        var error = Assert.Throws<SqliteException>(() => Execute(connection,
            "DELETE FROM consent_events WHERE customer_id = 'delete-case'"));

        Assert.Contains("append-only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_granted_row_survives_a_refused_update()
    {
        // A trigger that aborts but leaves the row altered would be worse than
        // no trigger, so check the row is untouched afterwards.
        using var connection = Grant("survives-case");

        Assert.Throws<SqliteException>(() => Execute(connection,
            "UPDATE consent_events SET action = 'withdrawn' WHERE customer_id = 'survives-case'"));

        Assert.Equal("granted", ScalarOn(connection,
            "SELECT action FROM consent_events WHERE customer_id = 'survives-case'"));
    }

    [Fact]
    public void Withdrawal_is_recorded_as_a_second_event()
    {
        // The shape the DPIA requires: the grant stays, the withdrawal is
        // appended beside it, and the history reads in order.
        using var connection = Grant("withdrawal-case");
        Append(connection, "withdrawal-case", "withdrawn", "2026-02-01T00:00:00Z");

        Assert.Equal("granted,withdrawn", ScalarOn(connection, """
            SELECT group_concat(action, ',') FROM (
                SELECT action FROM consent_events
                WHERE customer_id = 'withdrawal-case' ORDER BY occurred_at
            )
            """));
    }

    [Fact]
    public void Processing_log_refuses_an_update_and_the_entry_survives()
    {
        // The Art. 41 bis 3 logbook (D-045, DPIA §5.5). An entry the audited
        // party could edit is not evidence of anything.
        using var connection = _schema.Connect(enforceForeignKeys: false);
        LogEntry(connection, "01APPENDONLYLOGUPDATE");

        var error = Assert.Throws<SqliteException>(() => Execute(connection,
            "UPDATE processing_log SET purpose = 'pos_sale' WHERE log_id = '01APPENDONLYLOGUPDATE'"));

        Assert.Contains("append-only", error.Message, StringComparison.Ordinal);
        Assert.Equal("loyalty_lookup", ScalarOn(connection,
            "SELECT purpose FROM processing_log WHERE log_id = '01APPENDONLYLOGUPDATE'"));
    }

    [Fact]
    public void Processing_log_still_allows_the_retention_delete()
    {
        // Deliberately unguarded: after the statutory period, detail is rolled up
        // into processing_counters and dropped (D-045). A DELETE trigger would make
        // the retention sweep impossible.
        using var connection = _schema.Connect(enforceForeignKeys: false);
        LogEntry(connection, "01APPENDONLYLOGDELETE");

        Execute(connection, "DELETE FROM processing_log WHERE log_id = '01APPENDONLYLOGDELETE'");

        Assert.Equal("0", ScalarOn(connection,
            "SELECT count(*) FROM processing_log WHERE log_id = '01APPENDONLYLOGDELETE'"));
    }

    private static void LogEntry(SqliteConnection connection, string logId) =>
        Execute(connection, $"""
            INSERT INTO processing_log
                (log_id, occurred_at, operation, subject_type, actor_type, purpose, legal_basis, source_module)
            VALUES ('{logId}', '2026-09-14T09:00:00Z', 'consultation', 'customer', 'staff',
                    'loyalty_lookup', 'consent', 'tests')
            """);

    /// <summary>
    /// One granted-marketing event under its own customer id. Foreign keys are
    /// off: this suite is about the triggers, and satisfying every reference
    /// would mean seeding half the catalogue.
    /// </summary>
    private SqliteConnection Grant(string customerId)
    {
        var connection = _schema.Connect(enforceForeignKeys: false);
        Append(connection, customerId, "granted", "2026-01-01T00:00:00Z");
        return connection;
    }

    private static void Append(SqliteConnection connection, string customerId, string action, string occurredAt) =>
        Execute(connection, $"""
            INSERT INTO consent_events
                (consent_event_id, customer_id, occurred_at, action, consent_type, notice_version, method)
            VALUES ('{customerId}-{action}', '{customerId}', '{occurredAt}', '{action}', 'marketing', 'v1', 'verbal')
            """);

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string? ScalarOn(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString();
    }
}
