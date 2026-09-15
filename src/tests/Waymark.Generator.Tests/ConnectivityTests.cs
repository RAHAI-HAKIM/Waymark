using System.Globalization;
using System.Text.Json;
using Waymark.Generator.Configuration;
using Waymark.Generator.Writing;
using static Waymark.Generator.Tests.Sql;

namespace Waymark.Generator.Tests;

/// <summary>The mini shop for 60 days under each connectivity profile, from the same seed.</summary>
public sealed class ConnectivityRuns : IDisposable
{
    private readonly ScratchDirectory _scratch = new();

    public ConnectivityRuns()
    {
        RunResult Run(ConnectivityProfile profile, int days) => GeneratorRun.Execute(
            new GeneratorArguments(TestInputs.MiniConfig, Path.Combine(_scratch.Path, $"{profile}-{days}"), null, days) { Connectivity = profile },
            TextWriter.Null);

        AlwaysOn = Run(ConnectivityProfile.AlwaysOn, 60);
        Flaky = Run(ConnectivityProfile.Flaky, 60);
        Offline = Run(ConnectivityProfile.OfflineStretch, 60);

        // The mini config's outage starts on day 20 for 10 days: a 25-day run ends inside it.
        EndsOffline = Run(ConnectivityProfile.OfflineStretch, 25);
        EndsOnline = Run(ConnectivityProfile.AlwaysOn, 25);
    }

    /// <summary>The same 25 days always on: its outbox is empty where <see cref="EndsOffline"/>'s is full.</summary>
    internal RunResult EndsOnline { get; }

    internal RunResult AlwaysOn { get; }

    internal RunResult Flaky { get; }

    internal RunResult Offline { get; }

    internal RunResult EndsOffline { get; }

    public void Dispose()
    {
        _scratch.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// The outbox and its drain (W10 S8, D-043, D-046 §31): emission is unconditional and
/// anonymous, the drain follows the profile, and no profile changes a single sale.
/// </summary>
public sealed class ConnectivityTests(ConnectivityRuns runs) : IClassFixture<ConnectivityRuns>
{
    private static readonly string[] SyncTables = ["outbox", "sync_state"];

    private static JsonElement Manifest(RunResult result) => JsonDocument.Parse(File.ReadAllText(result.ManifestPath)).RootElement;

    private static List<(DateOnly Date, long Max, long End)> Backlog(RunResult result) =>
        [.. Manifest(result).GetProperty("outbox").GetProperty("backlog").EnumerateArray().Select(day => (
            DateOnly.ParseExact(day.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            day.GetProperty("max").GetInt64(),
            day.GetProperty("end").GetInt64()))];

    private static Dictionary<string, string> SyncState(RunResult result)
    {
        using var db = Open(result.DatabasePath);
        return Rows(db, "SELECT state_key, state_value FROM sync_state").ToDictionary(r => (string)r[0], r => (string)r[1], StringComparer.Ordinal);
    }

    private static long Sales(RunResult result, string where = "1 = 1")
    {
        using var db = Open(result.DatabasePath);
        return Scalar(db, $"SELECT count(*) FROM transactions WHERE status <> 'voided' AND original_transaction_id IS NULL AND {where}");
    }

    [Fact]
    public void The_same_seed_under_every_profile_writes_identical_sales_and_demand()
    {
        using var always = Open(runs.AlwaysOn.DatabasePath);
        using var flaky = Open(runs.Flaky.DatabasePath);
        using var offline = Open(runs.Offline.DatabasePath);

        var reference = CanonicalDump.Render(always, SyncTables);
        Assert.Equal(reference, CanonicalDump.Render(flaky, SyncTables));
        Assert.Equal(reference, CanonicalDump.Render(offline, SyncTables));
        Assert.Equal(File.ReadAllBytes(runs.AlwaysOn.LatentDemandPath), File.ReadAllBytes(runs.Flaky.LatentDemandPath));
        Assert.Equal(File.ReadAllBytes(runs.AlwaysOn.LatentDemandPath), File.ReadAllBytes(runs.Offline.LatentDemandPath));

        // …and the profiles really did differ where they are allowed to.
        Assert.NotEqual(Backlog(runs.AlwaysOn), Backlog(runs.Flaky));
        Assert.NotEqual(Backlog(runs.AlwaysOn), Backlog(runs.Offline));
    }

    [Fact]
    public void A_run_ending_offline_differs_from_one_ending_online_only_in_the_outbox_and_sync_state()
    {
        using var online = Open(runs.EndsOnline.DatabasePath);
        using var offline = Open(runs.EndsOffline.DatabasePath);

        Assert.Equal(CanonicalDump.Render(online, SyncTables), CanonicalDump.Render(offline, SyncTables));
        Assert.NotEqual(CanonicalDump.Render(online), CanonicalDump.Render(offline));
    }

    [Fact]
    public void Always_on_emits_every_sale_and_ends_every_day_with_an_empty_outbox()
    {
        var sales = Sales(runs.AlwaysOn);
        var state = SyncState(runs.AlwaysOn);

        Assert.True(sales > 0);
        Assert.Equal(sales.ToString(CultureInfo.InvariantCulture), state["last_sequence"]);
        Assert.Equal(sales.ToString(CultureInfo.InvariantCulture), state["last_acked_sequence"]);
        Assert.All(Backlog(runs.AlwaysOn), day => Assert.Equal(0, day.End));

        using var db = Open(runs.AlwaysOn.DatabasePath);
        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM outbox"));
    }

    [Fact]
    public void An_offline_stretch_builds_its_backlog_from_that_stretchs_baskets_then_drains()
    {
        var backlog = Backlog(runs.Offline);
        var first = backlog[0].Date;
        var start = first.AddDays(20);
        var end = start.AddDays(10);

        using var db = Open(runs.Offline.DatabasePath);
        var during = Scalar(db, $"SELECT count(*) FROM transactions WHERE status <> 'voided' AND original_transaction_id IS NULL AND date(occurred_at, '+1 hour') >= '{start:yyyy-MM-dd}' AND date(occurred_at, '+1 hour') < '{end:yyyy-MM-dd}'");
        var dayAfter = Scalar(db, $"SELECT count(*) FROM transactions WHERE status <> 'voided' AND original_transaction_id IS NULL AND date(occurred_at, '+1 hour') = '{end:yyyy-MM-dd}'");

        // Every basket of the stretch waits; the first open morning after it may add a few before its first drain.
        var peak = backlog.Max(day => day.Max);
        Assert.True(during > 0, "The mini shop sold nothing during its outage.");
        Assert.InRange(peak, during, during + dayAfter);
        Assert.All(backlog.Where(day => day.Date < start || day.Date >= end), day => Assert.Equal(0, day.End));
        Assert.Contains(backlog, day => day.Date >= start && day.Date < end && day.End > 0);

        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM outbox"));
        Assert.Equal(SyncState(runs.Offline)["last_sequence"], SyncState(runs.Offline)["last_acked_sequence"]);
    }

    [Fact]
    public void A_run_that_ends_offline_leaves_a_gapless_queue_of_anonymous_baskets_with_their_failed_attempts()
    {
        using var db = Open(runs.EndsOffline.DatabasePath);
        var state = SyncState(runs.EndsOffline);
        var acked = long.Parse(state["last_acked_sequence"], CultureInfo.InvariantCulture);
        var last = long.Parse(state["last_sequence"], CultureInfo.InvariantCulture);

        var queued = Rows(db, "SELECT sequence_number, channel, message_type, entity_type, entity_id, is_priority, attempts, last_attempt_at, last_error, payload_json FROM outbox ORDER BY sequence_number");
        Assert.NotEmpty(queued);
        Assert.Equal(Enumerable.Range((int)acked + 1, (int)(last - acked)).Select(n => (long)n), queued.Select(row => Long(row[0])));
        Assert.Equal(Sales(runs.EndsOffline), last);

        Assert.All(queued, row =>
        {
            Assert.Equal("A_statistics", row[1]);
            Assert.Equal("anonymous_basket", row[2]);
            Assert.IsType<DBNull>(row[3]);
            Assert.IsType<DBNull>(row[4]);
            Assert.Equal(0L, row[5]);
            Assert.True(Long(row[6]) > 0, "A message queued through the outage never counted a failed attempt.");
            Assert.IsType<string>(row[7]);
            Assert.IsType<string>(row[8]);
        });

        // The payload carries exactly D-043's basket fields: no customer, no transaction, no time finer than the hour.
        var transactionIds = Column(db, "SELECT transaction_id FROM transactions").ToHashSet(StringComparer.Ordinal);
        var payloads = queued.Select(row => JsonDocument.Parse((string)row[9]).RootElement).ToList();
        Assert.All(payloads, payload =>
        {
            Assert.Equal(
                ["basket_id", "date", "day_of_week", "has_discount", "hour_bucket", "lines", "payment_class", "store_id"],
                payload.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.False(transactionIds.Contains(payload.GetProperty("basket_id").GetString()!), "A basket id is a transaction id, so the anonymous stream joins back to the till.");
            Assert.All(payload.GetProperty("lines").EnumerateArray(), line =>
                Assert.Equal(["line_value", "product_id", "quantity"], line.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
        });

        // The queued baskets are the last sales, basket for basket: date, hour, weekday, total and discount flag.
        var lastSales = Rows(db, $"""
            SELECT date(occurred_at, '+1 hour'), CAST(strftime('%H', occurred_at, '+1 hour') AS INTEGER), CAST(strftime('%w', occurred_at, '+1 hour') AS INTEGER), total_amount,
                   EXISTS (SELECT 1 FROM transaction_items i WHERE i.transaction_id = t.transaction_id AND i.discount_amount > 0)
            FROM transactions t WHERE status <> 'voided' AND original_transaction_id IS NULL
            ORDER BY occurred_at, transaction_id LIMIT {queued.Count} OFFSET {acked}
            """).Select(r => $"{r[0]} {r[1]} {r[2]} {Long(r[3])} {Long(r[4]) == 1}");
        var fromPayloads = payloads.Select(p =>
            $"{p.GetProperty("date").GetString()} {p.GetProperty("hour_bucket").GetInt32()} {p.GetProperty("day_of_week").GetInt32()} "
            + $"{p.GetProperty("lines").EnumerateArray().Sum(l => decimal.Parse(l.GetProperty("line_value").GetString()!, CultureInfo.InvariantCulture) * 100):0} {p.GetProperty("has_discount").GetBoolean()}");
        Assert.Equal(lastSales, fromPayloads);
    }

    [Fact]
    public void A_flaky_link_leaves_a_backlog_on_some_evenings_and_catches_up()
    {
        var backlog = Backlog(runs.Flaky);

        Assert.Contains(backlog, day => day.End > 0);
        Assert.Contains(backlog.Skip(1), day => day.End == 0);
        var manifest = Manifest(runs.Flaky).GetProperty("outbox");
        Assert.True(manifest.GetProperty("acknowledged").GetInt64() <= manifest.GetProperty("emitted").GetInt64());
        Assert.Equal(Sales(runs.Flaky), manifest.GetProperty("emitted").GetInt64());
    }
}
