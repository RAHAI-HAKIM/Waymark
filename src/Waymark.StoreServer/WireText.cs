using System.Globalization;
using Waymark.Domain.Values;

namespace Waymark.StoreServer;

/// <summary>
/// Figures as exact decimal text for the wire (D-044), never through a double. Division of a
/// decimal by a power of ten is exact, and the invariant culture keeps '.' on every machine.
/// </summary>
public static class WireText
{
    public static string Figure(Money money) =>
        (money.MinorUnits / (decimal)Currency.StorageScale).ToString("0.00", CultureInfo.InvariantCulture);

    public static string Figure(Quantity quantity) =>
        (quantity.Thousandths / (decimal)Quantity.Scale).ToString("0.###", CultureInfo.InvariantCulture);
}
