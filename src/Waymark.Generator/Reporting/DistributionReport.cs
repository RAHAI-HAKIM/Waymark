using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Waymark.Generator.Calendar;
using Waymark.Generator.Configuration;
using Waymark.Generator.Simulation;

namespace Waymark.Generator.Reporting;

/// <summary>
/// <c>report.md</c>: the run's distributions, which are what D-046 says a reviewer reads
/// instead of the code. Read back from the finished database and <c>latent-demand.csv</c>, not
/// from the simulation's memory, so the report describes the store as a consumer would find it.
///
/// <para>
/// Deterministic, like everything else a run writes: no wall-clock time, no machine name, and
/// invariant formatting.
/// </para>
/// </summary>
internal static class DistributionReport
{
    public const string FileName = "report.md";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Write(
        string outputDirectory,
        SqliteConnection db,
        GeneratorConfig config,
        CalendarModel calendar,
        GeneratedStore store,
        RunWindow window,
        long seed,
        Outbox outbox,
        string latentDemandPath)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(outbox);

        var md = new StringBuilder();
        string M(long minorUnits) => Money(minorUnits, store.Currency.Code);
        var offset = new DateTimeOffset(window.FirstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) - calendar.ToUtc(window.FirstDay, 0);
        var days = Enumerable.Range(0, window.Days).Select(i => calendar.Day(window.FirstDay.AddDays(i))).ToDictionary(d => d.Date);
        var openDays = days.Values.Count(d => d.IsOpen);

        // Completed sales only: not voided ringings, not refunds.
        var sales = Rows(db, """
            SELECT t.occurred_at, t.total_amount, (SELECT sum(quantity) FROM transaction_items i WHERE i.transaction_id = t.transaction_id)
            FROM transactions t WHERE t.status <> 'voided' AND t.original_transaction_id IS NULL
            """).Select(row =>
            {
                var local = Utc((string)row[0]) + offset;
                return (Date: DateOnly.FromDateTime(local.DateTime), Hour: local.Hour, Total: L(row[1]), Units: L(row[2]) / 1000);
            }).ToList();

        var latent = ReadLatent(latentDemandPath);

        Line(md, $"# {store.StoreCode}: generated store report");
        Line(md);
        Line(md, $"Seed **{seed}** · commissioned {window.CommissioningDate:yyyy-MM-dd} · **{window.Days} days** from {window.FirstDay:yyyy-MM-dd} to {window.LastDay:yyyy-MM-dd} · connectivity **{Snake(config.Connectivity.Profile)}**.");
        Line(md);
        Line(md, "Every behavioural number behind these figures is in the configuration with its source; the manifest counts them. This report is for reading the distributions (D-046), not for trusting them.");
        Line(md);

        // ---------------------------------------------------------------- trading
        var revenue = S(db, "SELECT coalesce(sum(total_amount), 0) FROM transactions WHERE status <> 'voided'");
        Line(md, "## Trading");
        Line(md);
        Table(md, ["", "Value"], [
            ["Open days", $"{openDays} of {window.Days}"],
            ["Sales (completed, excluding refunds)", N(sales.Count)],
            ["Sales per open day", D(sales.Count / (double)Math.Max(1, openDays), 1)],
            ["Units sold", N(sales.Sum(s => s.Units))],
            ["Units per basket (mean)", D(sales.Count == 0 ? 0 : sales.Average(s => (double)s.Units), 2)],
            ["Basket value (mean)", sales.Count == 0 ? "—" : M((long)sales.Average(s => (double)s.Total))],
            ["Net revenue after refunds, TTC", M(revenue)],
            ["TVA collected", M(S(db, "SELECT coalesce(sum(tax_total), 0) FROM transactions WHERE status <> 'voided'"))],
        ]);

        // ---------------------------------------------------------------- shape
        Line(md, "## When customers come");
        Line(md);
        Line(md, "Share of sales per local hour, ordinary days against Ramadan days.");
        Line(md);
        var normal = sales.Where(s => days[s.Date].DayType is CalendarModel.Normal or CalendarModel.Friday).ToList();
        var ramadan = sales.Where(s => days[s.Date].DayType == CalendarModel.RamadanDayType).ToList();
        var hours = Enumerable.Range(0, 24).Where(h => sales.Any(s => s.Hour == h)).ToList();
        Table(md, ["Hour", "Ordinary days", "", "Ramadan days", ""], [.. hours.Select(h =>
        {
            var n = Share(normal.Count(s => s.Hour == h), normal.Count);
            var r = Share(ramadan.Count(s => s.Hour == h), ramadan.Count);
            return new[] { $"{h:00}h", Percent(n), Bar(n, 0.25), Percent(r), Bar(r, 0.25) };
        })]);

        Line(md, "Sales per open day by weekday, ordinary weeks only (no Ramadan, Aïd or events).");
        Line(md);
        var ordinary = days.Values.Where(d => d.IsOpen && d.RamadanPhase == RamadanPhase.None && d.Events.Count == 0).ToList();
        var ordinaryDates = ordinary.Select(d => d.Date).ToHashSet();
        Table(md, ["Weekday", "Open days", "Sales per day", "Index"], [.. Enum.GetValues<DayOfWeek>().Select(w =>
        {
            var weekdays = ordinary.Where(d => d.Date.DayOfWeek == w).Select(d => d.Date).ToHashSet();
            var perDay = weekdays.Count == 0 ? 0 : sales.Count(s => weekdays.Contains(s.Date)) / (double)weekdays.Count;
            var all = ordinary.Count == 0 ? 0 : sales.Count(s => ordinaryDates.Contains(s.Date)) / (double)ordinary.Count;
            return new[] { w.ToString(), N(weekdays.Count), D(perDay, 1), all == 0 ? "—" : D(perDay / all, 2) };
        })]);

        // ---------------------------------------------------------------- baskets
        Line(md, "## Basket sizes");
        Line(md);
        Line(md, "Units per completed sale against the configured table. A sale can hold fewer units than its planned basket when the shelf ran out.");
        Line(md);
        var weights = config.Sales.BasketUnits.Value;
        var weightTotal = weights.Sum();
        (string Label, int From, int To)[] buckets = [("1", 1, 1), ("2", 2, 2), ("3", 3, 3), ("4", 4, 4), ("5", 5, 5), ("6", 6, 6), ("7", 7, 7), ("8", 8, 8), ("9", 9, 9), ("10", 10, 10), ("11–13", 11, 13), ("14–20", 14, 20), ("21–30", 21, 30), ("31–50", 31, 50), ("over 50", 51, int.MaxValue)];
        Table(md, ["Units", "Sales", "Share", "", "Configured"], [.. buckets.Select(b =>
        {
            var share = Share(sales.Count(s => s.Units >= b.From && s.Units <= b.To), sales.Count);
            var configured = weights.Select((w, i) => (w, size: i + 1)).Where(x => x.size >= b.From && x.size <= b.To).Sum(x => x.w) / weightTotal;
            return new[] { b.Label, N(sales.Count(s => s.Units >= b.From && s.Units <= b.To)), Percent(share), Bar(share, 0.15), Percent(configured) };
        })]);

        // ---------------------------------------------------------------- calendar
        Line(md, "## Ramadan and the pay cycle");
        Line(md);
        Table(md, ["Period", "Days", "Sales per day", "Units wanted per day", "Units sold per day"], [.. new (string Label, Func<DayContext, bool> Is)[]
        {
            ("Ordinary", d => d.RamadanPhase == RamadanPhase.None),
            ("Stock-up week", d => d.RamadanPhase == RamadanPhase.StockUp),
            ("Ramadan", d => d.RamadanPhase == RamadanPhase.Ramadan),
            ("Aïd el-Fitr", d => d.RamadanPhase == RamadanPhase.Aid),
            ("Week after Aïd", d => d.RamadanPhase == RamadanPhase.PostAid),
            ("Payday spike", d => d.RamadanPhase == RamadanPhase.None && d.PaydayPhase == PaydayPhase.Spike),
            ("Pre-payday week", d => d.RamadanPhase == RamadanPhase.None && d.PaydayPhase == PaydayPhase.PrePayday),
            ("Mid-month trough", d => d.RamadanPhase == RamadanPhase.None && d.PaydayPhase == PaydayPhase.Trough),
        }.Select(p =>
        {
            var set = days.Values.Where(d => d.IsOpen && p.Is(d)).Select(d => d.Date).ToHashSet();
            if (set.Count == 0)
            {
                return new[] { p.Label, "0", "—", "—", "—" };
            }

            var wanted = latent.Where(r => set.Contains(r.Date)).Sum(r => (long)r.Latent);
            var sold = latent.Where(r => set.Contains(r.Date)).Sum(r => (long)r.Sold + r.SoldAsSubstitute);
            return new[] { p.Label, N(set.Count), D(sales.Count(s => set.Contains(s.Date)) / (double)set.Count, 1), D(wanted / (double)set.Count, 0), D(sold / (double)set.Count, 0) };
        })]);

        // ---------------------------------------------------------------- service
        Line(md, "## Demand against the shelf");
        Line(md);
        var wantedAll = latent.Sum(r => (long)r.Latent);
        var stockoutDays = latent.Count(r => r.StoreOpen && r.Lost > 0 && r.OnHandClose == 0);
        Table(md, ["", "Units", "Share of demand"], [
            ["Wanted (latent demand)", N(wantedAll), "100%"],
            ["Sold as wanted", N(latent.Sum(r => (long)r.Sold)), Percent(Share(latent.Sum(r => (long)r.Sold), wantedAll))],
            ["Bought as a substitute", N(latent.Sum(r => (long)r.Substituted)), Percent(Share(latent.Sum(r => (long)r.Substituted), wantedAll))],
            ["Lost", N(latent.Sum(r => (long)r.Lost)), Percent(Share(latent.Sum(r => (long)r.Lost), wantedAll))],
        ]);
        Line(md, $"Stockout variant-days (demand lost, shelf empty at close): **{N(stockoutDays)}** of {N(latent.Count(r => r.StoreOpen))} open variant-days.");
        Line(md);
        Table(md, ["Month", "Wanted", "Lost", "Lost share"], [.. latent.GroupBy(r => r.Date.ToString("yyyy-MM", Invariant)).Select(g =>
        {
            var w = g.Sum(r => (long)r.Latent);
            var lost = g.Sum(r => (long)r.Lost);
            return new[] { g.Key, N(w), N(lost), Percent(Share(lost, w)) };
        })]);

        // ---------------------------------------------------------------- supply
        Line(md, "## Supply and spoilage");
        Line(md);
        var received = S(db, "SELECT coalesce(sum(quantity_changed), 0) FROM stock_movements WHERE movement_type = 'receipt'") / 1000;
        var expired = -S(db, "SELECT coalesce(sum(quantity_changed), 0) FROM stock_movements WHERE movement_type = 'expiry'") / 1000;
        Table(md, ["", "Value"], [
            ["Purchase orders", string.Join(" · ", Rows(db, "SELECT status, count(*) FROM purchase_orders GROUP BY 1 ORDER BY 1").Select(r => $"{r[0]} {r[1]}"))],
            ["Cartons received / ordered (received and partial orders)", Percent(Share(
                S(db, "SELECT coalesce(sum(i.quantity_received), 0) FROM purchase_order_items i JOIN purchase_orders o USING (order_id) WHERE o.status IN ('received', 'partially_received')"),
                S(db, "SELECT coalesce(sum(i.quantity_ordered), 0) FROM purchase_order_items i JOIN purchase_orders o USING (order_id) WHERE o.status IN ('received', 'partially_received')")))],
            ["Units received (opening stock included)", N(received)],
            ["Units written off expired", $"{N(expired)} ({Percent(Share(expired, received))} of received)"],
            ["Value written off expired, at cost", M(-S(db, "SELECT coalesce(sum(quantity_changed * unit_cost / 1000), 0) FROM stock_movements WHERE movement_type = 'expiry'"))],
            ["Stock counts posted", $"{N(S(db, "SELECT count(*) FROM stock_counts"))}, variance {N(S(db, "SELECT coalesce(sum(variance_quantity), 0) FROM stock_count_items") / 1000)} units, {M(S(db, "SELECT coalesce(sum(variance_value), 0) FROM stock_count_items"))}"],
        ]);
        Line(md, "Days from order to delivery, received orders:");
        Line(md);
        var leads = Rows(db, "SELECT julianday((SELECT min(received_date) FROM batches b WHERE b.order_id = o.order_id)) - julianday(o.order_date) FROM purchase_orders o WHERE o.status IN ('received', 'partially_received')")
            .Select(r => (int)Math.Round(Convert.ToDouble(r[0], Invariant))).ToList();
        Table(md, ["Days", "Orders", ""], [.. leads.GroupBy(d => d).OrderBy(g => g.Key).Select(g => new[] { N(g.Key), N(g.Count()), Bar(Share(g.Count(), leads.Count), 0.5) })]);

        // ---------------------------------------------------------------- money
        Line(md, "## Payments and the till");
        Line(md);
        var paymentTotal = S(db, "SELECT coalesce(sum(p.amount), 0) FROM transaction_payments p JOIN transactions t USING (transaction_id) WHERE t.original_transaction_id IS NULL");
        Table(md, ["Method (sales)", "Payments", "Value", "Share of value"], [.. Rows(db, """
            SELECT p.payment_method, count(*), sum(p.amount) FROM transaction_payments p JOIN transactions t USING (transaction_id)
            WHERE t.original_transaction_id IS NULL GROUP BY 1 ORDER BY 3 DESC
            """).Select(r => new[] { (string)r[0], N(L(r[1])), M(L(r[2])), Percent(Share(L(r[2]), paymentTotal)) })]);
        Table(md, ["", "Value"], [
            ["Tender rounding (rounding_variance)", $"{N(S(db, "SELECT count(*) FROM rounding_variance"))} rows, net {M(S(db, "SELECT coalesce(sum(amount), 0) FROM rounding_variance"))}"],
            ["Drawer miscounts (cash_sessions.variance)", $"{N(S(db, "SELECT count(*) FROM cash_sessions WHERE variance <> 0"))} of {N(S(db, "SELECT count(*) FROM cash_sessions"))} closes, net {M(S(db, "SELECT coalesce(sum(variance), 0) FROM cash_sessions"))}"],
            ["Paid in / paid out / dropped", string.Join(" · ", Rows(db, "SELECT movement_type, count(*), sum(amount) FROM cash_movements GROUP BY 1 ORDER BY 1").Select(r => $"{r[0]} {r[1]} ({M(L(r[2]))})"))],
            ["Discounted lines", $"{N(S(db, "SELECT count(*) FROM transaction_items i JOIN transactions t USING (transaction_id) WHERE i.discount_amount > 0 AND i.quantity > 0 AND t.status <> 'voided'"))}, {M(S(db, "SELECT coalesce(sum(i.discount_amount), 0) FROM transaction_items i JOIN transactions t USING (transaction_id) WHERE i.quantity > 0 AND t.status <> 'voided'"))}"],
            ["Voided ringings", N(S(db, "SELECT count(*) FROM transactions WHERE status = 'voided'"))],
            ["Returns", $"{N(S(db, "SELECT count(*) FROM returns"))}, {M(S(db, "SELECT coalesce(sum(refund_amount), 0) FROM returns"))}, {Percent(Share(S(db, "SELECT count(*) FROM returns WHERE restock_flag = 1"), S(db, "SELECT count(*) FROM returns")))} restocked"],
            ["Store credit held at the end", M(S(db, "SELECT coalesce(sum(credit), 0) FROM customers"))],
            ["Sales rung up against a regular", Percent(Share(S(db, "SELECT count(*) FROM transactions WHERE status <> 'voided' AND original_transaction_id IS NULL AND customer_id IS NOT NULL"), sales.Count))],
        ]);

        // ---------------------------------------------------------------- the tab
        Line(md, "## The tab (on account)");
        Line(md);
        var payday = config.Calendar.Payday;
        var repaid = Rows(db, "SELECT date(occurred_at) FROM receivable_movements WHERE movement_type = 'payment'")
            .Select(r => DateOnly.ParseExact((string)r[0], "yyyy-MM-dd", Invariant)).ToList();
        var inSpike = repaid.Count(day => day.Day <= payday.SpikeFirstDaysOfMonth.Value
            || day.Day > DateTime.DaysInMonth(day.Year, day.Month) - payday.SpikeLastDaysOfMonth.Value);
        Table(md, ["", "Value"], [
            ["Customers with a tab", $"{N(S(db, "SELECT count(*) FROM customers WHERE credit_limit IS NOT NULL"))} of {N(S(db, "SELECT count(*) FROM customers"))}, limits {M(S(db, "SELECT coalesce(min(credit_limit), 0) FROM customers"))} to {M(S(db, "SELECT coalesce(max(credit_limit), 0) FROM customers"))}"],
            ["Charged (sales)", $"{N(S(db, "SELECT count(*) FROM receivable_movements WHERE movement_type = 'charge' AND amount > 0"))}, {M(S(db, "SELECT coalesce(sum(amount), 0) FROM receivable_movements WHERE movement_type = 'charge' AND amount > 0"))}"],
            ["Taken off by refunds", $"{N(S(db, "SELECT count(*) FROM receivable_movements WHERE movement_type = 'charge' AND amount < 0"))}, {M(-S(db, "SELECT coalesce(sum(amount), 0) FROM receivable_movements WHERE movement_type = 'charge' AND amount < 0"))}"],
            ["Repaid", $"{N(repaid.Count)}, {M(-S(db, "SELECT coalesce(sum(amount), 0) FROM receivable_movements WHERE movement_type = 'payment'"))}, {Percent(Share(inSpike, repaid.Count))} in the payday spike"],
            ["Change written off", $"{N(S(db, "SELECT count(*) FROM receivable_movements WHERE movement_type = 'write_off'"))}, {M(-S(db, "SELECT coalesce(sum(amount), 0) FROM receivable_movements WHERE movement_type = 'write_off'"))}"],
            ["Owed at the end", $"{M(S(db, "SELECT coalesce(sum(amount), 0) FROM receivable_movements"))} by {N(S(db, "SELECT count(*) FROM (SELECT 1 FROM receivable_movements GROUP BY customer_id HAVING sum(amount) > 0)"))} customers"],
            ["Tabs never repaid", N(S(db, "SELECT count(*) FROM (SELECT 1 FROM receivable_movements GROUP BY customer_id HAVING sum(movement_type = 'payment') = 0 AND sum(amount) > 0)"))],
            ["Tabs at 90% of their limit or more at the end", N(S(db, "SELECT count(*) FROM customers c WHERE c.credit_limit IS NOT NULL AND (SELECT coalesce(sum(amount), 0) FROM receivable_movements m WHERE m.customer_id = c.customer_id) * 10 >= c.credit_limit * 9"))],
        ]);

        // ---------------------------------------------------------------- sync
        Line(md, "## The outbox");
        Line(md);
        var peak = outbox.Backlog.Count == 0 ? null : outbox.Backlog.MaxBy(b => b.Max);
        Table(md, ["", "Value"], [
            ["Profile", Snake(config.Connectivity.Profile)],
            ["Anonymous baskets emitted", N(outbox.Emitted)],
            ["Acknowledged by the cloud", N(outbox.Acknowledged)],
            ["Round trips carrying a batch", N(outbox.Batches)],
            ["Peak backlog", peak is null ? "—" : $"{N(peak.Max)} on {peak.Date:yyyy-MM-dd}"],
            ["Backlog at the end", N(outbox.Backlog.Count == 0 ? 0 : outbox.Backlog[^1].End)],
        ]);

        var path = Path.Combine(outputDirectory, FileName);
        File.WriteAllText(path, md.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
        return path;
    }

    private static List<LatentDemandRow> ReadLatent(string path) =>
        [.. File.ReadLines(path).Skip(1).Select(line =>
        {
            var f = line.Split(',');
            return new LatentDemandRow(
                DateOnly.ParseExact(f[0], "yyyy-MM-dd", Invariant), f[1], f[2] == "1",
                long.Parse(f[3], Invariant), int.Parse(f[4], Invariant), int.Parse(f[5], Invariant),
                int.Parse(f[6], Invariant), int.Parse(f[7], Invariant), int.Parse(f[8], Invariant), long.Parse(f[9], Invariant));
        })];

    private static List<object[]> Rows(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
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

    private static long S(SqliteConnection db, string sql) => L(Rows(db, sql)[0][0]);

    private static long L(object value) => value is DBNull ? 0 : Convert.ToInt64(value, Invariant);

    private static DateTimeOffset Utc(string text) =>
        DateTimeOffset.ParseExact(text, "yyyy-MM-dd HH:mm:ss", Invariant, DateTimeStyles.AssumeUniversal);

    private static void Line(StringBuilder md, string text = "") => md.Append(text).Append('\n');

    private static void Table(StringBuilder md, IReadOnlyList<string> header, IReadOnlyList<string[]> rows)
    {
        Line(md, "| " + string.Join(" | ", header) + " |");
        Line(md, "| " + string.Join(" | ", header.Select((_, i) => i == 0 ? ":---" : "---:")) + " |");
        foreach (var row in rows)
        {
            Line(md, "| " + string.Join(" | ", row) + " |");
        }

        Line(md);
    }

    private static double Share(long part, long whole) => whole == 0 ? 0 : part / (double)whole;

    private static string Percent(double share) => (share * 100).ToString("0.0", Invariant) + "%";

    private static string N(long value) => value.ToString("#,0", Invariant).Replace(",", " ", StringComparison.Ordinal);

    private static string D(double value, int decimals) => value.ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), Invariant);

    private static string Money(long minorUnits, string currency) =>
        (minorUnits / 100m).ToString("#,0.00", Invariant).Replace(",", " ", StringComparison.Ordinal) + " " + currency;

    /// <summary>A text bar for a share, full width at <paramref name="full"/>.</summary>
    private static string Bar(double share, double full) => new('█', (int)Math.Round(Math.Min(1, share / full) * 20));

    private static string Snake(ConnectivityProfile profile) => profile switch
    {
        ConnectivityProfile.AlwaysOn => "always_on",
        ConnectivityProfile.Flaky => "flaky",
        _ => "offline_stretch",
    };
}
