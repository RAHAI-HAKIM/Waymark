using System.Globalization;
using Waymark.Generator.Catalogues;
using Waymark.Generator.Configuration;

namespace Waymark.Generator.Calendar;

/// <summary>Where a date falls relative to a Ramadan (D-046 §15).</summary>
internal enum RamadanPhase
{
    None,

    /// <summary>The days before Ramadan when households stock up.</summary>
    StockUp,

    /// <summary>The month itself: inverted daily rhythm, pre-iftar peak.</summary>
    Ramadan,

    /// <summary>Aïd el-Fitr.</summary>
    Aid,

    /// <summary>The quiet week after Aïd.</summary>
    PostAid,
}

/// <summary>Where a date falls in the monthly pay cycle (D-046 §17).</summary>
internal enum PaydayPhase
{
    Normal,

    /// <summary>The last days of a month and the first days of the next: wages are in.</summary>
    Spike,

    /// <summary>The week before the spike: money is short, on-account buying rises.</summary>
    PrePayday,

    /// <summary>Mid-month: smaller baskets.</summary>
    Trough,
}

/// <summary>Everything the calendar says about one store-local date.</summary>
/// <param name="Date">The store-local date.</param>
/// <param name="DayType">normal, friday, ramadan or aid — selects opening hours and traffic.</param>
/// <param name="WeekdayFactor">Footfall multiplier for the day of the week.</param>
/// <param name="RamadanPhase">The Ramadan phase.</param>
/// <param name="RamadanDay">1-based day of Ramadan, or null outside it.</param>
/// <param name="PaydayPhase">The pay-cycle phase.</param>
/// <param name="Payday">The multipliers for that phase.</param>
/// <param name="Events">Names of the configured events active on this date.</param>
/// <param name="HourlyTraffic">24 arrival weights, already zeroed where the store is closed.</param>
internal sealed record DayContext(
    DateOnly Date,
    string DayType,
    double WeekdayFactor,
    RamadanPhase RamadanPhase,
    int? RamadanDay,
    PaydayPhase PaydayPhase,
    PaydayEffect Payday,
    IReadOnlyList<string> Events,
    IReadOnlyList<double> HourlyTraffic)
{
    /// <summary>Whether any customer can arrive at all.</summary>
    public bool IsOpen => HourlyTraffic.Any(weight => weight > 0);
}

/// <summary>
/// The store's calendar: Ramadan and Aïd by their actual dates, the pay cycle, events,
/// weekdays, and the opening hours and traffic shape of each day type.
///
/// <para>
/// It says what kind of day a date is and nothing about demand. How much a Ramadan day
/// lifts halwa chamia is a seasonality profile's business; the calendar only reports that
/// it is day 12 of Ramadan. Keeping the two apart is what lets a second catalogue reuse the
/// calendar with different profiles.
/// </para>
/// </summary>
internal sealed class CalendarModel
{
    public const string Normal = "normal";
    public const string Friday = "friday";
    public const string RamadanDayType = "ramadan";
    public const string AidDayType = "aid";

    /// <summary>The day types every store profile and traffic table must define.</summary>
    public static readonly IReadOnlyList<string> RequiredDayTypes = [Normal, Friday, RamadanDayType, AidDayType];

    private readonly CalendarSettings _settings;
    private readonly TimeSpan _utcOffset;
    private readonly Dictionary<string, double[]> _traffic = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<OpeningInterval>> _opening = new(StringComparer.Ordinal);

    public CalendarModel(CalendarSettings settings, StoreProfile store)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);

        _settings = settings;
        _utcOffset = TimeSpan.FromHours(store.UtcOffsetHours);

        foreach (var (dayType, weights) in settings.HourlyTraffic)
        {
            var intervals = store.OpeningHours.TryGetValue(dayType, out var text)
                ? OpeningInterval.ParseAll(text)
                : [];

            _opening[dayType] = intervals;
            _traffic[dayType] = [.. Enumerable.Range(0, 24).Select(hour => weights.Value[hour] * OpeningInterval.OpenFraction(intervals, hour))];
        }
    }

    /// <summary>The calendar's view of one store-local date.</summary>
    public DayContext Day(DateOnly date)
    {
        var (phase, ramadanDay) = RamadanPhaseOf(date);
        var events = _settings.Events.Value
            .Where(e => date >= e.Start && date < e.Start.AddDays(e.Days))
            .ToList();

        var dayType = events.FirstOrDefault(e => e.DayType is not null)?.DayType
            ?? phase switch
            {
                RamadanPhase.Aid => AidDayType,
                RamadanPhase.Ramadan => RamadanDayType,
                _ => date.DayOfWeek == DayOfWeek.Friday ? Friday : Normal,
            };

        var payday = PaydayPhaseOf(date);

        return new DayContext(
            date,
            dayType,
            _settings.WeekdayFactors.Value[WeekdayKey(date.DayOfWeek)],
            phase,
            ramadanDay,
            payday,
            _settings.Payday.Effects.Value[PaydayKey(payday)],
            [.. events.Select(e => e.Name).Distinct(StringComparer.Ordinal)],
            _traffic[dayType]);
    }

    /// <summary>The opening intervals of a day type, in store-local minutes.</summary>
    public IReadOnlyList<OpeningInterval> OpeningIntervals(string dayType) => _opening[dayType];

    /// <summary>A store-local date and minute of the day as a UTC instant.</summary>
    public DateTimeOffset ToUtc(DateOnly date, int minuteOfDay) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue).AddMinutes(minuteOfDay), _utcOffset).ToUniversalTime();

    /// <summary>The configuration key for a weekday: sunday … saturday.</summary>
    public static string WeekdayKey(DayOfWeek day) => day.ToString().ToLowerInvariant();

    /// <summary>The configuration key for a pay-cycle phase.</summary>
    public static string PaydayKey(PaydayPhase phase) => phase switch
    {
        PaydayPhase.Spike => "spike",
        PaydayPhase.PrePayday => "pre_payday",
        PaydayPhase.Trough => "trough",
        _ => "normal",
    };

    /// <summary>The configuration key for a Ramadan phase, or null for none.</summary>
    public static string? RamadanKey(RamadanPhase phase) => phase switch
    {
        RamadanPhase.StockUp => "stock_up",
        RamadanPhase.Ramadan => "ramadan",
        RamadanPhase.Aid => "aid",
        RamadanPhase.PostAid => "post_aid",
        _ => null,
    };

    private (RamadanPhase Phase, int? Day) RamadanPhaseOf(DateOnly date)
    {
        var found = (Phase: RamadanPhase.None, Day: (int?)null);

        foreach (var period in _settings.Ramadan.Value)
        {
            var aidEnd = period.AidStart.AddDays(period.AidDays - 1);

            // Ramadan and Aïd outrank the stock-up and quiet weeks around a neighbouring year.
            if (date >= period.Start && date <= period.End)
            {
                return (RamadanPhase.Ramadan, date.DayNumber - period.Start.DayNumber + 1);
            }

            if (date >= period.AidStart && date <= aidEnd)
            {
                return (RamadanPhase.Aid, null);
            }

            if (date >= period.Start.AddDays(-_settings.StockUpDays.Value) && date < period.Start)
            {
                found = (RamadanPhase.StockUp, null);
            }
            else if (found.Phase == RamadanPhase.None && date > aidEnd && date <= aidEnd.AddDays(_settings.PostAidQuietDays.Value))
            {
                found = (RamadanPhase.PostAid, null);
            }
        }

        return found;
    }

    private PaydayPhase PaydayPhaseOf(DateOnly date)
    {
        var payday = _settings.Payday;
        var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
        var spikeStart = daysInMonth - payday.SpikeLastDaysOfMonth.Value + 1;

        if (date.Day >= spikeStart || date.Day <= payday.SpikeFirstDaysOfMonth.Value)
        {
            return PaydayPhase.Spike;
        }

        if (date.Day >= spikeStart - payday.PrePaydayDays.Value)
        {
            return PaydayPhase.PrePayday;
        }

        return date.Day >= payday.TroughFirstDay.Value && date.Day <= payday.TroughLastDay.Value
            ? PaydayPhase.Trough
            : PaydayPhase.Normal;
    }
}

/// <summary>A store-local opening interval in minutes of the day, end exclusive.</summary>
internal readonly record struct OpeningInterval(int StartMinute, int EndMinute)
{
    /// <summary>Parses "HH:mm-HH:mm"; the end may be "24:00".</summary>
    public static bool TryParse(string text, out OpeningInterval interval)
    {
        interval = default;
        var parts = text.Split('-');
        if (parts.Length != 2 || !TryMinute(parts[0], out var start) || !TryMinute(parts[1], out var end) || end <= start)
        {
            return false;
        }

        interval = new OpeningInterval(start, end);
        return true;
    }

    /// <summary>Parses a list, or throws naming the bad entry. Validation reports these first.</summary>
    public static IReadOnlyList<OpeningInterval> ParseAll(IReadOnlyList<string> texts) =>
        [.. texts.Select(text => TryParse(text, out var interval)
            ? interval
            : throw new GeneratorInputException($"Opening hours '{text}' are not HH:mm-HH:mm with the end after the start."))
            .OrderBy(interval => interval.StartMinute)];

    /// <summary>The fraction of <paramref name="hour"/> covered by the intervals, 0 to 1.</summary>
    public static double OpenFraction(IReadOnlyList<OpeningInterval> intervals, int hour)
    {
        var from = hour * 60;
        var to = from + 60;
        var open = intervals.Sum(i => Math.Max(0, Math.Min(to, i.EndMinute) - Math.Max(from, i.StartMinute)));
        return Math.Min(60, open) / 60.0;
    }

    private static bool TryMinute(string text, out int minute)
    {
        minute = 0;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2
            || parts[0].Length != 2 || parts[1].Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || minutes > 59 || hours > 24 || (hours == 24 && minutes != 0))
        {
            return false;
        }

        minute = (hours * 60) + minutes;
        return true;
    }
}
