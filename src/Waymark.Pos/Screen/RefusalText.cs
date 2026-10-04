using Waymark.Contracts.Pos;
using Waymark.Pos.Checkout;

namespace Waymark.Pos.Screen;

/// <summary>
/// A server's refusal as the till says it (D-107). The server answers a code and what it names;
/// the sentence is the till's, in its own language, and an amount is written the till's way. A
/// refusal with no code, or a code this till does not know, is said plainly with the server's own
/// words behind it: never the English sentence alone, as it was (block B review).
/// </summary>
public static class RefusalText
{
    /// <summary>Which of a code's names are amounts, by position: written "1 714,00 DA", not "1714.00".</summary>
    private static readonly Dictionary<string, int[]> Amounts = new(StringComparer.Ordinal)
    {
        [RefusalCodes.PriceOutOfBand] = [1, 2],
        [RefusalCodes.PartsAboveTotal] = [0],
        [RefusalCodes.TabAboveLimit] = [1],
        [RefusalCodes.CreditInsufficient] = [1],
        [RefusalCodes.LimitAboveCeiling] = [0],
        [RefusalCodes.RepayAboveBalance] = [0],
        [RefusalCodes.RepayNotOnStep] = [0],
    };

    /// <param name="refusal">The code and what it names; null when the server gave none.</param>
    /// <param name="reason">The server's own words, shown only when there is no sentence for the code.</param>
    /// <param name="currency">The currency of the amounts named.</param>
    public static string Say(TillText text, Refusal? refusal, string? reason, string currency = "DZD")
    {
        ArgumentNullException.ThrowIfNull(text);
        if (refusal is not null)
        {
            Amounts.TryGetValue(refusal.Code, out var money);
            List<string> args = [.. (refusal.Args ?? []).Select((arg, index) =>
                money is not null && money.Contains(index) ? Amount(arg, currency, text) : arg)];
            if (text.Refusal(refusal.Code, args) is { } said)
            {
                return said;
            }
        }

        return string.IsNullOrWhiteSpace(reason) ? text.RefusedByServer : $"{text.RefusedByServer} · {reason}";
    }

    /// <summary>An amount off the wire as the till shows every other; the text as it came when it cannot be read.</summary>
    private static string Amount(string figure, string currency, TillText text)
    {
        try
        {
            return DisplayFigures.AmountWithCurrency(WireFigures.Money(figure, currency), text);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return figure;
        }
    }
}
