using System.Text;

namespace Waymark.Generator.Catalogues;

/// <summary>
/// A CSV file read by header name, RFC 4180: comma-separated, fields optionally quoted, a
/// doubled quote inside quotes is a quote, and a quoted field may contain commas and line
/// breaks. UTF-8, with or without a byte-order mark; LF or CRLF.
///
/// <para>
/// Hand-written rather than a package: forty lines, no dependency, and the one behaviour
/// that matters here — <c>"Café, Thé &amp; Petit Déjeuner"</c> staying one field — is tested
/// directly.
/// </para>
/// </summary>
internal sealed class CsvTable
{
    private readonly Dictionary<string, int> _columns;

    private CsvTable(string path, IReadOnlyList<string> header, IReadOnlyList<CsvRow> rows)
    {
        Path = path;
        Header = header;
        Rows = rows;
        _columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Count; i++)
        {
            _columns[header[i]] = i;
        }
    }

    /// <summary>The file, for messages.</summary>
    public string Path { get; }

    public IReadOnlyList<string> Header { get; }

    public IReadOnlyList<CsvRow> Rows { get; }

    /// <summary>Reads a file, requiring exactly <paramref name="expectedColumns"/> in its header, in any order.</summary>
    public static CsvTable Read(string path, IReadOnlyList<string> expectedColumns)
    {
        if (!File.Exists(path))
        {
            throw new GeneratorInputException($"{path} does not exist.");
        }

        return Parse(path, Encoding.UTF8.GetString(InputFile.ReadAllBytes(path)), expectedColumns);
    }

    /// <summary>Parses text as if read from <paramref name="path"/>.</summary>
    public static CsvTable Parse(string path, string text, IReadOnlyList<string> expectedColumns)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(expectedColumns);

        var records = Split(path, text.TrimStart('\uFEFF'));
        if (records.Count == 0)
        {
            throw new GeneratorInputException($"{path} is empty; it needs a header line.");
        }

        var header = records[0].Fields;
        var problems = new List<string>();

        foreach (var missing in expectedColumns.Where(column => !header.Contains(column, StringComparer.Ordinal)))
        {
            problems.Add($"{path}: missing column '{missing}'.");
        }

        foreach (var unexpected in header.Where(column => !expectedColumns.Contains(column, StringComparer.Ordinal)))
        {
            problems.Add($"{path}: unexpected column '{unexpected}'.");
        }

        foreach (var duplicate in header.GroupBy(column => column, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            problems.Add($"{path}: column '{duplicate.Key}' appears twice.");
        }

        var rows = new List<CsvRow>();
        foreach (var record in records.Skip(1))
        {
            if (record.Fields.Count == 1 && record.Fields[0].Length == 0)
            {
                continue;
            }

            if (record.Fields.Count != header.Count)
            {
                problems.Add(
                    $"{path} line {record.Line}: {record.Fields.Count} fields where the header has {header.Count}. "
                    + "A field containing a comma must be quoted.");
                continue;
            }

            rows.Add(new CsvRow(path, record.Line, record.Fields));
        }

        if (problems.Count > 0)
        {
            throw new GeneratorInputException(problems);
        }

        var table = new CsvTable(path, header, rows);
        foreach (var row in rows)
        {
            row.Attach(table);
        }

        return table;
    }

    internal int ColumnIndex(string column) =>
        _columns.TryGetValue(column, out var index)
            ? index
            : throw new InvalidOperationException($"{Path} has no column '{column}'.");

    private static List<(int Line, List<string> Fields)> Split(string path, string text)
    {
        var records = new List<(int, List<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordLine = 1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    break;

                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;

                case '\r':
                    break;

                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add((recordLine, fields));
                    fields = [];
                    line++;
                    recordLine = line;
                    break;

                default:
                    field.Append(c);
                    break;
            }
        }

        if (inQuotes)
        {
            throw new GeneratorInputException($"{path} line {recordLine}: a quoted field is never closed.");
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add((recordLine, fields));
        }

        return records;
    }
}

/// <summary>One data row, read by column name.</summary>
internal sealed class CsvRow
{
    private readonly IReadOnlyList<string> _fields;
    private CsvTable? _table;

    internal CsvRow(string path, int line, IReadOnlyList<string> fields)
    {
        Path = path;
        Line = line;
        _fields = fields;
    }

    public string Path { get; }

    /// <summary>1-based line number in the file, header being line 1.</summary>
    public int Line { get; }

    /// <summary>"catalogue.csv line 17", for messages.</summary>
    public string Where => $"{System.IO.Path.GetFileName(Path)} line {Line}";

    /// <summary>The trimmed text of a column.</summary>
    public string this[string column] => _fields[_table!.ColumnIndex(column)].Trim();

    internal void Attach(CsvTable table) => _table = table;
}
