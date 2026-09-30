namespace Waymark.Domain.Customers;

/// <summary>
/// An Algerian telephone number in one form, <c>+213</c> then the nine digits after the leading 0
/// (B7, D-096), so "0550 12 34 56", "0550-123456" and "+213 550 12 34 56" are the same customer.
///
/// <para>
/// A mobile is 05, 06 or 07 then eight digits; a fixed line is 02, 03 or 04 then eight. That is
/// the national plan since 2009, and anything else is refused rather than stored in a form the next
/// search will not find. A number from abroad is kept only in the <c>+</c> form it was typed in.
/// </para>
/// </summary>
public static class PhoneNumber
{
    private const string Algeria = "+213";

    /// <param name="typed">As the cashier typed it: spaces, dots, dashes and brackets are ignored.</param>
    /// <param name="normalised">The number in its one form; null when it is not a number.</param>
    public static bool TryNormalise(string? typed, out string? normalised)
    {
        normalised = null;
        var text = new string((typed ?? string.Empty).Where(c => c is not (' ' or '.' or '-' or '(' or ')')).ToArray());
        if (text.Length == 0 || !text.Skip(text[0] == '+' ? 1 : 0).All(char.IsAsciiDigit))
        {
            return false;
        }

        // The national form, the international form with 00, and with +.
        var national = text.StartsWith("00213", StringComparison.Ordinal) ? "0" + text[5..]
            : text.StartsWith(Algeria, StringComparison.Ordinal) ? "0" + text[4..]
            : text;

        if (national.Length == 10 && national[0] == '0' && national[1] is >= '2' and <= '7')
        {
            normalised = Algeria + national[1..];
            return true;
        }

        // Abroad: kept as typed, + and 8 to 15 digits (E.164).
        if (text[0] == '+' && !text.StartsWith(Algeria, StringComparison.Ordinal) && text.Length is >= 9 and <= 16)
        {
            normalised = text;
            return true;
        }

        return false;
    }
}
