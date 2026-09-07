using Microsoft.Data.Sqlite;
using Waymark.Persistence;

namespace Waymark.Integration.Tests;

/// <summary>
/// Builds a real database from the embedded schema, once per test class.
///
/// <para>
/// A real temporary file, not an in-memory database (CLAUDE.md §8). WAL mode,
/// foreign key enforcement and file-level behaviour are part of what is being
/// asserted, and an in-memory substitute quietly changes all three.
/// </para>
/// </summary>
public sealed class SchemaFixture : IDisposable
{
    private readonly string _directory;

    public SchemaFixture()
    {
        _directory = Path.Combine(Path.GetTempPath(), "waymark-schema-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "waymark-store.db");

        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = SchemaScript.Read();
        command.ExecuteNonQuery();
    }

    public string DatabasePath { get; }

    /// <summary>An open connection with foreign keys enforced.</summary>
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
    }
}
