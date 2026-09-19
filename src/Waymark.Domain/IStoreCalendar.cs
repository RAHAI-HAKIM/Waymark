namespace Waymark.Domain;

/// <summary>
/// The store's own date. Prices change at the store's midnight, not UTC's: between
/// 00:00 and 01:00 in Algiers the UTC date is still yesterday, and a lookup keyed on
/// it would sell at yesterday's price.
///
/// <para>
/// A port, so the one question of how the store's date is known has one answer.
/// <c>stores.timezone</c> holds an IANA name, which the build's invariant
/// globalisation cannot resolve on Windows (F-22); until that is decided, no
/// implementation reads it.
/// </para>
/// </summary>
public interface IStoreCalendar
{
    /// <summary>Today, in the store.</summary>
    DateOnly Today { get; }
}
