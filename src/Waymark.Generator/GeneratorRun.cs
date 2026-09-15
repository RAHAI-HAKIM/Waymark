using System.Globalization;
using System.Text.Json;
using Waymark.Application.IdGenerator;
using Waymark.Domain.Values;
using Waymark.Generator.Calendar;
using Waymark.Generator.Catalogues;
using Waymark.Generator.Configuration;
using Waymark.Generator.Randomness;
using Waymark.Generator.Reporting;
using Waymark.Generator.Simulation;
using Waymark.Generator.Writing;

namespace Waymark.Generator;

/// <summary>What a finished run produced.</summary>
internal sealed record RunResult(
    string DatabasePath,
    string LatentDemandPath,
    string ReportPath,
    string ManifestPath,
    string StoreId,
    long Seed,
    IReadOnlyDictionary<string, long> Counts);

/// <summary>
/// One generator run, start to finish: read and validate everything, then build the store.
///
/// <para>
/// <b>Nothing is written until every input has been validated</b> (D-046 §9). A catalogue
/// error found halfway through a simulated year would leave a half-built store that looks
/// like a result.
/// </para>
/// </summary>
internal static class GeneratorRun
{
    /// <summary>The steps implemented so far, recorded in the manifest so a store says how complete it is.</summary>
    public static readonly IReadOnlyList<string> Steps = ["reference_data", "customers", "opening_stock", "sales", "supply", "mess", "connectivity"];

    public static RunResult Execute(GeneratorArguments arguments, TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(log);

        var configPath = Path.GetFullPath(arguments.ConfigPath);
        var config = GeneratorJson.Read<GeneratorConfig>(configPath);
        var catalogueDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configPath)!, config.Catalogue));
        var catalogue = new CsvCatalogueSource(catalogueDirectory).Load();
        var window = RunWindow.From(config.Run, arguments.Days);
        if (arguments.Connectivity is { } profile)
        {
            config = config with { Connectivity = config.Connectivity with { Profile = profile } };
        }

        var problems = ConfigValidator.Validate(config, catalogue.Store, window);
        problems.AddRange(CatalogueValidator.Validate(catalogue, config, window));
        if (problems.Count > 0)
        {
            throw new GeneratorInputException(problems);
        }

        var seed = arguments.Seed ?? config.Run.MasterSeed;
        var random = new RandomSource(seed);
        var calendar = new CalendarModel(config.Calendar, catalogue.Store);
        var clock = new SimulatedClock(calendar.ToUtc(window.CommissioningDate, ReferenceData.CommissioningMinute));
        var ids = new SeededIdGenerator(random.DeriveSeed("ids"), clock);
        var currency = Currency.FromCode(catalogue.Store.Currency);
        var storeId = ids.NewId();

        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Generating {catalogue.Store.StoreName}: seed {seed}, commissioned {window.CommissioningDate:yyyy-MM-dd}, "
            + $"{window.Days} days from {window.FirstDay:yyyy-MM-dd}, {catalogue.Variants.Count} variants."));

        using var database = StoreDatabase.Create(arguments.OutputDirectory, storeId, currency);

        var store = ReferenceData.Write(database, storeId, catalogue, config, window, calendar, clock, ids, random);
        log.WriteLine($"  reference data and {store.Customers.Count} customers written");

        var state = OpeningStock.Write(database, store, config.OpeningStock, window, calendar, clock, ids, random);
        log.WriteLine("  opening stock received");

        string latentDemandPath;
        var loop = new DayLoop(new SimulationContext(database, store, config, calendar, clock, ids, random, state));
        using (var csv = new LatentDemandCsv(arguments.OutputDirectory))
        {
            var totals = loop.Run(window, csv);
            latentDemandPath = csv.FilePath;
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {totals.OpenDays} trading days: {totals.Transactions} sales, {totals.UnitsSold} of {totals.UnitsWanted} units wanted sold, {totals.UnitsLost} lost; {totals.Deliveries} deliveries, {totals.Returns} returns; outbox {loop.Outbox.Emitted} emitted, {loop.Outbox.Acknowledged} acknowledged ({config.Connectivity.Profile})"));
        }

        FinalState.Write(database, store, state, clock, loop.Sales);

        var counts = CanonicalDump.Counts(database.Connection);
        var reportPath = DistributionReport.Write(arguments.OutputDirectory, database.Connection, config, calendar, store, window, seed, loop.Outbox, latentDemandPath);
        var manifestPath = Path.Combine(arguments.OutputDirectory, "manifest.json");
        WriteManifest(manifestPath, arguments, configPath, catalogue, config, window, seed, storeId, counts, loop.Outbox, [latentDemandPath, reportPath]);

        log.WriteLine($"  done: {counts.Values.Sum()} rows, manifest at {manifestPath}");
        return new RunResult(database.FilePath, latentDemandPath, reportPath, manifestPath, storeId, seed, counts);
    }

    private static void WriteManifest(
        string path,
        GeneratorArguments arguments,
        string configPath,
        Catalogue catalogue,
        GeneratorConfig config,
        RunWindow window,
        long seed,
        string storeId,
        IReadOnlyDictionary<string, long> counts,
        Outbox outbox,
        IReadOnlyList<string> outputs)
    {
        var catalogueFiles = Directory.GetFiles(catalogue.Directory)
            .Order(StringComparer.Ordinal)
            .ToDictionary(file => Path.GetFileName(file), file => Hash(InputFile.ReadAllBytes(file)), StringComparer.Ordinal);

        var manifest = new RunManifest(
            Generator: "Waymark.Generator",
            Seed: seed,
            SeedOverridden: arguments.Seed is not null,
            Config: configPath,
            ConfigHash: Hash(InputFile.ReadAllBytes(configPath)),
            Catalogue: catalogue.Directory,
            CatalogueHashes: catalogueFiles,
            CommissioningDate: window.CommissioningDate,
            FirstDay: window.FirstDay,
            LastDay: window.LastDay,
            Days: window.Days,
            StoreId: storeId,
            StepsCompleted: Steps,
            ParameterSources: ParameterSources(config),
            RowCounts: counts,
            Connectivity: config.Connectivity.Profile,
            Outbox: new OutboxSummary(outbox.Emitted, outbox.Acknowledged, outbox.Batches, outbox.Backlog),
            Outputs: outputs.ToDictionary(file => Path.GetFileName(file), file => Hash(File.ReadAllBytes(file)), StringComparer.Ordinal));

        File.WriteAllText(path, JsonSerializer.Serialize(manifest, GeneratorJson.Options));
    }

    /// <summary>How many parameters come from each source, so the manifest says how much of the store is a guess.</summary>
    internal static SortedDictionary<string, int> ParameterSources(GeneratorConfig config)
    {
        var sources = new List<ParameterSource>();
        Collect(config, sources);

        return new SortedDictionary<string, int>(
            sources.GroupBy(s => s.ToString().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Count()),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Every sourced parameter in the configuration tree, found by walking it, so a parameter
    /// added later is counted without anyone remembering to list it.
    /// </summary>
    private static void Collect(object? node, List<ParameterSource> sources)
    {
        switch (node)
        {
            case null or string:
                return;

            case ISourced sourced:
                sources.Add(sourced.Source);
                return;

            case System.Collections.IDictionary dictionary:
                foreach (var value in dictionary.Values)
                {
                    Collect(value, sources);
                }

                return;

            case System.Collections.IEnumerable items:
                foreach (var item in items)
                {
                    Collect(item, sources);
                }

                return;
        }

        var type = node.GetType();
        if (type.Namespace != typeof(GeneratorConfig).Namespace)
        {
            return;
        }

        foreach (var property in type.GetProperties())
        {
            Collect(property.GetValue(node), sources);
        }
    }

    private static string Hash(byte[] bytes) =>
        Mixing.Fnv1a(bytes).ToString("x16", CultureInfo.InvariantCulture);
}

/// <summary>The run manifest (D-046 §35): enough to reproduce a store and to know what it is.</summary>
internal sealed record RunManifest(
    string Generator,
    long Seed,
    bool SeedOverridden,
    string Config,
    string ConfigHash,
    string Catalogue,
    IReadOnlyDictionary<string, string> CatalogueHashes,
    DateOnly CommissioningDate,
    DateOnly FirstDay,
    DateOnly LastDay,
    int Days,
    string StoreId,
    IReadOnlyList<string> StepsCompleted,
    IReadOnlyDictionary<string, int> ParameterSources,
    IReadOnlyDictionary<string, long> RowCounts,
    ConnectivityProfile Connectivity,
    OutboxSummary Outbox,
    IReadOnlyDictionary<string, string> Outputs);

/// <summary>The outbox over the run: emitted, acknowledged, round trips, and the backlog at the end of every day.</summary>
internal sealed record OutboxSummary(long Emitted, long Acknowledged, long Batches, IReadOnlyList<BacklogDay> Backlog);
