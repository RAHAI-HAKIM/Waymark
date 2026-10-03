using Waymark.Domain.Catalogue;

namespace Waymark.Domain.Customers;

/// <summary>
/// A customer found by name at the till (B7, D-100): quicker than asking for a number nobody
/// remembers, and kept narrow, since every customer listed is a consultation of that person (D-061).
///
/// <para><b>The rules:</b></para>
/// <list type="number">
///   <item><description><b>A full name only</b> (<see cref="IsFullName"/>): at least two words, each of
///   at least <see cref="MinimumWord"/> letters, so a first name alone, or "Sam B", searches nothing.</description></item>
///   <item><description><b>Whole words</b> (<see cref="Matches"/>): every word typed is a whole word of
///   the customer's name, case and accents ignored as the product search ignores them (D-088): "samira
///   benali" finds "Samira Benali", never "Samira Benaliche"; the order typed does not matter.</description></item>
///   <item><description><b>At most <see cref="MaximumShown"/></b>: more matches than that list nobody,
///   and the cashier adds the number or the name in full; a list of strangers is never shown.</description></item>
///   <item><description><b>The number masked</b> (<see cref="Masked"/>): a name search shows the last
///   four digits, enough to ask "06.. 34 56 ?", never the whole number.</description></item>
/// </list>
/// </summary>
public static class CustomerNameSearch
{
    public const int MinimumWord = 2;

    public const int MaximumShown = 3;

    /// <summary>Whether what was typed is a full name: two words or more, each of two letters or more.</summary>
    public static bool IsFullName(string? typed)
    {
        var words = NameSearch.Words(typed);
        return words.Count >= 2 && words.All(word => word.Length >= MinimumWord && !word.All(char.IsAsciiDigit));
    }

    /// <summary>Whether every word typed is a whole word of <paramref name="name"/>; false unless <paramref name="typed"/> is a full name.</summary>
    public static bool Matches(string? typed, string? name)
    {
        if (!IsFullName(typed))
        {
            return false;
        }

        var words = NameSearch.Words(name);
        return NameSearch.Words(typed).All(word => words.Contains(word, StringComparer.Ordinal));
    }

    /// <summary>"+213550123456" as "•••• •• 34 56": the last four digits only; null stays null.</summary>
    public static string? Masked(string? phone)
    {
        if (phone is null)
        {
            return null;
        }

        var digits = new string([.. phone.Where(char.IsAsciiDigit)]);
        return digits.Length < 4 ? "••••" : $"•••• •• {digits[^4..^2]} {digits[^2..]}";
    }
}
