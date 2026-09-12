using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Waymark.Domain.Values;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Conversions shared by every configuration, so the storage conventions in the
/// schema header are written down once rather than retyped 58 times.
/// </summary>
internal static class WaymarkConverters
{
    /// <summary>
    /// The model default for a money column the schema declares <c>DEFAULT 0</c>.
    ///
    /// <para>
    /// <see cref="Money"/> cannot be built without a currency, and a
    /// configuration cannot know the ledger's — it is instantiated by
    /// <c>ApplyConfigurationsFromAssembly</c> with nothing passed in. The
    /// currency named here never surfaces: the value exists only so migrations
    /// emit <c>DEFAULT 0</c>, and the converter turns it into the integer 0
    /// whatever currency it carries.
    /// </para>
    /// <para>
    /// No matching <c>HasSentinel</c>, and that is an improvement rather than
    /// an omission. D-027's problem was that <c>0L</c> means both "no value" and
    /// "zero", so an explicit zero was silently replaced by the column default.
    /// <c>default(Money)</c> is the only Money with no currency at all, so it
    /// cannot collide with a real amount — and EF already uses it as the
    /// sentinel for a struct without being told.
    /// </para>
    /// </summary>
    public static readonly Money ZeroMoney = Money.Zero(Currency.Dzd);

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

    /// <summary>
    /// Dates are TEXT, <c>YYYY-MM-DD</c> — an expiry or a joining date, where
    /// the time of day would be invented rather than recorded.
    /// </summary>
    public const string DateFormat = "yyyy-MM-dd";

    public static readonly ValueConverter<DateOnly, string> Date = new(
        value => value.ToString(DateFormat, CultureInfo.InvariantCulture),
        text => DateOnly.ParseExact(text, DateFormat, CultureInfo.InvariantCulture));
}
