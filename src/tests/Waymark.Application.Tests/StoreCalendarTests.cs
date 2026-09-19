using Waymark.Application.Time;

namespace Waymark.Application.Tests;

/// <summary>
/// The store's date, which decides the price the till charges (D-066, D-067).
/// The silent failure is an hour: between 00:00 and 01:00 in Algiers the UTC date
/// is still yesterday, and a till reading UTC would charge yesterday's price.
/// </summary>
public sealed class StoreCalendarTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static DateOnly TodayAt(string utc) =>
        new StoreCalendar(
            new FixedClock(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture)),
            StoreTimeZones.Resolve("Africa/Algiers")).Today;

    [Fact]
    public void Algiers_starts_its_day_an_hour_before_utc()
    {
        Assert.Equal(new DateOnly(2026, 9, 17), TodayAt("2026-09-17T22:59:59Z"));
        Assert.Equal(new DateOnly(2026, 9, 18), TodayAt("2026-09-17T23:00:00Z"));
    }

    [Fact]
    public void Algiers_has_no_summer_time()
    {
        // Mid-January and mid-July both sit at UTC+1: the day turns at 23:00 UTC.
        Assert.Equal(new DateOnly(2026, 1, 16), TodayAt("2026-01-15T23:00:00Z"));
        Assert.Equal(new DateOnly(2026, 7, 16), TodayAt("2026-07-15T23:00:00Z"));
        Assert.Equal(new DateOnly(2026, 7, 15), TodayAt("2026-07-15T22:59:59Z"));
    }

    [Fact]
    public void The_calendar_follows_the_clock_rather_than_caching_the_day()
    {
        var clock = new MovingClock(new DateTimeOffset(2026, 9, 17, 22, 59, 0, TimeSpan.Zero));
        var calendar = new StoreCalendar(clock, StoreTimeZones.Resolve("Africa/Algiers"));

        Assert.Equal(new DateOnly(2026, 9, 17), calendar.Today);
        clock.Now = clock.Now.AddMinutes(1);
        Assert.Equal(new DateOnly(2026, 9, 18), calendar.Today);
    }

    private sealed class MovingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Every_supported_zone_resolves_on_this_machine()
    {
        // The map is hand-written: a mistyped Windows id would pass review and
        // fail on the till at start.
        foreach (var name in StoreTimeZones.Supported)
        {
            Assert.NotNull(StoreTimeZones.Resolve(name));
        }
    }

    [Fact]
    public void An_unmapped_zone_is_refused_rather_than_read_as_utc()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => StoreTimeZones.Resolve("Europe/Paris"));

        Assert.Contains("F-22", refusal.Message, StringComparison.Ordinal);
    }
}
