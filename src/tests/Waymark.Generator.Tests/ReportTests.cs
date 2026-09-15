using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Waymark.Generator.Tests.Sql;

namespace Waymark.Generator.Tests;

/// <summary>
/// The run's outputs beside the database (W10 S9): <c>report.md</c> says what the store holds,
/// and the manifest fingerprints everything a run wrote.
/// </summary>
public sealed partial class ReportTests(GrocerySalesRun grocery) : IClassFixture<GrocerySalesRun>
{
    private string Report => File.ReadAllText(grocery.Result.ReportPath);

    /// <summary>The value cell of a two-column report row.</summary>
    private string Cell(string label)
    {
        var match = Regex.Match(Report, "^\\| " + Regex.Escape(label) + " \\| (.+?) \\|$", RegexOptions.Multiline);
        Assert.True(match.Success, $"report.md has no row '{label}'.");
        return match.Groups[1].Value;
    }

    private static long Number(string text) => long.Parse(NotDigits().Replace(text, string.Empty), CultureInfo.InvariantCulture);

    [Fact]
    public void The_report_has_every_section_a_reviewer_reads()
    {
        foreach (var section in new[] { "## Trading", "## When customers come", "## Basket sizes", "## Ramadan and the pay cycle", "## Demand against the shelf", "## Supply and spoilage", "## Payments and the till", "## The outbox" })
        {
            Assert.Contains(section + "\n", Report, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_reports_figures_are_the_databases_and_the_csvs()
    {
        using var db = grocery.Open();
        var rows = SalesTests.ReadCsv(grocery.Result.LatentDemandPath);

        Assert.Equal(Scalar(db, "SELECT count(*) FROM transactions WHERE status <> 'voided' AND original_transaction_id IS NULL"), Number(Cell("Sales (completed, excluding refunds)")));
        Assert.Equal(
            Scalar(db, "SELECT sum(i.quantity) FROM transaction_items i JOIN transactions t USING (transaction_id) WHERE t.status <> 'voided' AND t.original_transaction_id IS NULL") / 1000,
            Number(Cell("Units sold")));
        Assert.Equal(Scalar(db, "SELECT sum(total_amount) FROM transactions WHERE status <> 'voided'"), Number(Cell("Net revenue after refunds, TTC")));
        Assert.Equal(rows.Sum(r => (long)r.Latent), Number(Regex.Match(Report, @"^\| Wanted \(latent demand\) \| ([\d ]+) \|", RegexOptions.Multiline).Groups[1].Value));
        Assert.Equal(rows.Sum(r => (long)r.Lost), Number(Regex.Match(Report, @"^\| Lost \| ([\d ]+) \|", RegexOptions.Multiline).Groups[1].Value));
        Assert.Equal(Scalar(db, "SELECT count(*) FROM transactions WHERE status = 'voided'"), Number(Cell("Voided ringings")));
        Assert.Equal(Scalar(db, "SELECT count(*) FROM transactions WHERE status <> 'voided' AND original_transaction_id IS NULL"), Number(Cell("Anonymous baskets emitted")));
    }

    [Fact]
    public void The_basket_size_table_accounts_for_every_sale()
    {
        using var db = grocery.Open();
        var section = Report[Report.IndexOf("## Basket sizes", StringComparison.Ordinal)..Report.IndexOf("## Ramadan", StringComparison.Ordinal)];
        var counts = Regex.Matches(section, @"^\| [^|]+ \| ([\d ]+) \| [\d.]+% \|", RegexOptions.Multiline).Select(m => Number(m.Groups[1].Value)).ToList();

        Assert.Equal(15, counts.Count);
        Assert.Equal(Scalar(db, "SELECT count(*) FROM transactions WHERE status <> 'voided' AND original_transaction_id IS NULL"), counts.Sum());
    }

    [Fact]
    public void The_manifest_fingerprints_every_output_and_records_the_backlog_of_every_day()
    {
        var manifest = JsonDocument.Parse(File.ReadAllText(grocery.Result.ManifestPath)).RootElement;
        var outputs = manifest.GetProperty("outputs");

        foreach (var file in new[] { grocery.Result.LatentDemandPath, grocery.Result.ReportPath })
        {
            var expected = Randomness.Mixing.Fnv1a(File.ReadAllBytes(file)).ToString("x16", CultureInfo.InvariantCulture);
            Assert.Equal(expected, outputs.GetProperty(Path.GetFileName(file)).GetString());
        }

        Assert.Equal("always_on", manifest.GetProperty("connectivity").GetString());
        Assert.Equal(49, manifest.GetProperty("outbox").GetProperty("backlog").GetArrayLength());
        Assert.Contains("connectivity", manifest.GetProperty("steps_completed").EnumerateArray().Select(s => s.GetString()));
    }

    [GeneratedRegex("[^0-9-]")]
    private static partial Regex NotDigits();
}
