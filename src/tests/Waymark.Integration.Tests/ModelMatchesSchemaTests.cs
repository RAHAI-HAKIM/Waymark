using Microsoft.Data.Sqlite;

namespace Waymark.Integration.Tests;

/// <summary>
/// What ships must match what was reviewed.
///
/// <para>
/// Two databases: one built from the frozen <c>schema_v7_1.sql</c>, the artifact
/// a human read and approved; one built the way a store gets it, <c>Migrate()</c>
/// then <c>ApplyTriggers()</c>. They must describe the same database.
/// </para>
/// <para>
/// Nothing else checks this. <c>HasPendingModelChanges()</c> compares the model
/// to its own snapshot, both generated from the configurations, so it never sees
/// the schema at all (decisions.md O-7).
/// </para>
/// <para>
/// The comparison is deliberately wide, because an earlier version of it was
/// not. Looking only at index names and columns let a composite UNIQUE flattened
/// into three single-column constraints, and twelve partial indexes stripped of
/// their filter, both compare equal (D-024). It now checks column order, types,
/// nullability, keys, STRICT, index columns, index uniqueness, partial-index
/// filters and triggers.
/// </para>
/// </summary>
public sealed class ModelMatchesSchemaTests
    : IClassFixture<ReviewedSchemaFixture>, IClassFixture<MigratedDatabaseFixture>
{
    private readonly ReviewedSchemaFixture _reviewed;
    private readonly MigratedDatabaseFixture _shipped;

    public ModelMatchesSchemaTests(ReviewedSchemaFixture reviewed, MigratedDatabaseFixture shipped)
    {
        _reviewed = reviewed;
        _shipped = shipped;
    }

    private sealed record ColumnShape(int Position, string Type, bool NotNull, bool IsKey);

    private sealed record IndexShape(string Table, string Columns, bool Unique, string? Filter);

    private static List<string> Tables(SqliteConnection connection)
    {
        var tables = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name FROM sqlite_schema
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%'
            ORDER BY name
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static Dictionary<string, Dictionary<string, ColumnShape>> Columns(SqliteConnection connection)
    {
        var described = new Dictionary<string, Dictionary<string, ColumnShape>>(StringComparer.Ordinal);
        foreach (var table in Tables(connection))
        {
            var columns = new Dictionary<string, ColumnShape>(StringComparer.Ordinal);
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT cid, name, type, \"notnull\", pk FROM pragma_table_info('{table}')";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // Position is compared too: SELECT * and a column-less INSERT
                // both depend on declaration order.
                columns[reader.GetString(1)] = new ColumnShape(
                    (int)reader.GetInt64(0),
                    reader.GetString(2).ToUpperInvariant(),
                    reader.GetInt64(3) == 1,
                    reader.GetInt64(4) > 0);
            }

            described[table] = columns;
        }

        return described;
    }

    private static HashSet<string> StrictTables(SqliteConnection connection)
    {
        var strict = new HashSet<string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name FROM pragma_table_list
            WHERE schema = 'main' AND type = 'table' AND strict = 1 AND name NOT LIKE '__EF%'
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            strict.Add(reader.GetString(0));
        }

        return strict;
    }

    /// <summary>
    /// Indexes by what they constrain, not by name: EF cannot write an inline
    /// UNIQUE, so its equivalent of one is always named differently.
    /// </summary>
    private static HashSet<IndexShape> Indexes(SqliteConnection connection)
    {
        var shapes = new HashSet<IndexShape>();
        foreach (var table in Tables(connection))
        {
            var indexNames = new List<(string Name, bool Unique)>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT name, \"unique\" FROM pragma_index_list('{table}')";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    indexNames.Add((reader.GetString(0), reader.GetInt64(1) == 1));
                }
            }

            foreach (var (name, unique) in indexNames)
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"""
                    SELECT (SELECT group_concat(name, ',') FROM (
                                SELECT name FROM pragma_index_info('{name}') ORDER BY seqno)),
                           (SELECT sql FROM sqlite_schema WHERE type = 'index' AND name = '{name}')
                    """;
                using var reader = command.ExecuteReader();
                if (!reader.Read() || reader.IsDBNull(0))
                {
                    continue;
                }

                var sql = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var where = sql.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
                var filter = where < 0
                    ? null
                    : string.Join(
                            ' ',
                            sql[(where + 7)..]
                                .Replace("\"", string.Empty, StringComparison.Ordinal)
                                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                        .ToLowerInvariant();

                shapes.Add(new IndexShape(table, reader.GetString(0), unique, filter));
            }
        }

        return shapes;
    }

    private static HashSet<string> Triggers(SqliteConnection connection)
    {
        var triggers = new HashSet<string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'trigger'";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            triggers.Add(reader.GetString(0));
        }

        return triggers;
    }

    [Fact]
    public void The_shipped_database_matches_the_reviewed_schema()
    {
        using var reviewed = _reviewed.Connect();
        using var shipped = _shipped.Connect();

        var differences = new List<string>();

        var left = Columns(reviewed);
        var right = Columns(shipped);

        foreach (var table in left.Keys.Except(right.Keys).Order(StringComparer.Ordinal))
        {
            differences.Add($"{table}: in the reviewed schema, not created by Migrate()");
        }

        foreach (var table in right.Keys.Except(left.Keys).Order(StringComparer.Ordinal))
        {
            differences.Add($"{table}: created by Migrate(), not in the reviewed schema");
        }

        foreach (var table in left.Keys.Intersect(right.Keys).Order(StringComparer.Ordinal))
        {
            var reviewedColumns = left[table];
            var shippedColumns = right[table];

            foreach (var column in reviewedColumns.Keys.Except(shippedColumns.Keys).Order(StringComparer.Ordinal))
            {
                differences.Add($"{table}.{column}: reviewed, not mapped");
            }

            foreach (var column in shippedColumns.Keys.Except(reviewedColumns.Keys).Order(StringComparer.Ordinal))
            {
                differences.Add($"{table}.{column}: mapped, not in the reviewed schema");
            }

            foreach (var column in reviewedColumns.Keys.Intersect(shippedColumns.Keys).Order(StringComparer.Ordinal))
            {
                if (reviewedColumns[column] != shippedColumns[column])
                {
                    differences.Add(
                        $"{table}.{column}: reviewed {reviewedColumns[column]} vs shipped {shippedColumns[column]}");
                }
            }
        }

        foreach (var table in StrictTables(reviewed).Except(StrictTables(shipped)).Order(StringComparer.Ordinal))
        {
            differences.Add($"{table}: STRICT in the reviewed schema, not as shipped");
        }

        var reviewedIndexes = Indexes(reviewed);
        var shippedIndexes = Indexes(shipped);

        foreach (var index in reviewedIndexes.Except(shippedIndexes)
                     .OrderBy(i => i.Table + i.Columns, StringComparer.Ordinal))
        {
            differences.Add(
                $"{index.Table}({index.Columns}) unique={index.Unique} filter={index.Filter ?? "none"}: "
                + "constrained in the reviewed schema, not as shipped");
        }

        foreach (var index in shippedIndexes.Except(reviewedIndexes)
                     .OrderBy(i => i.Table + i.Columns, StringComparer.Ordinal))
        {
            differences.Add(
                $"{index.Table}({index.Columns}) unique={index.Unique} filter={index.Filter ?? "none"}: "
                + "constrained as shipped, not in the reviewed schema");
        }

        // Triggers reach the shipped database only through ApplyTriggers(), so
        // these two loops are also what prove it ran.
        foreach (var trigger in Triggers(reviewed).Except(Triggers(shipped)).Order(StringComparer.Ordinal))
        {
            differences.Add(
                $"trigger {trigger}: in the reviewed schema, missing after Migrate() + ApplyTriggers()");
        }

        foreach (var trigger in Triggers(shipped).Except(Triggers(reviewed)).Order(StringComparer.Ordinal))
        {
            differences.Add($"trigger {trigger}: shipped, but not in the reviewed schema");
        }

        Assert.True(
            differences.Count == 0,
            "The database a store gets no longer matches schema_v7_1.sql, the file that was "
            + "reviewed. Nothing else catches this.\n\n  "
            + string.Join("\n  ", differences));
    }
}
