using Waymark.Domain.Values;

namespace Waymark.Generator.Simulation;

/// <summary>
/// What is on the shelf, kept in memory while the history is simulated.
///
/// <para>
/// Entities are init-only and a year of stock levels is millions of reads, so the simulation
/// holds the mutable truth here and writes the append-only facts — receipts, sales,
/// write-offs — as they happen. <c>inventories</c> is written from this at the end
/// (<see cref="FinalState"/>), and a test holds the two to
/// <c>closing = opening + Σ(deltas)</c>.
/// </para>
/// </summary>
internal sealed class StoreState
{
    private readonly List<StockLot>[] _lots;
    private readonly List<int>[] _dailySales;

    public StoreState(int variantCount)
    {
        _lots = [.. Enumerable.Range(0, variantCount).Select(_ => new List<StockLot>())];
        _dailySales = [.. Enumerable.Range(0, variantCount).Select(_ => new List<int>())];
    }

    /// <summary>Appends a finished day's units sold per variant: the shopkeeper's memory.</summary>
    public void RecordDaySales(IReadOnlyList<int> unitsSold)
    {
        ArgumentNullException.ThrowIfNull(unitsSold);
        for (var i = 0; i < _dailySales.Length; i++)
        {
            _dailySales[i].Add(unitsSold[i]);
        }
    }

    /// <summary>Units sold on each of the last <paramref name="days"/> finished days, oldest first; fewer early in the history.</summary>
    public IReadOnlyList<int> RecentSales(int variantIndex, int days)
    {
        var history = _dailySales[variantIndex];
        var count = Math.Min(days, history.Count);
        return history.GetRange(history.Count - count, count);
    }

    /// <summary>The lots of one variant, oldest received first — the FIFO order sales consume in.</summary>
    public IReadOnlyList<StockLot> LotsOf(int variantIndex) => _lots[variantIndex];

    /// <summary>Every lot of every variant, variants in catalogue order.</summary>
    public IEnumerable<(int VariantIndex, StockLot Lot)> AllLots() =>
        _lots.SelectMany((lots, index) => lots.Select(lot => (index, lot)));

    /// <summary>Adds a received lot at the back of the FIFO queue.</summary>
    public void Receive(int variantIndex, StockLot lot) => _lots[variantIndex].Add(lot);

    /// <summary>Everything of a variant on hand, sellable or not, in thousandths.</summary>
    public long OnHand(int variantIndex) => _lots[variantIndex].Sum(lot => lot.OnHand.Thousandths);

    /// <summary>What of a variant can be sold on <paramref name="date"/>, in thousandths: on hand and not expired.</summary>
    public long Sellable(int variantIndex, DateOnly date) =>
        _lots[variantIndex].Where(lot => lot.IsSellableOn(date)).Sum(lot => lot.OnHand.Thousandths);

    /// <summary>
    /// Takes <paramref name="quantity"/> of a variant off the shelf, oldest sellable lot first,
    /// and says which lots it came from.
    ///
    /// <para>
    /// An expired lot is skipped, not consumed: it stays on hand until it is written off, and
    /// nobody sells it. Asking for more than is sellable throws, because the caller decides how
    /// much sells and a silent partial take would lose the difference.
    /// </para>
    /// </summary>
    public IReadOnlyList<(StockLot Lot, Quantity Taken)> Take(int variantIndex, DateOnly date, Quantity quantity)
    {
        if (quantity.Thousandths > Sellable(variantIndex, date))
        {
            throw new InvalidOperationException(
                $"Variant {variantIndex}: asked for {quantity} on {date:yyyy-MM-dd} with only "
                + $"{Sellable(variantIndex, date)} thousandths sellable. The caller must cap a sale at what is sellable.");
        }

        var taken = new List<(StockLot, Quantity)>();
        var outstanding = quantity.Thousandths;

        foreach (var lot in _lots[variantIndex])
        {
            if (outstanding == 0)
            {
                break;
            }

            if (!lot.IsSellableOn(date))
            {
                continue;
            }

            var take = Quantity.FromThousandths(Math.Min(outstanding, lot.OnHand.Thousandths), quantity.Unit!);
            lot.Apply(QuantityDelta.Decrease(take));
            taken.Add((lot, take));
            outstanding -= take.Thousandths;
        }

        return taken;
    }
}

/// <summary>One variant's quantity from one batch.</summary>
internal sealed class StockLot(string batchId, DateOnly received, DateOnly? expires, Money unitCost, Quantity onHand)
{
    public string BatchId { get; } = batchId;

    public DateOnly Received { get; } = received;

    /// <summary>The last day the lot may be sold. Null for a non-perishable.</summary>
    public DateOnly? Expires { get; } = expires;

    /// <summary>Purchase cost per selling unit, HT.</summary>
    public Money UnitCost { get; } = unitCost;

    /// <summary>A level, not a change: it only moves by <see cref="Apply"/>.</summary>
    public Quantity OnHand { get; private set; } = onHand;

    /// <summary>Applies a stock movement. Level plus change is a level (D-036).</summary>
    public void Apply(QuantityDelta change) => OnHand += change;

    /// <summary>
    /// Whether any of this lot can be sold on <paramref name="date"/>. The expiration date is
    /// the last day of sale, so a lot is still sold on it and never after.
    /// </summary>
    public bool IsSellableOn(DateOnly date) => OnHand.IsPositive && (Expires is null || date <= Expires);
}
