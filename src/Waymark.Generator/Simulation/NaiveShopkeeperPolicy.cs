using Waymark.Generator.Configuration;

namespace Waymark.Generator.Simulation;

/// <summary>
/// How the synthetic shopkeeper restocks: <b>deliberately mediocre</b> (D-046 §22–23). Every
/// later claim that Almanac orders better is measured against this rule, so it must be the
/// rule a busy épicier actually follows, not a good one.
///
/// <list type="bullet">
/// <item><b>Pace by eyeball:</b> the average of what sold over the last few days, stockout days
/// included, so an empty shelf teaches him the product sells less.</item>
/// <item><b>Reorder below a threshold</b> of a few days' pace, counting what is already on order.</item>
/// <item><b>Order up to</b> a longer cover, <b>rounded up to whole cartons</b>, never less than one.</item>
/// <item><b>Over-order before Ramadan</b>, across the board.</item>
/// <item><b>Slow movers are ignored until they run out</b>, then get one carton.</item>
/// </list>
/// Orders are placed only when a supplier's representative visits, on that supplier's delivery
/// days; <see cref="SupplyChain"/> decides when that is.
/// </summary>
internal sealed class NaiveShopkeeperPolicy(SupplySettings settings)
{
    /// <summary>
    /// Units a day the shopkeeper believes a product sells: the mean of the last
    /// <c>memory_days</c> days of sales. Days before the history began count at the product's
    /// base rate — he knew his shop before Waymark did.
    /// </summary>
    /// <param name="recentSales">Units sold on each remembered day that has happened, oldest first; at most memory_days.</param>
    /// <param name="baseRate">The catalogue's base rate, for the days before the history.</param>
    public double PerceivedRate(IReadOnlyList<int> recentSales, double baseRate)
    {
        ArgumentNullException.ThrowIfNull(recentSales);

        var memory = settings.MemoryDays.Value;
        var known = Math.Min(recentSales.Count, memory);
        var sold = recentSales.Skip(recentSales.Count - known).Sum();
        return (sold + ((memory - known) * baseRate)) / memory;
    }

    /// <summary>Cartons to order for one product, zero for none.</summary>
    /// <param name="perceivedRate">From <see cref="PerceivedRate"/>.</param>
    /// <param name="positionUnits">Sellable on the shelf plus already on order, in selling units.</param>
    /// <param name="unitsPerCarton">The supplier's carton.</param>
    /// <param name="ramadanComing">Whether Ramadan's stock-up is near.</param>
    public int CartonsToOrder(double perceivedRate, long positionUnits, int unitsPerCarton, bool ramadanComing)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(unitsPerCarton, 1);

        if (perceivedRate < settings.SlowMoverRate.Value)
        {
            return positionUnits <= 0 ? 1 : 0;
        }

        if (positionUnits > perceivedRate * settings.ReorderCoverDays.Value)
        {
            return 0;
        }

        var target = perceivedRate * settings.OrderUpToCoverDays.Value * (ramadanComing ? settings.RamadanOverOrder.Value : 1);
        var needed = target - positionUnits;
        return Math.Max(1, (int)Math.Ceiling(needed / unitsPerCarton));
    }
}
