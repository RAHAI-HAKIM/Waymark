namespace Waymark.Pos.Screen;

/// <summary>
/// A weight typed in the field for a product sold by weight (B3, D-090): "0,556" or "0.556" kg.
/// Read here, not in the window, so the rule is tested without one.
/// </summary>
public static class WeightEntry
{
    /// <summary>
    /// The most one weighing may be, in thousandths of the unit: 99,999 kg, what five digits of a
    /// scale label can carry. A weight past it was typed with a digit too many.
    /// </summary>
    public const long MaxThousandths = 99_999;

    /// <summary>
    /// A weight above zero and at most <see cref="MaxThousandths"/>, with no more decimals than the
    /// unit is sold to (<paramref name="decimals"/>), as thousandths. A comma or a point separates the
    /// decimals: the till is French and Arabic, the numeric pad has a point. <b>ASCII digits only</b>,
    /// as for a count (<see cref="QuantityEntry"/>). Anything else is refused and nothing is weighed.
    /// </summary>
    public static bool TryParse(string? text, int decimals, out long thousandths)
    {
        thousandths = 0;
        var trimmed = (text ?? string.Empty).Trim();
        var separator = trimmed.IndexOfAny([',', '.']);
        var whole = separator < 0 ? trimmed : trimmed[..separator];
        var fraction = separator < 0 ? string.Empty : trimmed[(separator + 1)..];

        if (separator >= 0 && fraction.Length == 0)
        {
            return false; // "1," is unfinished, not one kilo
        }

        if (whole.Length is 0 or > 2 || fraction.Length > Math.Clamp(decimals, 0, 3)
            || !whole.All(IsDigit) || !fraction.All(IsDigit))
        {
            return false;
        }

        var value = (Number(whole) * 1_000) + Number(fraction.PadRight(3, '0'));
        if (value is < 1 or > MaxThousandths)
        {
            return false;
        }

        thousandths = value;
        return true;
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static long Number(string digits) => digits.Aggregate(0L, (value, c) => (value * 10) + (c - '0'));
}
