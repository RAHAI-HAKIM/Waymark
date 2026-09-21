using Waymark.Domain;

namespace Waymark.Application.Time;

/// <summary>
/// <see cref="IStoreCalendar"/> from the injected clock and the store's zone.
///
/// <para>
/// Asked each time, never cached: a till left running over midnight must start the new day's
/// prices at midnight, not at its next restart.
/// </para>
/// </summary>
public sealed class StoreCalendar(TimeProvider clock, TimeZoneInfo zone) : IStoreCalendar
{
    /// <inheritdoc />
    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);

    /// <inheritdoc />
    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    /// <inheritdoc />
    public int HourOfDay => Now.Hour;
}
