using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Waymark.Generator.Tests;

/// <summary>
/// A store generated once and shared by every test in a class: a run simulates trading days,
/// so building one per test would make the suite slow for no extra coverage.
/// </summary>
public abstract class GeneratedStoreFixture : IDisposable
{
    private readonly ScratchDirectory _scratch = new();

    protected GeneratedStoreFixture(string config, int days)
    {
        ConfigPath = config;
        Result = GeneratorRun.Execute(new GeneratorArguments(config, Path.Combine(_scratch.Path, "store"), null, days), TextWriter.Null);
    }

    internal string ConfigPath { get; }

    internal RunResult Result { get; }

    internal SqliteConnection Open() => Sql.Open(Result.DatabasePath);

    public void Dispose()
    {
        _scratch.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>The hardware shop for 60 days: <c>half_even</c>, a closed Friday, prices off the 5 DZD grid.</summary>
public sealed class MiniSalesRun() : GeneratedStoreFixture(TestInputs.MiniConfig, 60);

/// <summary>The grocery for seven weeks: <c>half_up</c>, 399 variants, a lunch closure, deliveries and stockouts.</summary>
public sealed class GrocerySalesRun() : GeneratedStoreFixture(TestInputs.GroceryConfig, 49);

/// <summary>Read-only queries against a generated store.</summary>
internal static class Sql
{
    public static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        return connection;
    }

    public static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public static List<string> Column(SqliteConnection connection, string sql) =>
        [.. Rows(connection, sql).Select(row => Convert.ToString(row[0], CultureInfo.InvariantCulture)!)];

    public static List<object[]> Rows(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<object[]>();
        while (reader.Read())
        {
            var row = new object[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row);
        }

        return rows;
    }

    public static DateTimeOffset Timestamp(object value) =>
        DateTimeOffset.ParseExact((string)value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    /// <summary>Asserts every row's id (column 0) carries the time in its created_at (column 1), to the second.</summary>
    public static void AssertIdsMatch(SqliteConnection db, string sql)
    {
        var rows = Rows(db, sql);
        Assert.True(rows.Count > 0, $"'{sql}' returned no rows; the check checked nothing.");

        foreach (var row in rows)
        {
            var created = Timestamp(row[1]);
            var idTime = CalendarTests.UlidTime((string)row[0]);
            Assert.True(
                (idTime - created).Duration() < TimeSpan.FromSeconds(1),
                $"id {row[0]} is stamped {idTime:O} but its row was created {created:O}.");
        }
    }

    public static long Long(object value) => Convert.ToInt64(value, CultureInfo.InvariantCulture);
}
