namespace Waymark.Domain;

/// <summary>
/// The store's own clock. Prices change at the store's midnight, not UTC's: between 00:00
/// and 01:00 in Algiers the UTC date is still yesterday, and a lookup keyed on it would sell
/// at yesterday's price. The hour matters too, because a basket crosses at its hour (D-043).
///
/// <para>
/// A port, so the one question of what time it is in the store has one answer.
/// <c>stores.timezone</c> holds an IANA name, which the build's invariant globalisation
/// cannot resolve on Windows, so <c>StoreTimeZones</c> maps it (F-22, D-067).
/// </para>
/// </summary>
public interface IStoreCalendar
{
    /// <summary>Now, in the store.</summary>
    DateTimeOffset Now { get; }

    /// <summary>Today, in the store.</summary>
    DateOnly Today { get; }

    /// <summary>The hour of the day in the store, 0 to 23: a basket's bucket (D-043).</summary>
    int HourOfDay { get; }
}
