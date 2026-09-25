using Waymark.Pos.Checkout;

namespace Waymark.Pos.Screen;

/// <summary>
/// A count typed into the field between − and + (B2, Hakim 25/09): a big number without pressing +
/// a hundred times. Read here, not in the window, so the rule is tested without one.
/// </summary>
public static class QuantityEntry
{
    /// <summary>
    /// A whole count from 1 to <see cref="Cart.MaxCount"/>, in the digits '0' to '9'. The grouping
    /// space the field shows ("1 200") is read through; anything else is refused, and the line keeps
    /// its count. <b>ASCII digits only</b>, as for a PIN (D-083): <c>char.IsDigit</c> would take
    /// Arabic-Indic digits from a keyboard switched to Arabic, and <c>int.Parse</c> a sign.
    /// </summary>
    public static bool TryParse(string? text, out int count)
    {
        count = 0;
        var digits = (text ?? string.Empty).Replace(DisplayFigures.ThousandsSeparator.ToString(), string.Empty, StringComparison.Ordinal).Trim();
        if (digits.Length is 0 or > 4 || !digits.All(c => c is >= '0' and <= '9'))
        {
            return false;
        }

        var value = 0;
        foreach (var c in digits)
        {
            value = (value * 10) + (c - '0');
        }

        if (value is < 1 or > Cart.MaxCount)
        {
            return false;
        }

        count = value;
        return true;
    }
}
