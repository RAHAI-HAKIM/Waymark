using System.Globalization;
using Waymark.Generator.Calendar;
using Waymark.Generator.Configuration;
using Waymark.Generator.Simulation;
using static Waymark.Generator.Tests.Sql;

namespace Waymark.Generator.Tests;

/// <summary>
/// Restocking (W10 S6): the naive shopkeeper's rule on its own, then the orders, deliveries,
/// batches and write-offs it produces, held to D-046's invariants.
/// </summary>
public sealed class SupplyTests(MiniSalesRun mini, GrocerySalesRun grocery) : IClassFixture<MiniSalesRun>, IClassFixture<GrocerySalesRun>
{
    private GeneratedStoreFixture Store(string name) => name == "mini" ? mini : grocery;

    // ------------------------------------------------------------ the policy

    private static NaiveShopkeeperPolicy Policy() => new(TestInputs.Load(TestInputs.GroceryConfig).Config.Supply);

    [Fact]
    public void The_perceived_pace_is_remembered_sales_with_the_base_rate_filling_days_before_the_history()
    {
        // grocery-dz: memory_days 14.
        var policy = Policy();

        Assert.Equal(2.0, policy.PerceivedRate([.. Enumerable.Repeat(2, 14)], baseRate: 9), 12);
        Assert.Equal(((4 * 4) + (10 * 1.5)) / 14.0, policy.PerceivedRate([4, 4, 4, 4], baseRate: 1.5), 12);
        Assert.Equal(1.0, policy.PerceivedRate([.. Enumerable.Repeat(0, 30).Concat(Enumerable.Repeat(1, 14))], baseRate: 5), 12);
    }

    [Theory]
    // grocery-dz: reorder at 4 days of pace, up to 12 days, slow movers below 0.3 a day, ×1.5 before Ramadan.
    [InlineData(2.0, 9, 12, false, 0)]     // 9 on hand is above the 8-unit threshold: no order
    [InlineData(2.0, 8, 12, false, 2)]     // at the threshold: up to 24, needs 16, two cartons of 12
    [InlineData(2.0, 8, 10, false, 2)]     // needs 16 in cartons of 10: rounded up to 2
    [InlineData(2.0, 0, 48, false, 1)]     // needs 24 in cartons of 48: never less than one carton
    [InlineData(2.0, 8, 12, true, 3)]      // before Ramadan: up to 36, needs 28, three cartons
    [InlineData(0.2, 1, 12, false, 0)]     // a slow mover with anything left is ignored
    [InlineData(0.2, 0, 12, false, 1)]     // until it runs out: one carton
    public void The_shopkeeper_reorders_below_his_threshold_in_whole_cartons(double pace, long position, int carton, bool ramadan, int expected)
    {
        Assert.Equal(expected, Policy().CartonsToOrder(pace, position, carton, ramadan));
    }

    [Theory]
    [InlineData("2025-02-07", false)]   // Ramadan's stock-up (from 22/02) is 15 days away
    [InlineData("2025-02-08", true)]    // 14 days away: the look-ahead reaches it
    [InlineData("2025-02-25", true)]    // the stock-up week itself
    [InlineData("2025-03-05", false)]   // during Ramadan he orders by what he sees
    [InlineData("2025-04-20", false)]
    public void Over_ordering_starts_when_Ramadan_is_within_the_look_ahead(string date, bool expected)
    {
        var (config, catalogue, _) = TestInputs.Load(TestInputs.GroceryConfig);
        var calendar = new CalendarModel(config.Calendar, catalogue.Store);

        Assert.Equal(expected, SupplyChain.RamadanComing(calendar, DateOnly.Parse(date, CultureInfo.InvariantCulture), config.Supply.RamadanLookaheadDays.Value));
    }

    // ------------------------------------------------------------ the data

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Orders_are_placed_on_the_suppliers_delivery_days_and_arrive_after_its_lead_time(string store)
    {
        var fixture = Store(store);
        using var db = fixture.Open();
        var (config, catalogue, _) = TestInputs.Load(fixture.ConfigPath);
        var calendar = new CalendarModel(config.Calendar, catalogue.Store);
        var suppliers = catalogue.Suppliers.ToDictionary(s => s.Code, StringComparer.Ordinal);

        var orders = Rows(db, """
            SELECT s.supplier_code, o.order_date, (SELECT min(b.received_date) FROM batches b WHERE b.order_id = o.order_id), o.status
            FROM purchase_orders o JOIN suppliers s USING (supplier_id)
            """);
        Assert.NotEmpty(orders);
        Assert.Contains(orders, o => o[2] is string);

        foreach (var order in orders)
        {
            var supplier = suppliers[(string)order[0]];
            var ordered = DateOnly.ParseExact((string)order[1], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            Assert.Contains(ordered.DayOfWeek, supplier.DeliveryDays);

            if (order[2] is not string receivedText)
            {
                continue;
            }

            // No earlier than the stated lead time; no later than the latest delay, moved on past closed days.
            var received = DateOnly.ParseExact(receivedText, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var latest = ordered.AddDays(Math.Max(1, supplier.StatedLeadTimeDays + config.Supply.LateDeliveryMaxDays.Value));
            while (!calendar.Day(latest).IsOpen)
            {
                latest = latest.AddDays(1);
            }

            Assert.InRange(received, ordered.AddDays(Math.Max(1, supplier.StatedLeadTimeDays)), latest);
        }
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Nothing_is_received_beyond_what_was_ordered_and_batches_hold_exactly_what_was_received(string store)
    {
        using var db = Store(store).Open();

        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM purchase_order_items WHERE quantity_received > quantity_ordered OR quantity_received % 1000 <> 0"));

        // Cartons received × units per carton = units in the order's batches, line by line.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM purchase_order_items poi
            WHERE (poi.quantity_received / 1000) * poi.units_per_purchase_unit <>
                  coalesce((SELECT sum(bi.quantity_received) FROM batch_items bi JOIN batches b USING (batch_id)
                            WHERE b.order_id = poi.order_id AND bi.variant_id = poi.variant_id), 0)
            """));

        // An order's status says what arrived.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM purchase_orders o
            JOIN (SELECT order_id, sum(quantity_received = quantity_ordered) AS full, sum(quantity_received > 0) AS some, count(*) AS n
                  FROM purchase_order_items GROUP BY order_id) i USING (order_id)
            WHERE (o.status = 'received' AND i.full <> i.n)
               OR (o.status = 'partially_received' AND (i.some = 0 OR i.full = i.n))
               OR (o.status = 'cancelled' AND i.some > 0)
               OR (o.status = 'sent' AND (i.some > 0 OR o.received_at IS NOT NULL))
               OR (o.status IN ('received', 'partially_received') AND o.received_at IS NULL)
               OR o.total_amount <> (SELECT sum(line_total) FROM purchase_order_items x WHERE x.order_id = o.order_id)
            """));

        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM stock_movements m WHERE m.movement_type = 'receipt' AND m.reference_type = 'purchase_order' AND NOT EXISTS (SELECT 1 FROM batches b WHERE b.batch_id = m.batch_id AND b.order_id = m.reference_id)"));
        Assert.True(Scalar(db, "SELECT count(*) FROM purchase_orders WHERE status = 'partially_received'") > 0, "No delivery ever came short, so the fill rate was never exercised.");
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Every_batch_expires_on_or_after_it_was_received(string store)
    {
        using var db = Store(store).Open();

        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM batches WHERE expiration_date IS NOT NULL AND expiration_date < received_date"));
        Assert.True(Scalar(db, "SELECT count(*) FROM batches WHERE order_id IS NOT NULL") > 0);
    }

    [Fact]
    public void Expired_stock_is_written_off_the_morning_after_its_last_day_of_sale()
    {
        using var db = grocery.Open();

        Assert.True(Scalar(db, "SELECT count(*) FROM stock_movements WHERE movement_type = 'expiry'") > 0, "Nothing expired in seven weeks, so write-offs were never exercised.");
        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM stock_movements m JOIN batches b USING (batch_id) WHERE m.movement_type = 'expiry' AND m.movement_date <= b.expiration_date"));

        // Written off whole: after its expiry movement, a batch line holds nothing.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM inventories i JOIN batches b USING (batch_id)
            WHERE i.quantity <> 0 AND b.expiration_date < (SELECT max(movement_date) FROM stock_movements)
            """));
        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM batches b WHERE b.status = 'written_off' AND NOT EXISTS (SELECT 1 FROM stock_movements m WHERE m.batch_id = b.batch_id AND m.movement_type = 'expiry')"));
    }

    [Fact]
    public void Stockouts_happen_and_the_shelf_is_restocked_after_them()
    {
        using var db = grocery.Open();
        var rows = SalesTests.ReadCsv(grocery.Result.LatentDemandPath);
        var last = rows.Max(r => r.Date);

        var receipts = Rows(db, "SELECT v.sku, m.movement_date FROM stock_movements m JOIN variants v USING (variant_id) WHERE m.movement_type = 'receipt'")
            .ToLookup(row => (string)row[0], row => DateOnly.ParseExact((string)row[1], "yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparer.Ordinal);

        // An empty shelf that turned customers away, early enough for a delivery to follow.
        var stockouts = rows.Where(r => r.OnHandClose == 0 && r.Lost > 0 && r.Date <= last.AddDays(-21)).ToList();
        Assert.NotEmpty(stockouts);

        Assert.All(stockouts, stockout => Assert.True(
            receipts[stockout.Sku].Any(date => date > stockout.Date && date <= stockout.Date.AddDays(21)),
            $"{stockout.Sku} ran out on {stockout.Date:yyyy-MM-dd} and was not delivered again within three weeks."));
    }
}
