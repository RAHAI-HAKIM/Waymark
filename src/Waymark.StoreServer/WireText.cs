using System.Globalization;
using Waymark.Contracts;
using Waymark.Domain.Values;

namespace Waymark.StoreServer;

/// <summary>
/// The domain's values as exact wire text (D-044). The formatting itself is
/// <see cref="Figures"/>, in Contracts, so the outbox payloads and the till's answers
/// cannot drift apart; this only unwraps the value objects.
/// </summary>
public static class WireText
{
    public static string Figure(Money money) => Figures.Amount(money.MinorUnits);

    public static string Figure(Quantity quantity) => Figures.Quantity(quantity.Thousandths);

    /// <summary>
    /// A quantity sent by the till, "0.556", as thousandths: digits, then at most three after a
    /// point. False for anything else, a sign or a comma included: the till sends invariant text.
    /// </summary>
    public static bool TryThousandths(string? text, out long thousandths)
    {
        thousandths = 0;
        var parts = (text ?? string.Empty).Split('.');
        if (parts.Length > 2 || parts[0].Length is 0 or > 9 || !parts[0].All(char.IsAsciiDigit)
            || (parts.Length == 2 && (parts[1].Length is 0 or > 3 || !parts[1].All(char.IsAsciiDigit))))
        {
            return false;
        }

        var fraction = parts.Length == 2 ? parts[1].PadRight(3, '0') : "000";
        thousandths = (long.Parse(parts[0], CultureInfo.InvariantCulture) * Quantity.Scale)
            + long.Parse(fraction, CultureInfo.InvariantCulture);
        return true;
    }
}
