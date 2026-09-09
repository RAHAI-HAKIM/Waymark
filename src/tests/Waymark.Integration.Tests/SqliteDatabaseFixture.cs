using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waymark.Persistence;

namespace Waymark.Integration.Tests;

/// <summary>
/// A real database in a temporary directory, built one of two ways.
///
/// <para>
/// A real file, not an in-memory database (CLAUDE.md §8). WAL mode, foreign key
/// enforcement and file-level behaviour are part of what these suites assert,
/// and an in-memory substitute quietly changes all three.
/// </para>
/// </summary>
public abstract class SqliteDatabaseFixture : IDisposable
{
    private readonly string _directory;

    protected SqliteDatabaseFixture()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "waymark-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "waymark-store.db");
        Build();
    }

    public string DatabasePath { get; }

    protected abstract void Build();

    public WaymarkDbContext NewContext(bool enforceForeignKeys = true)
    {
        var options = new DbContextOptionsBuilder<WaymarkDbContext>()
            .UseWaymarkSqlite(DatabasePath, enforceForeignKeys)
            .Options;

        return new WaymarkDbContext(options);
    }

    public SqliteConnection Connect(bool enforceForeignKeys = true)
    {
        var connection = new SqliteConnection(
            $"Data Source={DatabasePath};Foreign Keys={(enforceForeignKeys ? "True" : "False")}");
        connection.Open();
        return connection;
    }

    /// <summary>Every row of a single-column query, as strings.</summary>
    public IReadOnlyList<string> Query(string sql)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(reader.IsDBNull(0) ? "<null>" : reader.GetValue(0).ToString() ?? "");
        }

        return rows;
    }

    public string? Scalar(string sql)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString();
    }

    public void Dispose()
    {
        // Microsoft.Data.Sqlite pools connections; the file stays locked until
        // the pool is cleared, and the -wal and -shm siblings with it.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A locked file on a build agent must not fail the test run.
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Built from the frozen <c>schema_v7_1.sql</c> — the artifact a human reviewed.
///
/// <para>
/// This is the reference side of the fidelity comparison and nothing else. It
/// is not what ships: under D-019 a store database is created by
/// <c>Migrate()</c>, so a suite that asserts rules against this file is
/// checking a museum piece (decisions.md D-020).
/// </para>
/// </summary>
public sealed class ReviewedSchemaFixture : SqliteDatabaseFixture
{
    protected override void Build()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = SchemaScript.Read();
        command.ExecuteNonQuery();
    }
}

/// <summary>
/// Built the way a store is: <c>Migrate()</c> then <c>ApplyTriggers()</c>.
///
/// <para>
/// Everything that asserts a rule about the database uses this one, so the
/// rules are checked against what actually reaches a till.
/// </para>
/// </summary>
public sealed class MigratedDatabaseFixture : SqliteDatabaseFixture
{
    protected override void Build()
    {
        using var context = NewContext();
        context.MigrateAndApplyTriggers();
    }
}
