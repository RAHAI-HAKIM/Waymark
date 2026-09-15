using System.Globalization;
using Waymark.Domain.Values;
using Waymark.Generator.Calendar;
using Waymark.Generator.Reporting;
using static Waymark.Generator.Tests.Sql;

namespace Waymark.Generator.Tests;

/// <summary>
/// The trading days (W10 S5), held to D-046's pass/fail invariants on both stores: the
/// hardware shop under <c>half_even</c> and the grocery under <c>half_up</c>. Each invariant
/// recomputes from the stored rows rather than trusting a column the generator also wrote.
/// </summary>
public sealed class SalesTests(MiniSalesRun mini, GrocerySalesRun grocery) : IClassFixture<MiniSalesRun>, IClassFixture<GrocerySalesRun>
{
    private GeneratedStoreFixture Store(string name) => name == "mini" ? mini : grocery;

    [Theory]
    [InlineData("mini", "half_even")]
    [InlineData("grocery", "half_up")]
    public void Every_line_recomputes_from_its_price_quantity_VAT_and_the_stamped_policy(string store, string policy)
    {
        using var db = Store(store).Open();

        // D-053: the policy on the transaction is the store's, copied, never defaulted.
        Assert.Equal([policy], Column(db, "SELECT DISTINCT rounding_policy FROM transactions"));
        Assert.Equal([policy], Column(db, "SELECT rounding_policy FROM stores"));

        var lines = Rows(db, """
            SELECT i.sell_price, i.quantity, i.discount_amount, i.tax_amount, i.line_total, c.tax_rate, t.rounding_policy
            FROM transaction_items i
            JOIN transactions t USING (transaction_id)
            JOIN variants v USING (variant_id)
            JOIN product_category pc ON pc.product_id = v.product_id AND pc.is_primary = 1
            JOIN categories c ON c.category_id = pc.category_id
            """);
        Assert.NotEmpty(lines);

        foreach (var line in lines)
        {
            // A refund line has a negative quantity, and a discount returned with it comes back
            // too, so the discount is subtracted in the direction of the quantity.
            var rounding = (string)line[6] == "half_even" ? Rounding.HalfEven : Rounding.HalfUp;
            var ttc = Money.FromMinorUnits(Long(line[0]), Currency.Dzd).Times(Long(line[1]), Quantity.Scale, rounding)
                - Money.FromMinorUnits(Math.Sign(Long(line[1])) * Long(line[2]), Currency.Dzd);
            var split = ttc.SplitTaxInclusive(new BasisPoints((int)Long(line[5])), rounding);

            Assert.Equal(ttc.MinorUnits, Long(line[4]));
            Assert.Equal(split.Tax.MinorUnits, Long(line[3]));
            Assert.Equal(Long(line[4]), split.Net.MinorUnits + Long(line[3]));
        }
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void A_transaction_is_the_sum_of_its_lines_and_its_payments_cover_it_exactly(string store)
    {
        using var db = Store(store).Open();

        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM transactions t
            JOIN (SELECT transaction_id, sum(line_total) AS total, sum(tax_amount) AS tax,
                         sum(CASE WHEN quantity < 0 THEN -discount_amount ELSE discount_amount END) AS discount
                  FROM transaction_items GROUP BY transaction_id) i USING (transaction_id)
            WHERE t.total_amount <> i.total OR t.tax_total <> i.tax OR t.subtotal <> i.total - i.tax
               OR t.subtotal + t.tax_total <> t.total_amount OR t.discount_total <> i.discount
            """));
        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM transactions t WHERE NOT EXISTS (SELECT 1 FROM transaction_items i WHERE i.transaction_id = t.transaction_id)"));

        // Every rung transaction is paid to the centime, however many tenders; a voided one is not paid at all.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM transactions t
            WHERE (t.status <> 'voided' AND coalesce((SELECT sum(amount) FROM transaction_payments p WHERE p.transaction_id = t.transaction_id), 0) <> t.total_amount)
               OR (t.status = 'voided' AND EXISTS (SELECT 1 FROM transaction_payments p WHERE p.transaction_id = t.transaction_id))
            """));
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM (SELECT transaction_id, count(*) AS n, max(sequence) AS last FROM transaction_payments GROUP BY transaction_id)
            WHERE n <> last
            """));
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Every_cash_session_closes_on_the_D034_invariant_and_tender_rounding_stays_out_of_its_variance(string store)
    {
        using var db = Store(store).Open();

        // expected_cash = float + Σcash + Σpaid_in − Σpaid_out − Σdrops + Σtender_variance, and the
        // session's variance is the miscount alone: counted − expected.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM cash_sessions s
            WHERE s.status <> 'closed'
               OR s.expected_cash <> s.opening_float
                  + coalesce((SELECT sum(p.amount) FROM transactions t JOIN transaction_payments p USING (transaction_id)
                              WHERE t.cash_session_id = s.session_id AND p.payment_method = 'cash'), 0)
                  + coalesce((SELECT sum(CASE m.movement_type WHEN 'paid_in' THEN m.amount ELSE -m.amount END)
                              FROM cash_movements m WHERE m.session_id = s.session_id), 0)
                  + coalesce((SELECT sum(v.amount) FROM rounding_variance v JOIN transactions t ON t.transaction_id = v.reference_id
                              WHERE t.cash_session_id = s.session_id), 0)
               OR s.variance <> s.counted_cash - s.expected_cash
            """));
        Assert.True(Scalar(db, "SELECT count(*) FROM cash_movements WHERE movement_type IN ('paid_in', 'paid_out', 'drop')") > 0);

        // A miscount is whole 5 DZD steps; tender rounding never is, so it cannot be hiding in there.
        Assert.True(Scalar(db, "SELECT count(*) FROM cash_sessions WHERE variance <> 0") > 0, "No drawer ever miscounted, so the variance column was never tested.");
        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM cash_sessions WHERE variance % 500 <> 0"));

        // The drawer takes each cash payment rounded to the 5 DZD step, nearest; only cash rounds.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM transactions t
            LEFT JOIN (SELECT transaction_id, sum(amount) AS cash FROM transaction_payments WHERE payment_method = 'cash' GROUP BY transaction_id) p USING (transaction_id)
            LEFT JOIN rounding_variance v ON v.reference_id = t.transaction_id
            WHERE (p.cash IS NULL AND v.variance_id IS NOT NULL)
               OR (p.cash IS NOT NULL AND ((p.cash + coalesce(v.amount, 0)) % 500 <> 0 OR abs(coalesce(v.amount, 0)) > 250))
               OR (v.variance_id IS NOT NULL AND (v.source <> 'cash_tender' OR v.reference_type <> 'transaction' OR v.policy <> t.rounding_policy))
            """));

        // Both catalogues carry prices off the cash step (Hakim, 14/09), so rounding is exercised rather than assumed.
        Assert.True(Scalar(db, "SELECT count(*) FROM rounding_variance") > 0);
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Each_day_closes_at_the_previous_close_plus_that_days_movements(string store)
    {
        var fixture = Store(store);
        using var db = fixture.Open();

        var movements = Rows(db, "SELECT v.sku, m.movement_date, sum(m.quantity_changed) FROM stock_movements m JOIN variants v USING (variant_id) GROUP BY 1, 2")
            .ToDictionary(row => ((string)row[0], (string)row[1]), row => Long(row[2]));
        var close = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var row in ReadCsv(fixture.Result.LatentDemandPath))
        {
            var previous = close.GetValueOrDefault(row.Sku);
            var deltas = movements.GetValueOrDefault((row.Sku, row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            Assert.True(
                row.OnHandClose * Quantity.Scale == (previous * Quantity.Scale) + deltas,
                $"{row.Sku} on {row.Date:yyyy-MM-dd}: closed at {row.OnHandClose}, but opened at {previous} with {deltas} thousandths of movements.");
            close[row.Sku] = row.OnHandClose;
        }

        var inventories = Rows(db, "SELECT v.sku, sum(i.quantity) FROM inventories i JOIN variants v USING (variant_id) GROUP BY 1")
            .ToDictionary(row => (string)row[0], row => Long(row[1]));
        Assert.Equal(close.Where(c => c.Value != 0).OrderBy(c => c.Key, StringComparer.Ordinal).ToList(),
            inventories.Select(i => KeyValuePair.Create(i.Key, i.Value / Quantity.Scale)).Where(c => c.Value != 0).OrderBy(c => c.Key, StringComparer.Ordinal).ToList());
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void No_batch_ever_sells_more_than_it_received(string store)
    {
        using var db = Store(store).Open();

        // The running level of every batch, movement by movement in the order they happened.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM (
                SELECT sum(quantity_changed) OVER (PARTITION BY variant_id, batch_id ORDER BY created_at, movement_id) AS level
                FROM stock_movements)
            WHERE level < 0
            """));
        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM inventories WHERE quantity < 0"));
        Assert.True(Scalar(db, "SELECT count(*) FROM stock_movements WHERE movement_type = 'sale'") > 0);
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Every_sale_line_has_a_matching_sale_movement_from_the_same_batch(string store)
    {
        using var db = Store(store).Open();

        // Sold lines only: a voided line moved no stock, and a refund line is matched by its return.
        const string SoldLines = "JOIN transactions t USING (transaction_id) WHERE t.status <> 'voided' AND i.quantity > 0";
        Assert.Equal(
            Scalar(db, $"SELECT count(*) FROM transaction_items i {SoldLines}"),
            Scalar(db, "SELECT count(*) FROM stock_movements WHERE movement_type = 'sale'"));
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM transaction_items i
            JOIN transactions t USING (transaction_id)
            WHERE t.status <> 'voided' AND i.quantity > 0
              AND (i.batch_id IS NULL OR i.unit_cost_at_sale IS NULL
                   OR NOT EXISTS (SELECT 1 FROM stock_movements m
                                  WHERE m.movement_type = 'sale' AND m.reference_type = 'transaction' AND m.reference_id = i.transaction_id
                                    AND m.variant_id = i.variant_id AND m.batch_id = i.batch_id AND m.quantity_changed = -i.quantity
                                    AND m.unit_cost = i.unit_cost_at_sale AND m.staff_id = t.staff_id AND m.created_at = t.occurred_at))
            """));
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Every_completed_transaction_has_a_gapless_invoice_number_in_time_order(string store)
    {
        using var db = Store(store).Open();

        // Sales and refunds share the gapless sequence; a voided ringing never gets a number.
        var invoices = Column(db, "SELECT invoice_number FROM transactions WHERE status <> 'voided' ORDER BY occurred_at, transaction_id");
        Assert.NotEmpty(invoices);
        Assert.Equal(Scalar(db, "SELECT count(*) FROM transactions WHERE invoice_number IS NOT NULL"), invoices.Count);
        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM transactions WHERE status = 'voided' AND invoice_number IS NOT NULL"));

        var code = Column(db, "SELECT store_code FROM stores")[0];
        foreach (var year in invoices.GroupBy(number => number[(code.Length + 1)..(code.Length + 5)], StringComparer.Ordinal))
        {
            Assert.Equal(
                Enumerable.Range(1, year.Count()).Select(n => string.Create(CultureInfo.InvariantCulture, $"{code}-{year.Key}-{n:000000}")),
                year);
        }
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Customers_are_served_only_while_the_store_is_open(string store)
    {
        var fixture = Store(store);
        using var db = fixture.Open();
        var (config, catalogue, _) = TestInputs.Load(fixture.ConfigPath);
        var calendar = new CalendarModel(config.Calendar, catalogue.Store);
        var offset = TimeSpan.FromHours(catalogue.Store.UtcOffsetHours);

        foreach (var at in Column(db, "SELECT occurred_at FROM transactions").Select(value => Timestamp(value).ToOffset(offset)))
        {
            var date = DateOnly.FromDateTime(at.DateTime);
            var second = (int)at.TimeOfDay.TotalSeconds;
            var intervals = calendar.OpeningIntervals(calendar.Day(date).DayType);
            Assert.True(
                intervals.Any(i => second >= i.StartMinute * 60 && second < i.EndMinute * 60),
                $"A sale at {at:yyyy-MM-dd HH:mm:ss} local falls outside the {calendar.Day(date).DayType} opening hours.");
        }

        // Every open day has one closed drawer per till, and a closed day has none.
        var openDays = ReadCsv(fixture.Result.LatentDemandPath).Where(r => r.StoreOpen).Select(r => r.Date).Distinct().Count();
        Assert.Equal(openDays * Scalar(db, "SELECT count(*) FROM terminals"), Scalar(db, "SELECT count(*) FROM cash_sessions"));
    }

    [Fact]
    public void A_shop_closed_on_fridays_sells_nothing_on_an_ordinary_friday()
    {
        using var db = mini.Open();
        var (config, catalogue, _) = TestInputs.Load(mini.ConfigPath);
        var calendar = new CalendarModel(config.Calendar, catalogue.Store);

        // A Ramadan Friday takes Ramadan hours (the calendar's day-type priority), so only
        // days of type friday are closed.
        var closed = ReadCsv(mini.Result.LatentDemandPath)
            .Where(r => calendar.Day(r.Date).DayType == CalendarModel.Friday)
            .ToList();
        var fridaysWithSales = Column(db, "SELECT DISTINCT date(occurred_at, '+1 hour') FROM transactions")
            .Select(d => DateOnly.ParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Count(d => calendar.Day(d).DayType == CalendarModel.Friday);

        Assert.NotEmpty(closed);
        Assert.Equal(0, fridaysWithSales);
        Assert.All(closed, row =>
        {
            Assert.False(row.StoreOpen);
            Assert.Equal(0, row.Latent);
        });
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void No_expired_unit_is_sold(string store)
    {
        using var db = Store(store).Open();

        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM stock_movements m JOIN batches b USING (batch_id)
            WHERE m.movement_type = 'sale' AND b.expiration_date IS NOT NULL AND m.movement_date > b.expiration_date
            """));
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Latent_demand_is_fully_accounted_for_and_matches_what_the_database_sold(string store)
    {
        var fixture = Store(store);
        using var db = fixture.Open();

        var sold = Rows(db, "SELECT v.sku, m.movement_date, -sum(m.quantity_changed) FROM stock_movements m JOIN variants v USING (variant_id) WHERE m.movement_type = 'sale' GROUP BY 1, 2")
            .ToDictionary(row => ((string)row[0], (string)row[1]), row => Long(row[2]));
        var product = Rows(db, "SELECT v.sku, v.product_id FROM variants v").ToDictionary(row => (string)row[0], row => (string)row[1]);
        var rows = ReadCsv(fixture.Result.LatentDemandPath);

        Assert.Equal(Scalar(db, "SELECT count(*) FROM variants") * rows.Select(r => r.Date).Distinct().Count(), rows.Count);

        foreach (var row in rows)
        {
            Assert.Equal(row.Latent, row.Sold + row.Substituted + row.Lost);
            Assert.True(row.Lost >= 0 && row.Sold >= 0 && row.Substituted >= 0, $"{row.Sku} {row.Date}: a negative count.");
            Assert.Equal(
                sold.GetValueOrDefault((row.Sku, row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
                (long)(row.Sold + row.SoldAsSubstitute) * Quantity.Scale);
        }

        // Substitution stays inside a product: what one variant lost to a sibling, a sibling sold.
        foreach (var day in rows.GroupBy(r => (r.Date, Product: product[r.Sku])))
        {
            Assert.Equal(day.Sum(r => r.Substituted), day.Sum(r => r.SoldAsSubstitute));
        }
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Every_sale_id_carries_the_moment_it_happened(string store)
    {
        using var db = Store(store).Open();

        AssertIdsMatch(db, "SELECT transaction_id, occurred_at FROM transactions");
        AssertIdsMatch(db, "SELECT transaction_item_id, created_at FROM transaction_items");
        AssertIdsMatch(db, "SELECT session_id, opened_at FROM cash_sessions");
        AssertIdsMatch(db, "SELECT shift_id, created_at FROM shifts");
    }

    [Fact]
    public void Customers_are_attached_to_some_sales_and_every_payment_method_is_used()
    {
        using var db = grocery.Open();

        const string Sales = "status <> 'voided' AND original_transaction_id IS NULL";
        var attached = Scalar(db, $"SELECT count(*) FROM transactions WHERE {Sales} AND customer_id IS NOT NULL");
        var total = Scalar(db, $"SELECT count(*) FROM transactions WHERE {Sales}");
        Assert.InRange(attached / (double)total, 0.14, 0.22);
        Assert.Superset(
            new HashSet<string>(StringComparer.Ordinal) { "card", "cash", "mobile_wallet", "on_account" },
            Column(db, "SELECT DISTINCT payment_method FROM transaction_payments").ToHashSet(StringComparer.Ordinal));
        Assert.Equal(3, Scalar(db, "SELECT count(DISTINCT staff_id) FROM transactions"));
    }

    internal static List<LatentDemandRow> ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        Assert.Equal(LatentDemandCsv.Header, lines[0]);

        return [.. lines.Skip(1).Select(line =>
        {
            var f = line.Split(',');
            return new LatentDemandRow(
                DateOnly.ParseExact(f[0], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                f[1],
                f[2] == "1",
                long.Parse(f[3], CultureInfo.InvariantCulture),
                int.Parse(f[4], CultureInfo.InvariantCulture),
                int.Parse(f[5], CultureInfo.InvariantCulture),
                int.Parse(f[6], CultureInfo.InvariantCulture),
                int.Parse(f[7], CultureInfo.InvariantCulture),
                int.Parse(f[8], CultureInfo.InvariantCulture),
                long.Parse(f[9], CultureInfo.InvariantCulture));
        })];
    }
}
