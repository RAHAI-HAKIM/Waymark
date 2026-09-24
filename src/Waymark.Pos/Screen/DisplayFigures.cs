using System.Globalization;
using Waymark.Domain.Values;

namespace Waymark.Pos.Screen;

/// <summary>
/// Figures as the cashier reads them (G1 kit §2): <c>3 320,80</c>, a comma for the decimal
/// point and a narrow no-break space between thousands.
///
/// <para>
/// <b>Integers all the way.</b> A <see cref="Money"/> is a count of minor units; it is split
/// into units and hundredths with integer division, never through a <see cref="double"/> or the
/// machine's culture. A till on a Windows set to English must print exactly what one set to
/// French prints.
/// </para>
/// <para>
/// <b>The thousands separator is U+202F, never a space.</b> An ordinary space is a word break:
/// inside an Arabic sentence the bidirectional algorithm treats "3 320,80" as two numbers and
/// may print them in the other order, so the cashier reads 320,80 3. The same figure is also
/// wrapped at a line break by a space and never by U+202F.
/// </para>
/// </summary>
public static class DisplayFigures
{
    /// <summary>Between groups of thousands: narrow, and never a break.</summary>
    public const char ThousandsSeparator = '\u202F';

    /// <summary>Between a figure and its unit or currency: a no-break space.</summary>
    public const char UnitSeparator = '\u00A0';

    public const char DecimalSeparator = ',';

    /// <summary>A true minus sign, the width of a digit, so negative figures stay aligned.</summary>
    public const char Minus = '\u2212';

    /// <summary>"3 320,80", "-42,00" with a true minus, "0,00".</summary>
    public static string Amount(Money amount)
    {
        var scale = Pow10(amount.Currency.MinorUnitExponent);
        var magnitude = amount.MinorUnits == long.MinValue
            ? throw new OverflowException("An amount this large is not a till figure.")
            : Math.Abs(amount.MinorUnits);

        var units = Grouped(magnitude / scale);
        var sign = amount.IsNegative ? Minus.ToString() : string.Empty;

        return scale == 1
            ? sign + units
            : sign + units + DecimalSeparator + (magnitude % scale).ToString(
                new string('0', amount.Currency.MinorUnitExponent), CultureInfo.InvariantCulture);
    }

    /// <summary><c>3 320,80 DA</c> in French, <c>3 320,80 د.ج</c> in Arabic.</summary>
    public static string AmountWithCurrency(Money amount, TillText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Amount(amount) + UnitSeparator + text.CurrencySymbol(amount.Currency);
    }

    /// <summary>A whole count of units, grouped like every other figure.</summary>
    public static string Count(long count) =>
        count < 0 ? Minus + Grouped(Math.Abs(count)) : Grouped(count);

    /// <summary>The till's local time, <c>14:32</c>.</summary>
    public static string Clock(DateTimeOffset moment) =>
        moment.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A day in the month, <c>25/09</c>.</summary>
    public static string DayAndMonth(DateOnly day) =>
        day.ToString("dd/MM", CultureInfo.InvariantCulture);

    private static string Grouped(long value)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        if (digits.Length <= 3)
        {
            return digits;
        }

        var groups = new List<string>();
        for (var end = digits.Length; end > 0; end -= 3)
        {
            groups.Insert(0, digits[Math.Max(0, end - 3)..end]);
        }

        return string.Join(ThousandsSeparator, groups);
    }

    private static long Pow10(int exponent)
    {
        var result = 1L;
        for (var i = 0; i < exponent; i++)
        {
            result *= 10;
        }

        return result;
    }
}
