using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Waymark.Generator.Writing;

/// <summary>
/// Every table's rows as text, in a fixed order: tables by name, rows by primary key then by
/// every column, values in an invariant format.
///
/// <para>
/// <b>Determinism is compared on this, never on file bytes (D-046).</b> Two databases with
/// identical contents can differ byte for byte — page layout, freelists, and once the
/// database key lands SQLCipher's random salts (O-20). Two runs from the same seed must
/// produce the same dump; that is the property the generator promises.
/// </para>
/// </summary>
internal static class CanonicalDump
{
    /// <param name="connection">The store.</param>
    /// <param name="except">Tables left out, for comparing two stores that may legitimately differ there (the outbox across connectivity profiles).</param>
    public static string Render(SqliteConnection connection, params string[] except)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(except);

        var output = new StringBuilder();

        foreach (var table in Tables(connection).Where(table => !except.Contains(table, StringComparer.Ordinal)))
        {
            var columns = Columns(connection, table);
            var keys = columns.Where(c => c.KeyPosition > 0).OrderBy(c => c.KeyPosition).Select(c => c.Name)
                .Concat(columns.Select(c => c.Name))
                .Select(Quote);

            output.Append("## ").Append(table).Append(" (").AppendJoin(", ", columns.Select(c => c.Name)).Append(")\n");

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {Quote(table)} ORDER BY {string.Join(", ", keys)}";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    if (i > 0)
                    {
                        output.Append('\t');
                    }

                    output.Append(Format(reader.GetValue(i)));
                }

                output.Append('\n');
            }
        }

        return output.ToString();
    }

    /// <summary>Row counts per table, for the manifest.</summary>
    public static SortedDictionary<string, long> Counts(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in Tables(connection))
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT count(*) FROM {Quote(table)}";
            counts[table] = (long)command.ExecuteScalar()!;
        }

        return counts;
    }

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

    private static List<(string Name, long KeyPosition)> Columns(SqliteConnection connection, string table)
    {
        var columns = new List<(string, long)>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, pk FROM pragma_table_info($table) ORDER BY cid";
        command.Parameters.AddWithValue("$table", table);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return columns;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string Format(object value) => value switch
    {
        DBNull => "NULL",
        long number => number.ToString(CultureInfo.InvariantCulture),
        double real => real.ToString("R", CultureInfo.InvariantCulture),
        byte[] blob => "x'" + Convert.ToHexString(blob) + "'",
        string text => text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
