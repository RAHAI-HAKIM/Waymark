using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.Generator.Calendar;
using Waymark.Generator.Randomness;
using Waymark.Generator.Simulation;

namespace Waymark.Generator.Tests;

/// <summary>
/// The pieces of a trading day on their own (W10 S5): demand, baskets, arrivals, FIFO and the
/// line arithmetic — and the property the design rests on, that latent demand never depends
/// on stock.
/// </summary>
public sealed class DemandTests(MiniSalesRun mini) : IClassFixture<MiniSalesRun>
{
    private static readonly DateOnly Salon = new(2025, 2, 20);

    // ------------------------------------------------------------ demand

    [Theory]
    [InlineData("2025-02-20", 0.6 * 1.2 * 0.8 * 1.0 * 1.5)]   // Thursday, February, pre-payday, the salon
    [InlineData("2025-03-05", 0.6 * 1.0 * 1.0 * 1.1 * 0.7)]   // Wednesday, March, payday spike, Ramadan
    [InlineData("2025-02-21", 0.0)]                           // A Friday: this shop is closed
    public void Mean_demand_is_the_base_rate_times_every_calendar_factor(string date, double expected)
    {
        var (demand, calendar, variant) = MiniDemand();

        Assert.Equal(expected, demand.Mean(variant, calendar.Day(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture))), 12);
    }

    [Fact]
    public void Latent_demand_is_one_draw_at_its_own_address()
    {
        var (demand, calendar, variant) = MiniDemand();
        var day = calendar.Day(Salon);
        var draws = new RandomSource(7).Stream("demand");

        Assert.Equal(Distributions.Poisson(draws.Uniform(variant.Index, Salon.DayNumber), demand.Mean(variant, day)), demand.Latent(variant, day));
    }

    [Fact]
    public void Latent_demand_does_not_depend_on_stock()
    {
        // D-046 §14: the same seed with a fraction of the opening stock must want exactly the
        // same units every day, and sell fewer of them.
        using var scratch = new ScratchDirectory();
        var config = scratch.CopyMini();
        scratch.Edit("configs/mini.json", "\"cover_days\": { \"value\": { \"min\": 10, \"max\": 20 }", "\"cover_days\": { \"value\": { \"min\": 1, \"max\": 1 }");
        var starved = GeneratorRun.Execute(new GeneratorArguments(config, Path.Combine(scratch.Path, "store"), null, 60), TextWriter.Null);

        var full = SalesTests.ReadCsv(mini.Result.LatentDemandPath);
        var low = SalesTests.ReadCsv(starved.LatentDemandPath);

        Assert.Equal(full.Select(r => (r.Date, r.Sku, r.Latent)), low.Select(r => (r.Date, r.Sku, r.Latent)));
        Assert.True(low.Sum(r => r.Sold) < full.Sum(r => r.Sold), "Starving the shelf sold no less, so stock never capped a sale.");
        Assert.True(low.Sum(r => r.Lost) > full.Sum(r => r.Lost));
    }

    // ------------------------------------------------------------ baskets

    [Fact]
    public void Baskets_hold_exactly_the_days_demand_in_arrival_order_within_opening_hours()
    {
        var (config, catalogue, _) = TestInputs.Load(TestInputs.GroceryConfig);
        var calendar = new CalendarModel(config.Calendar, catalogue.Store);
        var assembler = new BasketAssembler(config.Sales, calendar, FakeStore(customers: 20), new RandomSource(42));

        foreach (var date in new[] { new DateOnly(2025, 1, 15), new DateOnly(2025, 1, 17), new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 30) })
        {
            var day = calendar.Day(date);
            var latent = Enumerable.Range(0, 60).Select(i => (i * 7) % 5).ToArray();
            var baskets = assembler.Plan(day, latent);
            var intervals = calendar.OpeningIntervals(day.DayType);

            var units = new int[latent.Length];
            foreach (var line in baskets.SelectMany(b => b.Lines))
            {
                units[line.VariantIndex] += line.Units;
            }

            Assert.Equal(latent, units);
            Assert.Equal(Enumerable.Range(0, baskets.Count), baskets.Select(b => b.Number).Order());
            Assert.Equal(baskets.OrderBy(b => b.SecondOfDay).ThenBy(b => b.Number), baskets);
            Assert.All(baskets, basket =>
            {
                Assert.NotEmpty(basket.Lines);
                Assert.Equal(basket.Lines.Count, basket.Lines.Select(l => l.VariantIndex).Distinct().Count());
                Assert.Contains(intervals, i => basket.SecondOfDay >= i.StartMinute * 60 && basket.SecondOfDay < i.EndMinute * 60);
            });
        }
    }

    [Fact]
    public void Basket_size_follows_the_table_scaled_by_the_pay_cycle()
    {
        var (config, catalogue, _) = TestInputs.Load(TestInputs.GroceryConfig);
        var calendar = new CalendarModel(config.Calendar, catalogue.Store);
        var assembler = new BasketAssembler(config.Sales, calendar, FakeStore(customers: 0), new RandomSource(42));
        var weights = config.Sales.BasketUnits.Value;
        var tableMean = weights.Select((w, i) => w * (i + 1)).Sum() / weights.Sum();

        foreach (var date in new[] { new DateOnly(2025, 1, 15), new DateOnly(2025, 1, 2) })
        {
            var day = calendar.Day(date);
            var mean = Enumerable.Range(0, 40_000).Average(number => assembler.BasketSize(day, number));
            Assert.Equal(tableMean * day.Payday.BasketSize, mean, 0.1);
        }
    }

    [Theory]
    [InlineData(12, 12 * 3600, (12 * 3600) + (15 * 60))]   // Friday: open 12:00–12:15 only
    [InlineData(14, (14 * 3600) + (30 * 60), 15 * 3600)]   // Friday: reopens at 14:30
    [InlineData(9, 9 * 3600, 10 * 3600)]
    public void An_arrival_falls_only_in_the_open_seconds_of_its_hour(int hour, int from, int to)
    {
        OpeningInterval[] friday = [new(7 * 60, (12 * 60) + 15), new((14 * 60) + 30, 22 * 60)];
        var seconds = Enumerable.Range(0, 2000).Select(i => BasketAssembler.ArrivalSecond(friday, hour, (i + 0.5) / 2000)).ToList();

        Assert.All(seconds, second => Assert.InRange(second, from, to - 1));
        Assert.Equal(from, seconds.Min());
        Assert.Equal(to - 1, seconds.Max());
    }

    // ------------------------------------------------------------ the shelf

    [Fact]
    public void Stock_is_taken_oldest_first_and_an_expired_lot_is_never_taken()
    {
        var piece = UnitPrecision.For("piece", 0);
        var state = new StoreState(1);
        var old = new StockLot("old", new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 10), Money.FromMinorUnits(100, Currency.Dzd), piece.Whole(5));
        var fresh = new StockLot("fresh", new DateOnly(2025, 1, 5), null, Money.FromMinorUnits(120, Currency.Dzd), piece.Whole(5));
        state.Receive(0, old);
        state.Receive(0, fresh);

        var taken = state.Take(0, new DateOnly(2025, 1, 10), piece.Whole(7));
        Assert.Equal([("old", 5000L), ("fresh", 2000L)], taken.Select(t => (t.Lot.BatchId, t.Taken.Thousandths)));

        var refill = new StockLot("late", new DateOnly(2025, 1, 2), new DateOnly(2025, 1, 10), Money.FromMinorUnits(100, Currency.Dzd), piece.Whole(4));
        state.Receive(0, refill);
        Assert.Equal(3000, state.Sellable(0, new DateOnly(2025, 1, 11)));
        Assert.Equal(7000, state.OnHand(0));
        Assert.Equal([("fresh", 3000L)], state.Take(0, new DateOnly(2025, 1, 11), piece.Whole(3)).Select(t => (t.Lot.BatchId, t.Taken.Thousandths)));
        Assert.Throws<InvalidOperationException>(() => state.Take(0, new DateOnly(2025, 1, 11), piece.Whole(1)));
    }

    [Theory]
    [InlineData(0, 170_000, 27_143)]
    [InlineData(1_000, 169_000, 26_983)]
    public void A_line_rounds_its_gross_once_takes_the_discount_then_derives_TVA_by_subtraction(long discount, long total, long tax)
    {
        var line = SaleArithmetic.Line(
            Money.FromMinorUnits(85_000, Currency.Dzd),
            UnitPrecision.For("piece", 0).Whole(2),
            Money.FromMinorUnits(discount, Currency.Dzd),
            BasisPoints.StandardVat,
            Rounding.HalfEven);

        Assert.Equal(170_000, line.Gross.MinorUnits);
        Assert.Equal(total, line.LineTotal.MinorUnits);
        Assert.Equal(tax, line.Split.Tax.MinorUnits);
        Assert.Equal(line.LineTotal, line.Split.Net + line.Split.Tax);
    }

    // ------------------------------------------------------------ helpers

    private static (DemandModel Demand, CalendarModel Calendar, GeneratedVariant Variant) MiniDemand()
    {
        var (config, catalogue, _) = TestInputs.Load(TestInputs.MiniConfig);
        var calendar = new CalendarModel(config.Calendar, catalogue.Store);
        var item = catalogue.Variants[0];
        var variant = new GeneratedVariant
        {
            Index = 0,
            VariantId = "v",
            ProductId = "p",
            SupplierId = "s",
            Catalogue = item,
            Vat = BasisPoints.StandardVat,
            SellingUnit = UnitPrecision.For("piece", 0),
            Prices = [new PricePoint(new DateOnly(2025, 1, 1), item.RetailPrice, item.PurchasePrice)],
        };

        return (new DemandModel(config.SeasonalityProfiles, new RandomSource(7)), calendar, variant);
    }

    private static GeneratedStore FakeStore(int customers)
    {
        var staff = new GeneratedStaff("staff", "Staff", "owner");
        return new GeneratedStore
        {
            StoreId = "store",
            StoreCode = "T-001",
            RoundingPolicy = Rounding.HalfUp,
            Currency = Currency.Dzd,
            TerminalIds = ["till"],
            Staff = [staff],
            Manager = staff,
            SupplierIds = new Dictionary<string, string>(),
            Suppliers = [],
            Subcategories = [],
            ReasonCodes = new Dictionary<string, Catalogues.ReasonCodeDefinition>(),
            ProductIds = new Dictionary<string, string>(),
            Variants = [],
            Customers = [.. Enumerable.Range(1, customers).Select(n => new GeneratedCustomer($"c{n}", n, false, false))],
            Notices = new Dictionary<NoticeType, string>(),
        };
    }
}
