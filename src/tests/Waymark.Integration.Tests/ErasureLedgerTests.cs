using Microsoft.Data.Sqlite;

namespace Waymark.Integration.Tests;

/// <summary>
/// <c>erasure_ledger</c> is the evidence that an erasure was asked for and done (F-18, D-060).
/// Its facts never change; its outcome only moves forward, and an executed erasure is final.
/// </summary>
public sealed class ErasureLedgerTests : IClassFixture<MigratedDatabaseFixture>
{
    private const string Requested = "2026-09-17 09:00:00";
    private const string Executed = "2026-09-17 10:00:00";
    private const string Confirmed = "2026-09-17 11:00:00";

    private readonly MigratedDatabaseFixture _database;

    public ErasureLedgerTests(MigratedDatabaseFixture database) => _database = database;

    /// <summary>A pending erasure under its own id; foreign keys off, as the ledger is the subject here.</summary>
    private (SqliteConnection Connection, string Id) Pending()
    {
        var connection = _database.Connect(enforceForeignKeys: false);
        var id = "erasure-" + Guid.NewGuid().ToString("N");
        Execute(connection, $"""
            INSERT INTO erasure_ledger (erasure_id, subject_type, subject_id, request_id, requested_at, scope_json, status, created_at)
            VALUES ('{id}', 'customer', 'subject-a', 'request-a', '{Requested}', '["customers"]', 'pending', '{Requested}')
            """);
        return (connection, id);
    }

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

    private static void Refused(SqliteConnection connection, string sql, string message)
    {
        var error = Assert.Throws<SqliteException>(() => Execute(connection, sql));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    private static void Execute(SqliteConnection connection, string id, string set) =>
        Execute(connection, $"UPDATE erasure_ledger SET {set} WHERE erasure_id = '{id}'");

    [Theory]
    [InlineData("subject_type = 'staff'")]
    [InlineData("subject_id = 'someone-else'")]
    [InlineData("subject_id = NULL")]
    [InlineData("request_id = 'request-b'")]
    [InlineData("request_id = NULL")]
    [InlineData("requested_at = '2026-01-01 00:00:00'")]
    [InlineData("scope_json = '{}'")]
    [InlineData("created_at = '2026-01-01 00:00:00'")]
    [InlineData("erasure_id = 'renamed'")]
    public void The_facts_of_an_erasure_never_change(string set)
    {
        var (connection, id) = Pending();
        using (connection)
        {
            Refused(connection, $"UPDATE erasure_ledger SET {set} WHERE erasure_id = '{id}'", "facts are fixed");
            Assert.Equal("subject-a", Scalar(connection, $"SELECT subject_id FROM erasure_ledger WHERE erasure_id = '{id}'"));
        }
    }

    [Fact]
    public void An_erasure_can_be_blocked_released_executed_and_then_confirmed()
    {
        var (connection, id) = Pending();
        using (connection)
        {
            Execute(connection, id, "status = 'blocked', blocked_reason = 'legal hold'");
            Execute(connection, id, "status = 'pending'");
            Execute(connection, id, "status = 'blocked', blocked_reason = 'second hold'");
            Execute(connection, id, $"status = 'executed', executed_at = '{Executed}', executed_by = 'staff-a'");
            Execute(connection, id, $"cloud_confirmed_at = '{Confirmed}'");

            Assert.Equal($"executed|{Executed}|{Confirmed}", Scalar(connection,
                $"SELECT status || '|' || executed_at || '|' || cloud_confirmed_at FROM erasure_ledger WHERE erasure_id = '{id}'"));
        }
    }

    [Theory]
    [InlineData("status = 'pending', executed_at = NULL")]
    [InlineData("status = 'blocked', blocked_reason = 'too late'")]
    [InlineData("executed_at = '2026-09-17 10:30:00'")]
    [InlineData("executed_by = 'staff-b'")]
    [InlineData("executed_by = NULL")]
    [InlineData("blocked_reason = 'rewritten'")]
    public void An_executed_erasure_is_final(string set)
    {
        var (connection, id) = Pending();
        using (connection)
        {
            Execute(connection, id, $"status = 'executed', executed_at = '{Executed}', executed_by = 'staff-a'");

            // Some of these also break the time flow; SQLite does not promise which guard speaks first.
            Assert.Throws<SqliteException>(() => Execute(connection, $"UPDATE erasure_ledger SET {set} WHERE erasure_id = '{id}'"));
            Assert.Equal($"executed|{Executed}|staff-a", Scalar(connection,
                $"SELECT status || '|' || executed_at || '|' || executed_by FROM erasure_ledger WHERE erasure_id = '{id}'"));
        }
    }

    [Fact]
    public void A_cloud_confirmation_is_written_once()
    {
        var (connection, id) = Pending();
        using (connection)
        {
            Execute(connection, id, $"status = 'executed', executed_at = '{Executed}', cloud_confirmed_at = '{Confirmed}'");

            Refused(connection, $"UPDATE erasure_ledger SET cloud_confirmed_at = '2026-09-18 00:00:00' WHERE erasure_id = '{id}'", "an executed erasure is final");
            Refused(connection, $"UPDATE erasure_ledger SET cloud_confirmed_at = NULL WHERE erasure_id = '{id}'", "an executed erasure is final");
        }
    }

    [Theory]
    [InlineData($"executed_at = '{Executed}'", "not yet executed, but stamped as executed")]
    [InlineData("executed_by = 'staff-a'", "not yet executed, but stamped by someone")]
    [InlineData($"cloud_confirmed_at = '{Confirmed}'", "confirmed by the cloud before it was done")]
    [InlineData("status = 'executed', executed_at = '2026-09-17 08:00:00'", "executed before it was asked for")]
    [InlineData($"status = 'executed', executed_at = '{Executed}', cloud_confirmed_at = '2026-09-17 09:30:00'", "confirmed before it was executed")]
    public void Time_only_runs_forward(string set, string _)
    {
        var (connection, id) = Pending();
        using (connection)
        {
            Refused(connection, $"UPDATE erasure_ledger SET {set} WHERE erasure_id = '{id}'", "runs forward");
            Assert.Equal("pending", Scalar(connection, $"SELECT status FROM erasure_ledger WHERE erasure_id = '{id}'"));
        }
    }

    [Theory]
    [InlineData("'pending'", $"'{Executed}'", "NULL")]
    [InlineData("'executed'", "'2026-09-17 08:00:00'", "NULL")]
    [InlineData("'executed'", $"'{Executed}'", "'2026-09-17 09:30:00'")]
    public void A_row_cannot_be_written_already_out_of_order(string status, string executedAt, string confirmedAt)
    {
        using var connection = _database.Connect(enforceForeignKeys: false);

        Refused(connection, $"""
            INSERT INTO erasure_ledger (erasure_id, subject_type, subject_id, requested_at, executed_at, cloud_confirmed_at, status, created_at)
            VALUES ('{Guid.NewGuid():N}', 'customer', 'subject-b', '{Requested}', {executedAt}, {confirmedAt}, {status}, '{Requested}')
            """, "runs forward");
    }

    [Fact]
    public void An_erasure_recorded_as_already_executed_goes_in()
    {
        using var connection = _database.Connect(enforceForeignKeys: false);

        Execute(connection, $"""
            INSERT INTO erasure_ledger (erasure_id, subject_type, subject_id, requested_at, executed_at, executed_by, cloud_confirmed_at, status, created_at)
            VALUES ('{Guid.NewGuid():N}', 'customer', 'subject-c', '{Requested}', '{Executed}', 'staff-a', '{Confirmed}', 'executed', '{Requested}')
            """);
    }
}
