using System.Globalization;
using Waymark.Domain.Enums;
using Waymark.Domain.Values;
using Waymark.Generator.Configuration;

namespace Waymark.Generator.Catalogues;

/// <summary>
/// Reads a catalogue directory: <c>store.json</c>, <c>units.csv</c>, <c>categories.csv</c>,
/// <c>suppliers.csv</c>, <c>catalogue.csv</c> and <c>price_changes.csv</c> (D-046 §6).
///
/// <para>
/// <b>Every column is required and nothing is defaulted.</b> A catalogue that parses is one
/// whose every value somebody wrote down. Parse errors are collected across all files and
/// reported together; referential checks — does this supplier exist — are
/// <see cref="CatalogueValidator"/>'s job.
/// </para>
/// </summary>
internal sealed class CsvCatalogueSource(string directory) : ICatalogueSource
{
    public static readonly IReadOnlyList<string> UnitColumns =
        ["unit_code", "name_fr", "name_ar", "dimension", "base_unit_code", "factor_to_base", "decimal_places"];

    public static readonly IReadOnlyList<string> CategoryColumns =
        ["category", "subcategory", "vat_bp", "sensitive", "sensitive_reason"];

    public static readonly IReadOnlyList<string> SupplierColumns =
        ["supplier_code", "company_name", "delivery_days", "stated_lead_time_days", "net_days"];

    public static readonly IReadOnlyList<string> VariantColumns =
    [
        "sku", "barcode", "category", "subcategory", "product_name", "variant_name", "net_content",
        "selling_unit", "retail_price_dzd", "purchase_price_dzd", "supplier_code", "purchase_unit",
        "units_per_purchase_unit", "shelf_life_days", "base_daily_rate", "seasonality_profile",
        "substitutability_tier",
    ];

    public static readonly IReadOnlyList<string> PriceChangeColumns =
        ["sku", "effective_date", "retail_price_dzd", "purchase_price_dzd"];

    private static readonly Dictionary<string, DayOfWeek> Weekdays = new(StringComparer.Ordinal)
    {
        ["sun"] = DayOfWeek.Sunday,
        ["mon"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday,
    };

    public Catalogue Load()
    {
        if (!System.IO.Directory.Exists(directory))
        {
            throw new GeneratorInputException($"The catalogue directory {directory} does not exist.");
        }

        var store = GeneratorJson.Read<StoreProfile>(Path.Combine(directory, "store.json"));

        if (!Domain.Values.Currency.TryFromCode(store.Currency, out var currency))
        {
            throw new GeneratorInputException($"store.json: currency '{store.Currency}' is not supported.");
        }

        var problems = new List<string>();

        var units = Read("units.csv", UnitColumns, row => new CatalogueUnit(
            row.Where,
            Text(row, "unit_code", problems),
            Text(row, "name_fr", problems),
            Text(row, "name_ar", problems),
            EnumValue<Dimension>(row, "dimension", problems),
            Optional(row, "base_unit_code"),
            Integer(row, "factor_to_base", problems),
            (int)Integer(row, "decimal_places", problems)));

        var subcategories = Read("categories.csv", CategoryColumns, row => new CatalogueSubcategory(
            row.Where,
            Text(row, "category", problems),
            Text(row, "subcategory", problems),
            Rate(row, "vat_bp", problems),
            Flag(row, "sensitive", problems),
            Optional(row, "sensitive_reason")));

        var suppliers = Read("suppliers.csv", SupplierColumns, row => new CatalogueSupplier(
            row.Where,
            Text(row, "supplier_code", problems),
            Text(row, "company_name", problems),
            Days(row, "delivery_days", problems),
            (int)Integer(row, "stated_lead_time_days", problems),
            (int)Integer(row, "net_days", problems)));

        var variants = Read("catalogue.csv", VariantColumns, row => new CatalogueVariant(
            row.Where,
            Text(row, "sku", problems),
            Text(row, "barcode", problems),
            Text(row, "category", problems),
            Text(row, "subcategory", problems),
            Text(row, "product_name", problems),
            Text(row, "variant_name", problems),
            Text(row, "net_content", problems),
            Text(row, "selling_unit", problems),
            MoneyValue(row, "retail_price_dzd", currency, problems),
            MoneyValue(row, "purchase_price_dzd", currency, problems),
            Text(row, "supplier_code", problems),
            Text(row, "purchase_unit", problems),
            (int)Integer(row, "units_per_purchase_unit", problems),
            Optional(row, "shelf_life_days") is null ? null : (int)Integer(row, "shelf_life_days", problems),
            Real(row, "base_daily_rate", problems),
            Text(row, "seasonality_profile", problems),
            (int)Integer(row, "substitutability_tier", problems)));

        var changes = Read("price_changes.csv", PriceChangeColumns, row => new CataloguePriceChange(
            row.Where,
            Text(row, "sku", problems),
            Date(row, "effective_date", problems),
            MoneyValue(row, "retail_price_dzd", currency, problems),
            MoneyValue(row, "purchase_price_dzd", currency, problems)));

        if (problems.Count > 0)
        {
            throw new GeneratorInputException(problems);
        }

        return new Catalogue(directory, store, units, subcategories, suppliers, variants, changes);
    }

    private List<T> Read<T>(string file, IReadOnlyList<string> columns, Func<CsvRow, T> map) =>
        [.. CsvTable.Read(Path.Combine(directory, file), columns).Rows.Select(map)];

    private static string Text(CsvRow row, string column, List<string> problems)
    {
        var value = row[column];
        if (value.Length == 0)
        {
            problems.Add($"{row.Where}: '{column}' is empty.");
        }

        return value;
    }

    private static string? Optional(CsvRow row, string column)
    {
        var value = row[column];
        return value.Length == 0 ? null : value;
    }

    private static long Integer(CsvRow row, string column, List<string> problems)
    {
        if (long.TryParse(row[column], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        problems.Add($"{row.Where}: '{column}' must be a whole number; got '{row[column]}'.");
        return 0;
    }

    private static double Real(CsvRow row, string column, List<string> problems)
    {
        if (double.TryParse(row[column], NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value))
        {
            return value;
        }

        problems.Add($"{row.Where}: '{column}' must be a number; got '{row[column]}'.");
        return 0;
    }

    private static bool Flag(CsvRow row, string column, List<string> problems)
    {
        switch (row[column])
        {
            case "0":
                return false;
            case "1":
                return true;
            default:
                problems.Add($"{row.Where}: '{column}' must be 0 or 1; got '{row[column]}'.");
                return false;
        }
    }

    private static BasisPoints Rate(CsvRow row, string column, List<string> problems)
    {
        var value = Integer(row, column, problems);
        if (value is < 0 or > BasisPoints.Scale)
        {
            problems.Add($"{row.Where}: '{column}' is basis points, 0 to {BasisPoints.Scale}; got {value}.");
            return BasisPoints.Zero;
        }

        return new BasisPoints((int)value);
    }

    private static DateOnly Date(CsvRow row, string column, List<string> problems)
    {
        if (DateOnly.TryParseExact(row[column], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            return value;
        }

        problems.Add($"{row.Where}: '{column}' must be a date yyyy-MM-dd; got '{row[column]}'.");
        return default;
    }

    /// <summary>
    /// A price written in currency units with at most two decimals, read exactly as a
    /// <c>decimal</c> — never through a double (CLAUDE.md §3.1).
    /// </summary>
    private static Money MoneyValue(CsvRow row, string column, Currency currency, List<string> problems)
    {
        if (decimal.TryParse(row[column], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            var minorUnits = amount * Domain.Values.Currency.StorageScale;
            if (minorUnits == decimal.Truncate(minorUnits))
            {
                return new Money((long)minorUnits, currency);
            }

            problems.Add($"{row.Where}: '{column}' has more than two decimals: '{row[column]}'.");
        }
        else
        {
            problems.Add($"{row.Where}: '{column}' must be a non-negative amount like 125.50; got '{row[column]}'.");
        }

        return Money.Zero(currency);
    }

    private static T EnumValue<T>(CsvRow row, string column, List<string> problems)
        where T : struct, Enum
    {
        var text = row[column];
        foreach (var value in Enum.GetValues<T>())
        {
            if (string.Equals(System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()), text, StringComparison.Ordinal))
            {
                return value;
            }
        }

        problems.Add($"{row.Where}: '{column}' value '{text}' is not one of {string.Join(", ", Enum.GetValues<T>().Select(v => System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString())))}.");
        return default;
    }

    private static HashSet<DayOfWeek> Days(CsvRow row, string column, List<string> problems)
    {
        var days = new HashSet<DayOfWeek>();
        foreach (var part in row[column].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Weekdays.TryGetValue(part, out var day))
            {
                days.Add(day);
            }
            else
            {
                problems.Add($"{row.Where}: '{column}' has '{part}'; days are sun, mon, tue, wed, thu, fri, sat separated by |.");
            }
        }

        return days;
    }
}
