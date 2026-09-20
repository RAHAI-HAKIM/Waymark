using Waymark.Domain.Engine;
using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// The near-expiry rule (hop 6, D-073). <b>The risky part is the window's edge</b>: a rule
/// that is one day out flags a shelf of cards nobody asked for, or stays quiet on the day a
/// shopkeeper could still have sold the stock — and both look like the rule working.
/// </summary>
public sealed class NearExpiryTests
{
    private static readonly DateOnly Today = new(2026, 9, 20);

    private const int Window = 7;

    private static BatchStock Batch(int? expiresInDays, params (long Units, long Cost)[] lines) => new(
        "batch-1",
        "product-1",
        "Lait UHT",
        expiresInDays is { } days ? Today.AddDays(days) : null,
        [.. lines.Select((line, index) => new BatchStockLine(
            $"variant-{index}",
            Quantity.FromThousandths(line.Units * Quantity.Scale, "pc"),
            Money.FromMinorUnits(line.Cost, Currency.Dzd)))]);

    private static NearExpiryFinding? Evaluate(BatchStock batch, int window = Window) =>
        NearExpiry.Evaluate(batch, window, Today);

    // --------------------------------------------------------- the window's edge

    [Fact]
    public void A_batch_expiring_exactly_on_the_window_is_flagged()
    {
        // The window is "within this many days", inclusive. Exclusive would mean the seventh
        // day is silently outside a seven-day window.
        var finding = Evaluate(Batch(expiresInDays: Window, (10, 10_000)));

        Assert.NotNull(finding);
        Assert.Equal(Window, finding.DaysToExpiry);
    }

    [Fact]
    public void A_batch_expiring_one_day_past_the_window_is_not()
    {
        Assert.Null(Evaluate(Batch(expiresInDays: Window + 1, (10, 10_000))));
    }

    [Fact]
    public void A_batch_expiring_today_is_flagged_with_no_days_left()
    {
        var finding = Evaluate(Batch(expiresInDays: 0, (10, 10_000)));

        Assert.NotNull(finding);
        Assert.Equal(0, finding.DaysToExpiry);
        Assert.Equal(Urgency.Warning, finding.Urgency);
    }

    [Fact]
    public void A_batch_already_past_its_date_is_critical_and_counts_backwards()
    {
        // Still on the shelf and no longer sellable: the loudest case the rule has, and the
        // only one where the days are negative.
        var finding = Evaluate(Batch(expiresInDays: -2, (10, 10_000)));

        Assert.NotNull(finding);
        Assert.Equal(-2, finding.DaysToExpiry);
        Assert.Equal(Urgency.Critical, finding.Urgency);
    }

    [Fact]
    public void A_window_of_zero_flags_only_what_expires_today_or_earlier()
    {
        Assert.NotNull(Evaluate(Batch(expiresInDays: 0, (10, 10_000)), window: 0));
        Assert.Null(Evaluate(Batch(expiresInDays: 1, (10, 10_000)), window: 0));
    }

    [Fact]
    public void A_negative_window_is_refused_rather_than_read_as_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NearExpiry.Evaluate(Batch(expiresInDays: 1, (10, 10_000)), -1, Today));
    }

    // --------------------------------------------------------- what is not said

    [Fact]
    public void A_batch_with_no_expiry_date_is_never_flagged()
    {
        // Flour has no shelf life in the catalogue. A rule that read "no date" as "expired
        // today" would bury the real cards under every dry good in the shop.
        Assert.Null(Evaluate(Batch(expiresInDays: null, (10, 10_000))));
    }

    [Fact]
    public void A_batch_with_nothing_left_is_not_a_card()
    {
        // Sold out is not near expiry, it is finished; there is no positive state to report
        // and nothing to act on either (CLAUDE.md §6).
        Assert.Null(Evaluate(Batch(expiresInDays: 1, (0, 10_000))));
    }

    // --------------------------------------------------------- the figures

    [Fact]
    public void The_stock_of_every_variant_in_the_batch_is_added_up()
    {
        var finding = Evaluate(Batch(expiresInDays: 3, (4, 10_000), (6, 10_000)));

        Assert.NotNull(finding);
        Assert.Equal(Quantity.FromThousandths(10 * Quantity.Scale, "pc"), finding.OnHand);
    }

    [Fact]
    public void The_value_at_risk_is_what_the_remaining_stock_cost()
    {
        // 4 × 100.00 + 6 × 80.00 = 880.00, each line rounded once.
        var finding = Evaluate(Batch(expiresInDays: 3, (4, 10_000), (6, 8_000)));

        Assert.NotNull(finding);
        Assert.Equal(Money.FromMinorUnits(88_000, Currency.Dzd), finding.ValueAtCost);
    }

    [Fact]
    public void A_part_unit_is_costed_at_the_engine_s_rounding()
    {
        // Half of a 25.01 DZD unit is 12.505, and half-even takes it to 12.50 — never the
        // store's own policy, which belongs to what it charges (CLAUDE.md §3.1).
        var half = new BatchStock(
            "batch-1", "product-1", "Fromage", Today.AddDays(1),
            [new BatchStockLine("variant-0", Quantity.FromThousandths(500, "kg"), Money.FromMinorUnits(2_501, Currency.Dzd))]);

        var finding = NearExpiry.Evaluate(half, Window, Today);

        Assert.NotNull(finding);
        Assert.Equal(Money.FromMinorUnits(1_250, Currency.Dzd), finding.ValueAtCost);
    }

    [Fact]
    public void A_batch_stocked_in_two_units_keeps_its_value_and_drops_its_quantity()
    {
        // Pieces and kilogrammes do not add up, and inventing a total would be the silent kind
        // of wrong. The card carries one figure fewer instead.
        var mixed = new BatchStock(
            "batch-1", "product-1", "Olives", Today.AddDays(2),
            [
                new BatchStockLine("variant-0", Quantity.FromThousandths(2 * Quantity.Scale, "pc"), Money.FromMinorUnits(10_000, Currency.Dzd)),
                new BatchStockLine("variant-1", Quantity.FromThousandths(3 * Quantity.Scale, "kg"), Money.FromMinorUnits(10_000, Currency.Dzd)),
            ]);

        var finding = NearExpiry.Evaluate(mixed, Window, Today);

        Assert.NotNull(finding);
        Assert.Null(finding.OnHand);
        Assert.Equal(Money.FromMinorUnits(50_000, Currency.Dzd), finding.ValueAtCost);
    }
}
