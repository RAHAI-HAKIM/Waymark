using System.Globalization;
using System.Text;

namespace Waymark.Generator.Reporting;

/// <summary>One variant on one day: what was wanted, what happened to it, and the shelf around it.</summary>
/// <param name="Date">The store-local date.</param>
/// <param name="Sku">The variant.</param>
/// <param name="StoreOpen">Whether the store opened that day. A closed day has no demand.</param>
/// <param name="OnHandOpen">Units on hand when the day's trading began, sellable or expired.</param>
/// <param name="Latent">Units customers wanted.</param>
/// <param name="Sold">Of those, units sold as this variant.</param>
/// <param name="Substituted">Of those, units bought as another variant of the same product because this one was out.</param>
/// <param name="Lost">Of those, units nobody sold: <c>latent − sold − substituted</c>.</param>
/// <param name="SoldAsSubstitute">Units of this variant bought in place of a sibling that was out. Not part of its own demand.</param>
/// <param name="OnHandClose">Units on hand at the end of the day.</param>
internal sealed record LatentDemandRow(
    DateOnly Date,
    string Sku,
    bool StoreOpen,
    long OnHandOpen,
    int Latent,
    int Sold,
    int Substituted,
    int Lost,
    int SoldAsSubstitute,
    long OnHandClose);

/// <summary>
/// <c>latent-demand.csv</c>: the ground truth the engine is later evaluated against (D-046 §34).
///
/// <para>
/// One row per variant per day, zeros included, because a zero is information. It is written
/// beside the database and <b>never loaded into it</b>: a store only ever knows what it sold,
/// and an engine that could read true demand would be marked on an exam it had seen. The
/// on-hand columns say whether a low sale was a low want or an empty shelf.
/// </para>
/// <para>
/// Units are whole selling units (the generator sells nothing by weight). UTF-8 without a
/// byte-order mark, LF line endings, invariant formatting, so two runs from the same seed
/// write the same bytes.
/// </para>
/// </summary>
internal sealed class LatentDemandCsv : IDisposable
{
    public const string FileName = "latent-demand.csv";

    public const string Header = "date,sku,store_open,on_hand_open,latent,sold,substituted,lost,sold_as_substitute,on_hand_close";

    private readonly StreamWriter _writer;

    public LatentDemandCsv(string outputDirectory)
    {
        FilePath = Path.Combine(outputDirectory, FileName);
        _writer = new StreamWriter(FilePath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { NewLine = "\n" };
        _writer.WriteLine(Header);
    }

    public string FilePath { get; }

    /// <summary>Rows written so far, header excluded.</summary>
    public long Rows { get; private set; }

    public void Write(LatentDemandRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Sku.Contains(',', StringComparison.Ordinal) || row.Sku.Contains('"', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"SKU '{row.Sku}' would need quoting in {FileName}; SKUs are plain codes.");
        }

        _writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{row.Date:yyyy-MM-dd},{row.Sku},{(row.StoreOpen ? 1 : 0)},{row.OnHandOpen},{row.Latent},{row.Sold},{row.Substituted},{row.Lost},{row.SoldAsSubstitute},{row.OnHandClose}"));
        Rows++;
    }

    public void Dispose() => _writer.Dispose();
}
