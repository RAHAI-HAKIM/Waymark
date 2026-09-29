using Waymark.Contracts.Pos;

namespace Waymark.Pos.Screen;

/// <summary>
/// A discount typed in the field (B4, D-091): "10" or "12,5" for a percent, "50" or "50,00" for an
/// amount. Read here, not in the window, so the rule is tested without one.
/// </summary>
public static class DiscountEntry
{
    /// <summary>The most an amount typed at the counter may be: 9 999 999,99, a digit too many beyond it.</summary>
    public const long MaxAmountHundredths = 999_999_999;

    /// <summary>
    /// The value as hundredths: a percent as basis points, above 0 and at most 100 %; an amount as
    /// centimes, above zero. At most two decimals, after a comma or a point. <b>ASCII digits only</b>,
    /// as for a count and a weight. Anything else is refused and nothing is given.
    /// </summary>
    public static bool TryParse(string? text, string form, out long hundredths)
    {
        hundredths = 0;
        var trimmed = (text ?? string.Empty).Trim();
        var separator = trimmed.IndexOfAny([',', '.']);
        var whole = separator < 0 ? trimmed : trimmed[..separator];
        var fraction = separator < 0 ? string.Empty : trimmed[(separator + 1)..];

        if ((separator >= 0 && fraction.Length == 0) || whole.Length is 0 or > 7 || fraction.Length > 2
            || !whole.All(IsDigit) || !fraction.All(IsDigit))
        {
            return false;
        }

        var value = (Number(whole) * 100) + Number(fraction.PadRight(2, '0'));
        var most = form == DiscountForms.Percent ? 10_000 : MaxAmountHundredths;
        if (value < 1 || value > most)
        {
            return false;
        }

        hundredths = value;
        return true;
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static long Number(string digits) => digits.Aggregate(0L, (value, c) => (value * 10) + (c - '0'));
}
