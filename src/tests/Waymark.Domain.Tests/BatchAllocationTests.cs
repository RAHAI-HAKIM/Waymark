using Waymark.Domain.Inventory;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Which batches a sale takes from (D-070). Wrong here is silent: every total still adds
/// up, but the per-batch levels drift, and expiry and write-off work on those levels.
/// </summary>
public sealed class BatchAllocationTests
{
    private static readonly DateOnly Today = new(2026, 9, 19);

    private static BatchLevel Batch(string id, int receivedDaysAgo, long units, int? expiresInDays = null) => new(
        id,
        Today.AddDays(-receivedDaysAgo),
        expiresInDays is { } days ? Today.AddDays(days) : null,
        Quantity.FromThousandths(units * Quantity.Scale, "pc"),
        UnitCost: null);

    private static Quantity Units(long units) => Quantity.FromThousandths(units * Quantity.Scale, "pc");

    private static List<(string Batch, long Units)> Take(IReadOnlyList<BatchLevel> batches, long units) =>
        [.. BatchAllocation.Take(batches, Units(units), Today).Select(t => (t.Batch.BatchId, t.Taken.Thousandths / Quantity.Scale))];

    [Fact]
    public void The_oldest_batch_sells_first()
    {
        var taken = Take([Batch("new", 2, 10), Batch("old", 20, 10)], 3);

        Assert.Equal([("old", 3L)], taken);
    }

    [Fact]
    public void A_line_splits_across_batches_when_the_oldest_runs_short()
    {
        var taken = Take([Batch("old", 20, 2), Batch("new", 2, 10)], 5);

        Assert.Equal([("old", 2L), ("new", 3L)], taken);
    }

    [Fact]
    public void An_expired_batch_is_skipped_even_when_oldest()
    {
        var taken = Take([Batch("expired", 30, 10, expiresInDays: -1), Batch("fresh", 5, 10, expiresInDays: 10)], 1);

        Assert.Equal([("fresh", 1L)], taken);
    }

    [Fact]
    public void A_batch_expiring_today_still_sells()
    {
        var taken = Take([Batch("today", 30, 10, expiresInDays: 0), Batch("later", 5, 10)], 1);

        Assert.Equal([("today", 1L)], taken);
    }

    [Fact]
    public void An_empty_or_negative_batch_is_skipped()
    {
        var taken = Take([Batch("empty", 30, 0), Batch("short", 20, -2), Batch("full", 5, 10)], 1);

        Assert.Equal([("full", 1L)], taken);
    }

    [Fact]
    public void A_shortfall_goes_to_the_newest_batch_rather_than_refusing_the_sale()
    {
        // CLAUDE.md §3.8: the product is in the customer's hand; the count was wrong.
        var taken = Take([Batch("old", 20, 1), Batch("new", 2, 0)], 3);

        Assert.Equal([("old", 1L), ("new", 2L)], taken);
    }

    [Fact]
    public void A_shortfall_joins_the_newest_batchs_own_take()
    {
        var taken = Take([Batch("old", 20, 1), Batch("new", 2, 1)], 4);

        Assert.Equal([("old", 1L), ("new", 3L)], taken);
    }

    [Fact]
    public void With_nothing_on_hand_anywhere_the_newest_batch_takes_it_all()
    {
        var taken = Take([Batch("old", 20, 0), Batch("new", 2, -1, expiresInDays: -3)], 2);

        Assert.Equal([("new", 2L)], taken);
    }

    [Fact]
    public void The_taken_quantities_add_up_to_what_was_sold()
    {
        var sold = Units(7);
        var taken = BatchAllocation.Take([Batch("a", 20, 2), Batch("b", 10, 1), Batch("c", 2, 1)], sold, Today);

        Assert.Equal(sold, Quantity.Sum("pc", taken.Select(t => t.Taken)));
    }

    [Fact]
    public void A_variant_with_no_batch_cannot_be_sold() =>
        Assert.Throws<InvalidOperationException>(() => BatchAllocation.Take([], Units(1), Today));

    [Fact]
    public void Ties_on_the_received_date_are_broken_by_batch_id_so_the_answer_never_varies()
    {
        var taken = Take([Batch("b", 5, 10), Batch("a", 5, 10)], 1);

        Assert.Equal([("a", 1L)], taken);
    }
}
