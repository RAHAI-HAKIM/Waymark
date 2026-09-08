using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waymark.Persistence;

namespace Waymark.Integration.Tests;

/// <summary>
/// The EF model and <c>schema_v7_1.sql</c> must describe the same database.
///
/// <para>
/// Nothing else checks this. <c>HasPendingModelChanges()</c> compares the model
/// to its own snapshot, both generated from the configurations, so it cannot
/// see the schema at all (decisions.md O-7). Under D-019 the initial migration
/// will be hand-pasted SQL, which means a configuration that disagrees with the
/// schema produces a database that disagrees with the model, silently, for the
/// life of the project.
/// </para>
/// <para>
/// So this builds both — one database from the schema, one from the model's own
/// create script — and compares them structurally.
/// </para>
/// </summary>
public sealed class ModelMatchesSchemaTests : IClassFixture<SchemaFixture>
{
    private readonly SchemaFixture _schema;

    public ModelMatchesSchemaTests(SchemaFixture schema) => _schema = schema;

    private sealed record ColumnShape(string Type, bool NotNull, bool IsKey);

    private static Dictionary<string, Dictionary<string, ColumnShape>> Describe(SqliteConnection connection)
    {
        var tables = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT name FROM sqlite_schema
                WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
                  AND name <> '__EFMigrationsHistory'
                ORDER BY name
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var described = new Dictionary<string, Dictionary<string, ColumnShape>>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var columns = new Dictionary<string, ColumnShape>(StringComparer.Ordinal);
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT name, type, \"notnull\", pk FROM pragma_table_info('{table}')";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns[reader.GetString(0)] = new ColumnShape(
                    reader.GetString(1).ToUpperInvariant(),
                    reader.GetInt64(2) == 1,
                    reader.GetInt64(3) > 0);
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
            WHERE schema = 'main' AND type = 'table' AND strict = 1
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            strict.Add(reader.GetString(0));
        }

        return strict;
    }


    private sealed record IndexShape(string Table, string Columns, bool Unique, string? Filter);

    /// <summary>
    /// Indexes, compared by what they constrain rather than by name.
    ///
    /// <para>
    /// Uniqueness and the WHERE clause of a partial index are the constraint,
    /// not decoration. An earlier version of this comparison looked only at
    /// names and columns, and let two real defects through: a composite
    /// UNIQUE(a, b, c) flattened into three single-column constraints, and
    /// twelve partial indexes that lost their filter. Without its filter,
    /// ux_parameter_current forbids the registry holding two versions of a
    /// parameter, which is the only thing the registry is for.
    /// </para>
    /// </summary>
    private static HashSet<IndexShape> Indexes(SqliteConnection connection)
    {
        var shapes = new HashSet<IndexShape>();
        var names = new List<(string Name, string Table)>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT name, tbl_name FROM sqlite_schema
                WHERE type = 'index' AND tbl_name <> '__EFMigrationsHistory'
                ORDER BY name
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                names.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        foreach (var (name, table) in names)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT (SELECT group_concat(name, ',') FROM (
                            SELECT name FROM pragma_index_info('{name}') ORDER BY seqno)),
                       (SELECT "unique" FROM pragma_index_list('{table}') WHERE name = '{name}'),
                       (SELECT sql FROM sqlite_schema WHERE type = 'index' AND name = '{name}')
                """;
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0))
            {
                continue;
            }

            var sql = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var where = sql.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
            var filter = where < 0
                ? null
                : string.Join(' ', sql[(where + 7)..].Replace("\"", "", StringComparison.Ordinal)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

            shapes.Add(new IndexShape(table, reader.GetString(0), reader.GetInt64(1) == 1, filter));
        }

        return shapes;
    }

    /// <summary>A database built from the model rather than from the schema.</summary>
    private static SqliteConnection BuildFromModel(string path)
    {
        var options = new DbContextOptionsBuilder<WaymarkDbContext>()
            .UseWaymarkSqlite(path)
            .Options;

        using (var context = new WaymarkDbContext(options))
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = context.Database.GenerateCreateScript();
            command.ExecuteNonQuery();
        }

        var open = new SqliteConnection($"Data Source={path}");
        open.Open();
        return open;
    }

    [Fact]
    public void Every_table_and_column_agrees()
    {
        var directory = Path.Combine(Path.GetTempPath(), "waymark-model-diff", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            using var fromSchema = _schema.Connect();
            using var fromModel = BuildFromModel(Path.Combine(directory, "model.db"));

            var schema = Describe(fromSchema);
            var model = Describe(fromModel);

            var differences = new List<string>();

            foreach (var table in schema.Keys.Except(model.Keys).Order(StringComparer.Ordinal))
            {
                differences.Add($"{table}: in the schema, no entity configured for it");
            }

            foreach (var table in model.Keys.Except(schema.Keys).Order(StringComparer.Ordinal))
            {
                differences.Add($"{table}: configured, but no such table in the schema");
            }

            foreach (var table in schema.Keys.Intersect(model.Keys).Order(StringComparer.Ordinal))
            {
                var (left, right) = (schema[table], model[table]);

                foreach (var column in left.Keys.Except(right.Keys).Order(StringComparer.Ordinal))
                {
                    differences.Add($"{table}.{column}: in the schema, not mapped");
                }

                foreach (var column in right.Keys.Except(left.Keys).Order(StringComparer.Ordinal))
                {
                    differences.Add($"{table}.{column}: mapped, but not in the schema");
                }

                foreach (var column in left.Keys.Intersect(right.Keys).Order(StringComparer.Ordinal))
                {
                    if (left[column] != right[column])
                    {
                        differences.Add($"{table}.{column}: schema {left[column]} vs model {right[column]}");
                    }
                }
            }

            // Indexes are compared by shape, not name: EF cannot write an
            // inline UNIQUE, so its equivalent of one is always named
            // differently.
            var indexesInSchema = Indexes(fromSchema);
            var indexesInModel = Indexes(fromModel);

            foreach (var index in indexesInSchema.Except(indexesInModel).OrderBy(i => i.Table + i.Columns, StringComparer.Ordinal))
            {
                differences.Add(
                    $"{index.Table}({index.Columns}) unique={index.Unique} filter={index.Filter ?? "none"}: "
                    + "constrained by the schema, not by the model");
            }

            foreach (var index in indexesInModel.Except(indexesInSchema).OrderBy(i => i.Table + i.Columns, StringComparer.Ordinal))
            {
                differences.Add(
                    $"{index.Table}({index.Columns}) unique={index.Unique} filter={index.Filter ?? "none"}: "
                    + "constrained by the model, not by the schema");
            }

            var strictInSchema = StrictTables(fromSchema);
            var strictInModel = StrictTables(fromModel);
            foreach (var table in strictInSchema.Except(strictInModel).Order(StringComparer.Ordinal))
            {
                differences.Add($"{table}: STRICT in the schema, not from the model");
            }

            Assert.True(differences.Count == 0,
                "The EF model and schema_v7_1.sql have drifted. Nothing else catches "
                + "this, because HasPendingModelChanges compares the model to its own "
                + "snapshot.\n\n  " + string.Join("\n  ", differences));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
