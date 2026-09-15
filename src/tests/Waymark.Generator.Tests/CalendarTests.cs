using Waymark.Generator.Calendar;

namespace Waymark.Generator.Tests;

/// <summary>
/// The calendar reports what kind of day a date is, exactly on the configured boundaries
/// (D-046 §15, §17). Off-by-one here shifts Ramadan demand by a day for every product.
/// </summary>
public sealed class CalendarTests
{
    // The grocery configuration: Ramadan 1446 is 1–29 March 2025, Aïd 30–31 March, stock-up
    // and quiet weeks of 7 days; payday spike = last 2 and first 5 days, pre-payday 7 days
    // before that, trough days 10–19.
    private static readonly CalendarModel Grocery = GroceryCalendar();

    private static CalendarModel GroceryCalendar()
    {
        var (config, catalogue, _) = TestInputs.Load(TestInputs.GroceryConfig);
        return new CalendarModel(config.Calendar, catalogue.Store);
    }

    private static DateOnly D(int month, int day, int year = 2025) => new(year, month, day);

    [Theory]
    [InlineData(2, 21, nameof(RamadanPhase.None))]
    [InlineData(2, 22, nameof(RamadanPhase.StockUp))]
    [InlineData(2, 28, nameof(RamadanPhase.StockUp))]
    [InlineData(3, 1, nameof(RamadanPhase.Ramadan))]
    [InlineData(3, 29, nameof(RamadanPhase.Ramadan))]
    [InlineData(3, 30, nameof(RamadanPhase.Aid))]
    [InlineData(3, 31, nameof(RamadanPhase.Aid))]
    [InlineData(4, 1, nameof(RamadanPhase.PostAid))]
    [InlineData(4, 7, nameof(RamadanPhase.PostAid))]
    [InlineData(4, 8, nameof(RamadanPhase.None))]
    public void Ramadan_phases_start_and_end_on_the_configured_days(int month, int day, string expected)
    {
        Assert.Equal(Enum.Parse<RamadanPhase>(expected), Grocery.Day(D(month, day)).RamadanPhase);
    }

    [Fact]
    public void Ramadan_days_are_counted_from_one()
    {
        Assert.Equal(1, Grocery.Day(D(3, 1)).RamadanDay);
        Assert.Equal(29, Grocery.Day(D(3, 29)).RamadanDay);
        Assert.Null(Grocery.Day(D(3, 30)).RamadanDay);
    }

    [Theory]
    [InlineData(1, 30, nameof(PaydayPhase.Spike))]
    [InlineData(1, 31, nameof(PaydayPhase.Spike))]
    [InlineData(2, 5, nameof(PaydayPhase.Spike))]
    [InlineData(2, 6, nameof(PaydayPhase.Normal))]
    [InlineData(2, 10, nameof(PaydayPhase.Trough))]
    [InlineData(2, 19, nameof(PaydayPhase.Trough))]
    [InlineData(2, 20, nameof(PaydayPhase.PrePayday))]
    [InlineData(2, 26, nameof(PaydayPhase.PrePayday))]
    [InlineData(2, 27, nameof(PaydayPhase.Spike))]
    [InlineData(4, 22, nameof(PaydayPhase.PrePayday))]
    [InlineData(4, 29, nameof(PaydayPhase.Spike))]
    public void The_pay_cycle_follows_the_length_of_each_month(int month, int day, string expected)
    {
        // February's spike starts on the 27th and April's on the 29th: "the last two days"
        // is counted from the real month end, not a fixed date.
        Assert.Equal(Enum.Parse<PaydayPhase>(expected), Grocery.Day(D(month, day)).PaydayPhase);
    }

    [Fact]
    public void Each_phase_carries_its_configured_effect()
    {
        Assert.Equal(2.0, Grocery.Day(D(2, 20)).Payday.OnAccount);
        Assert.Equal(0.88, Grocery.Day(D(2, 12)).Payday.BasketSize);
    }

    [Theory]
    [InlineData(1, 7, "normal")]   // Tuesday
    [InlineData(1, 10, "friday")]
    [InlineData(3, 7, "ramadan")]  // a Friday in Ramadan is a Ramadan day
    [InlineData(3, 30, "aid")]
    [InlineData(6, 6, "aid")]      // Aïd al-Adha, set by the event's day_type
    public void The_day_type_follows_aid_then_ramadan_then_friday(int month, int day, string expected)
    {
        Assert.Equal(expected, Grocery.Day(D(month, day)).DayType);
    }

    [Fact]
    public void Events_are_active_for_their_whole_span_only()
    {
        Assert.DoesNotContain("aid_al_adha_prep", Grocery.Day(D(5, 29)).Events);
        Assert.Contains("aid_al_adha_prep", Grocery.Day(D(5, 30)).Events);
        Assert.Contains("aid_al_adha_prep", Grocery.Day(D(6, 5)).Events);
        Assert.DoesNotContain("aid_al_adha_prep", Grocery.Day(D(6, 6)).Events);
    }

    [Fact]
    public void The_weekday_factor_is_the_configured_one()
    {
        Assert.Equal(1.1, Grocery.Day(D(1, 9)).WeekdayFactor);   // Thursday
        Assert.Equal(0.85, Grocery.Day(D(1, 10)).WeekdayFactor); // Friday
    }

    [Fact]
    public void Traffic_is_zero_whenever_the_store_is_closed_and_scaled_in_a_part_open_hour()
    {
        // Normal hours 07:00-13:00 and 14:00-22:00: the lunch hour is closed whatever its weight.
        var normal = Grocery.Day(D(1, 7)).HourlyTraffic;
        Assert.Equal(0, normal[13]);
        Assert.Equal(0, normal[22]);
        Assert.Equal(1.1, normal[12], 9);

        // Friday 07:00-12:15: the 12 o'clock hour is open a quarter of the time.
        var friday = Grocery.Day(D(1, 10)).HourlyTraffic;
        Assert.Equal(0.6 * 0.25, friday[12], 9);

        // Ramadan closes for iftar: 18:30-20:30.
        var ramadan = Grocery.Day(D(3, 7)).HourlyTraffic;
        Assert.Equal(1.8 * 0.5, ramadan[18], 9);
        Assert.Equal(0, ramadan[19]);
        Assert.Equal(0.6, ramadan[21], 9);
    }

    [Fact]
    public void A_day_type_with_no_opening_hours_is_a_closed_day()
    {
        var (config, catalogue, _) = TestInputs.Load(TestInputs.MiniConfig);
        var hardware = new CalendarModel(config.Calendar, catalogue.Store);

        Assert.False(hardware.Day(new DateOnly(2025, 2, 21)).IsOpen);
        Assert.True(hardware.Day(new DateOnly(2025, 2, 20)).IsOpen);
    }

    [Fact]
    public void Local_time_converts_to_utc_with_the_store_offset()
    {
        Assert.Equal(new DateTimeOffset(2025, 3, 1, 17, 30, 0, TimeSpan.Zero), Grocery.ToUtc(D(3, 1), (18 * 60) + 30));
        Assert.Equal(new DateTimeOffset(2025, 2, 28, 23, 0, 0, TimeSpan.Zero), Grocery.ToUtc(D(3, 1), 0));
    }

    [Theory]
    [InlineData("07:00-13:00", true)]
    [InlineData("20:30-24:00", true)]
    [InlineData("13:00-07:00", false)]
    [InlineData("7:00-13:00", false)]
    [InlineData("24:30-25:00", false)]
    [InlineData("07:00", false)]
    public void Opening_intervals_parse_strictly(string text, bool valid)
    {
        Assert.Equal(valid, OpeningInterval.TryParse(text, out _));
    }

    // ------------------------------------------------------------------ clock

    [Fact]
    public void The_simulated_clock_moves_forward_or_stays_but_never_goes_back()
    {
        var start = new DateTimeOffset(2025, 1, 1, 6, 0, 0, TimeSpan.Zero);
        var clock = new SimulatedClock(start);

        clock.AdvanceTo(start);
        clock.AdvanceTo(start.AddHours(2));
        Assert.Equal(start.AddHours(2), clock.GetUtcNow());

        var error = Assert.Throws<InvalidOperationException>(() => clock.AdvanceTo(start.AddHours(1)));
        Assert.Contains("time order", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ids_minted_on_the_simulated_clock_carry_simulated_time()
    {
        var at = new DateTimeOffset(2025, 3, 15, 16, 42, 0, TimeSpan.Zero);
        var clock = new SimulatedClock(at);
        var id = new Waymark.Application.IdGenerator.SeededIdGenerator(1, clock).NewId();

        Assert.Equal(at, UlidTime(id));
    }

    internal static DateTimeOffset UlidTime(string id)
    {
        const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var milliseconds = id[..10].Aggregate(0L, (value, c) => (value * 32) + Alphabet.IndexOf(c, StringComparison.Ordinal));
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }
}
