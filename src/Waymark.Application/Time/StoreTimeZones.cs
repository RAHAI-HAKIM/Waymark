namespace Waymark.Application.Time;

/// <summary>
/// The store's time zone from <c>stores.timezone</c> (F-22, D-067).
///
/// <para>
/// The column holds an IANA name. The build runs with invariant globalisation,
/// and on Windows that leaves .NET unable to turn an IANA name into a zone, while
/// Windows zone ids still resolve. So the names Waymark serves are mapped here,
/// by hand, to their Windows ids. A store in a zone that is not on the list is
/// refused, loudly, rather than given UTC: a wrong zone moves the date the till
/// prices by, and nothing else would notice.
/// </para>
/// </summary>
public static class StoreTimeZones
{
    /// <summary>IANA name → Windows id. Add a row, and its test, for a new market.</summary>
    private static readonly Dictionary<string, string> WindowsIds = new(StringComparer.Ordinal)
    {
        // UTC+1 all year: Algeria has had no daylight saving time since 1981.
        ["Africa/Algiers"] = "W. Central Africa Standard Time",
    };

    /// <summary>The IANA names this build can resolve.</summary>
    public static IReadOnlyCollection<string> Supported => WindowsIds.Keys;

    /// <summary>The zone for an IANA name, or an exception naming what to do.</summary>
    public static TimeZoneInfo Resolve(string ianaName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ianaName);

        if (!WindowsIds.TryGetValue(ianaName, out var windowsId))
        {
            throw new InvalidOperationException(
                $"The store's time zone '{ianaName}' is not one Waymark can resolve "
                + $"(supported: {string.Join(", ", Supported)}). Add it to {nameof(StoreTimeZones)} "
                + "with its Windows id (F-22).");
        }

        return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
    }
}
