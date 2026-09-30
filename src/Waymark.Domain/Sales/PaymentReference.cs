namespace Waymark.Domain.Sales;

/// <summary>What <see cref="PaymentReference.Read"/> says of a reference typed beside a card or BaridiMob part.</summary>
public enum ReferenceVerdict
{
    /// <summary>Kept, trimmed; or nothing typed, which is fine: a reference is optional (D-095).</summary>
    Accepted,

    /// <summary>It reads as a card number: never kept, never shown again.</summary>
    LooksLikeACardNumber,

    /// <summary>Longer than <see cref="PaymentReference.MaximumLength"/>, or a character a terminal never prints.</summary>
    Invalid,
}

/// <summary>
/// The optional reference of a card or BaridiMob part (B6, D-095): the terminal's authorisation
/// number, the last four digits, a transfer's id. <b>Never a card number</b>: the till has no business
/// holding one, and the row would carry it into every backup. A number of 13 to 19 digits that passes
/// the Luhn check is refused, whatever spaces or dashes it is typed with.
/// </summary>
public static class PaymentReference
{
    /// <summary>Longer than any authorisation number or transfer id seen.</summary>
    public const int MaximumLength = 32;

    /// <param name="typed">What the cashier typed; null or blank is no reference.</param>
    /// <param name="reference">The reference to keep, trimmed; null when none is kept.</param>
    public static ReferenceVerdict Read(string? typed, out string? reference)
    {
        reference = null;
        var text = typed?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return ReferenceVerdict.Accepted;
        }

        if (text.Length > MaximumLength || !text.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '/'))
        {
            return ReferenceVerdict.Invalid;
        }

        if (LooksLikeACardNumber(text))
        {
            return ReferenceVerdict.LooksLikeACardNumber;
        }

        reference = text;
        return ReferenceVerdict.Accepted;
    }

    /// <summary>Its digits alone are 13 to 19 of them and pass the Luhn check, as every card number does.</summary>
    private static bool LooksLikeACardNumber(string text)
    {
        if (text.Any(char.IsAsciiLetter))
        {
            return false;
        }

        var digits = text.Where(char.IsAsciiDigit).Select(c => c - '0').ToArray();
        if (digits.Length is < 13 or > 19)
        {
            return false;
        }

        // Luhn: from the right, every second digit doubled, and 9 taken off a double above 9.
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i];
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return sum % 10 == 0;
    }
}
