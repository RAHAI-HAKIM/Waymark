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
}
