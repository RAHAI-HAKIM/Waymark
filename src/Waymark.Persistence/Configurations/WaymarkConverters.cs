using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Conversions shared by every configuration, so the storage conventions in the
/// schema header are written down once rather than retyped 58 times.
/// </summary>
internal static class WaymarkConverters
{
    /// <summary>
    /// Timestamps are TEXT, ISO-8601 UTC, <c>YYYY-MM-DD HH:MM:SS</c> — the
    /// schema's own convention. Note the space rather than a <c>T</c>, and no
    /// zone suffix: the format sorts lexicographically, which is why timestamp
    /// range queries work as plain string comparisons.
    /// </summary>
    public const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>
    /// Everything is stored as UTC. Reading returns a
    /// <see cref="DateTimeOffset"/> with a zero offset — the store's local time
    /// is a presentation concern and never reaches the database.
    /// </summary>
    public static readonly ValueConverter<DateTimeOffset, string> Timestamp = new(
        value => value.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture),
        text => new DateTimeOffset(
            DateTime.SpecifyKind(
                DateTime.ParseExact(text, TimestampFormat, CultureInfo.InvariantCulture),
                DateTimeKind.Utc)));
}
