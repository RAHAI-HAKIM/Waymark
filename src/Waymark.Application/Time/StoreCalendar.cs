using Waymark.Domain;

namespace Waymark.Application.Time;

/// <summary>
/// <see cref="IStoreCalendar"/> from the injected clock and the store's zone.
///
/// <para>
/// Asked each time, never cached: a till left running over midnight must start
/// the new day's prices at midnight, not at its next restart.
/// </para>
/// </summary>
public sealed class StoreCalendar(TimeProvider clock, TimeZoneInfo zone) : IStoreCalendar
{
    /// <inheritdoc />
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
}
