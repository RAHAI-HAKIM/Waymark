using System.Globalization;
using Waymark.Domain.Values;

namespace Waymark.Pos.Checkout;

/// <summary>
/// The wire's exact decimal text (D-044) turned back into value objects.
///
/// <para>
/// <b>Refuses rather than rounds.</b> A price with a third decimal, or a stock
/// level finer than a thousandth, is not something the server sends; if one
/// arrives, the till says it cannot read the answer instead of quietly creating a
/// centime. Parsed as <see cref="decimal"/> in the invariant culture, never as a
/// double and never with the machine's decimal separator.
/// </para>
/// </summary>
public static class WireFigures
{
    private const NumberStyles Style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public static Money Money(string text, string currencyCode)
    {
        var currency = Currency.FromCode(currencyCode);
        var minorUnits = Exact(text, Currency.StorageScale, "a price");
        return Domain.Values.Money.FromMinorUnits(minorUnits, currency);
    }

    public static Quantity Quantity(string text, string unitCode) =>
        Domain.Values.Quantity.FromThousandths(Exact(text, Domain.Values.Quantity.Scale, "a quantity"), unitCode);

    private static long Exact(string text, int scale, string what)
    {
        if (!decimal.TryParse(text, Style, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"'{text}' is not {what}.");
        }

        var scaled = value * scale;
        if (scaled != decimal.Truncate(scaled))
        {
            throw new FormatException($"'{text}' is finer than {what} can be: it would have to be rounded.");
        }

        return checked((long)scaled);
    }
}
