using Waymark.Domain.Values;

namespace Waymark.Domain.Inventory;

/// <summary>One batch of a variant as a sale sees it: what is on hand, and when it came in and expires.</summary>
/// <param name="BatchId">The batch.</param>
/// <param name="ReceivedDate">When it came in; the oldest sells first.</param>
/// <param name="ExpirationDate">When it expires, if it does.</param>
/// <param name="OnHand">This store's level of the variant in the batch.</param>
/// <param name="UnitCost">What one unit cost, for <c>unit_cost_at_sale</c>; null if unknown.</param>
public sealed record BatchLevel(
    string BatchId,
    DateOnly ReceivedDate,
    DateOnly? ExpirationDate,
    Quantity OnHand,
    Money? UnitCost);

/// <summary>
/// Which batches a sold quantity comes out of (D-070). Every stock movement names a batch
/// (<c>stock_movements.batch_id</c> is required), so a sale has to say which.
///
/// <para>
/// <b>First in, first out</b>, as the synthetic store sells: the oldest batch received
/// first, skipping expired and empty ones, and a line splits across batches when one runs
/// short. <b>A shortfall is not a refusal</b> (CLAUDE.md §3.8: a level going negative is
/// not a bug): what the records say is not on the shelf is taken from the most recently
/// received batch, whose count is the likeliest to be wrong. Only a variant with no batch
/// at all cannot be sold, because there is nothing to name.
/// </para>
/// </summary>
public static class BatchAllocation
{
    /// <summary>The batches and quantities <paramref name="quantity"/> comes out of, in the order taken.</summary>
    /// <exception cref="InvalidOperationException">The variant has no batch at all.</exception>
    public static IReadOnlyList<(BatchLevel Batch, Quantity Taken)> Take(
        IReadOnlyList<BatchLevel> batches, Quantity quantity, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(batches);
        if (!quantity.IsPositive)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "A sale takes a positive quantity.");
        }

        if (batches.Count == 0)
        {
            throw new InvalidOperationException(
                "The variant has no batch, so no stock movement can name where it came from. Receive it first.");
        }

        var unit = quantity.Unit!;
        var ordered = batches
            .OrderBy(batch => batch.ReceivedDate)
            .ThenBy(batch => batch.BatchId, StringComparer.Ordinal)
            .ToList();

        var taken = new List<(BatchLevel, Quantity)>();
        var outstanding = quantity.Thousandths;

        foreach (var batch in ordered)
        {
            if (outstanding == 0)
            {
                break;
            }

            var expired = batch.ExpirationDate is { } expiry && expiry < today;
            if (expired || !batch.OnHand.IsPositive)
            {
                continue;
            }

            var take = Math.Min(outstanding, batch.OnHand.Thousandths);
            taken.Add((batch, Quantity.FromThousandths(take, unit)));
            outstanding -= take;
        }

        if (outstanding > 0)
        {
            var newest = ordered[^1];
            var index = taken.FindIndex(entry => entry.Item1.BatchId == newest.BatchId);
            if (index < 0)
            {
                taken.Add((newest, Quantity.FromThousandths(outstanding, unit)));
            }
            else
            {
                taken[index] = (newest, Quantity.FromThousandths(taken[index].Item2.Thousandths + outstanding, unit));
            }
        }

        return taken;
    }
}
