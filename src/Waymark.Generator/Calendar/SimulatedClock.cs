namespace Waymark.Generator.Calendar;

/// <summary>
/// The simulated "now". Every <c>created_at</c>, every <c>occurred_at</c> and every ULID
/// timestamp in a generated store comes from here (D-046 §12, F-3).
///
/// <para>
/// <b>It only moves forward.</b> The seeded id generator stamps ids with this clock, so
/// minting out of order would give a row an id that sorts before rows that happened
/// earlier — a quiet lie about chronology in exactly the data meant to teach the engine
/// what a year looks like. The simulation therefore processes events in time order, and
/// this refuses a step backwards rather than letting one slip through.
/// </para>
/// </summary>
internal sealed class SimulatedClock : TimeProvider
{
    private DateTimeOffset _now;

    public SimulatedClock(DateTimeOffset start) => _now = start.ToUniversalTime();

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>
    /// UTC, so nothing reads the machine running the generator. Store local time is the
    /// calendar's business (<see cref="CalendarModel.ToUtc"/>).
    /// </summary>
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <summary>Moves to <paramref name="instant"/>, which may equal the current time but not precede it.</summary>
    /// <exception cref="InvalidOperationException">The instant is earlier than now.</exception>
    public void AdvanceTo(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        if (utc < _now)
        {
            throw new InvalidOperationException(
                $"The simulated clock cannot go back from {_now:O} to {utc:O}. Events must be "
                + "simulated in time order, or the ids minted for them sort out of chronology.");
        }

        _now = utc;
    }
}
