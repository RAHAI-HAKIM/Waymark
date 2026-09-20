using System.Globalization;

namespace Waymark.Contracts;

/// <summary>
/// Figures as exact decimal text (D-044), written once for everything that puts one on a
/// wire: the outbox payloads, the till's answers, the generator's records.
///
/// <para>
/// Never through a double. Dividing a <see cref="decimal"/> by a power of ten is exact, and
/// the invariant culture keeps '.' as the point on every machine. Contracts references
/// nothing, so these take the stored integers rather than <c>Money</c> or <c>Quantity</c>;
/// the callers that have those pass their minor units and thousandths.
/// </para>
/// </summary>
public static class Figures
{
    /// <summary>An amount, from 1/100 of a currency unit: 12050 is "120.50".</summary>
    public static string Amount(long minorUnits) =>
        (minorUnits / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A quantity, from thousandths of its unit: 1500 is "1.5".</summary>
    public static string Quantity(long thousandths) =>
        (thousandths / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
}
