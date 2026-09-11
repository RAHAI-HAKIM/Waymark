using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// The change half of the pair: direction in the sign, and the two factories
/// that force a caller to say which way stock moved.
/// </summary>
public sealed class QuantityDeltaTests
{
    private static Quantity Kg(long thousandths) => Quantity.FromThousandths(thousandths, "kg");

    private static QuantityDelta KgChange(long thousandths) => QuantityDelta.FromThousandths(thousandths, "kg");

    [Fact]
    public void Increase_and_decrease_put_the_direction_in_the_sign()
    {
        Assert.Equal(KgChange(2_500), QuantityDelta.Increase(Kg(2_500)));
        Assert.Equal(KgChange(-2_500), QuantityDelta.Decrease(Kg(2_500)));

        Assert.True(QuantityDelta.Increase(Kg(1)).IsIncrease);
        Assert.True(QuantityDelta.Decrease(Kg(1)).IsDecrease);
    }

    [Fact]
    public void A_negative_magnitude_is_refused_rather_than_quietly_flipped()
    {
        // Taking the absolute value would turn "decrease by -5" into a decrease
        // of 5, which is the opposite of what the caller wrote. The whole point
        // of the two factories is that direction is said out loud.
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = QuantityDelta.Increase(Kg(-5_000)); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = QuantityDelta.Decrease(Kg(-5_000)); });
    }

    [Fact]
    public void Magnitude_discards_the_direction()
    {
        Assert.Equal(Kg(2_500), KgChange(2_500).Magnitude);
        Assert.Equal(Kg(2_500), KgChange(-2_500).Magnitude);
        Assert.Equal(Kg(0), KgChange(0).Magnitude);
    }

    [Fact]
    public void Round_tripping_through_magnitude_and_back_returns_the_change()
    {
        foreach (var thousandths in new long[] { 1, -1, 5_000, -5_000, 123, -123 })
        {
            var change = KgChange(thousandths);
            var rebuilt = change.IsDecrease
                ? QuantityDelta.Decrease(change.Magnitude)
                : QuantityDelta.Increase(change.Magnitude);

            Assert.Equal(change, rebuilt);
        }
    }

    [Fact]
    public void Negating_a_change_is_the_reversing_movement()
    {
        // stock_movements is append-only: a mistake is corrected by posting the
        // opposite movement, never by editing the row.
        var mistake = KgChange(3_000);
        var correction = -mistake;

        Assert.Equal(KgChange(-3_000), correction);
        Assert.Equal(KgChange(0), mistake + correction);
    }

    [Fact]
    public void Zero_is_a_legal_value_even_though_it_is_not_a_legal_movement()
    {
        // CHECK (quantity_changed <> 0) stops a pointless row being written. It
        // cannot stop a receipt and a write-off cancelling, and a type that
        // refused zero could not express the sum at all.
        var receipt = KgChange(4_000);
        var writeOff = KgChange(-4_000);

        Assert.True((receipt + writeOff).IsZero);
        Assert.True((Kg(1_000) - Kg(1_000)).IsZero);
        Assert.True(QuantityDelta.Zero("kg").IsZero);
    }

    [Fact]
    public void Changes_sum_to_a_change()
    {
        QuantityDelta[] movements = [KgChange(10_000), KgChange(-2_500), KgChange(-1_000), KgChange(750)];

        Assert.Equal(KgChange(7_250), QuantityDelta.Sum("kg", movements));
    }

    [Fact]
    public void Summing_is_the_same_as_folding_with_the_operator()
    {
        QuantityDelta[] movements = [KgChange(10_000), KgChange(-2_500), KgChange(-1_000)];

        var folded = movements.Aggregate(QuantityDelta.Zero("kg"), QuantityDelta.Add);

        Assert.Equal(folded, QuantityDelta.Sum("kg", movements));
    }

    [Fact]
    public void Order_does_not_change_the_total()
    {
        // Movements arrive out of order after an offline period, and the closing
        // level must not depend on the order they are replayed in.
        QuantityDelta[] movements = [KgChange(10_000), KgChange(-2_500), KgChange(-1_000), KgChange(750)];

        var forwards = QuantityDelta.Sum("kg", movements);
        var backwards = QuantityDelta.Sum("kg", movements.Reverse());

        Assert.Equal(forwards, backwards);
    }

    [Fact]
    public void A_change_scaled_by_a_whole_number_stays_a_change()
    {
        Assert.Equal(KgChange(-7_500), KgChange(-2_500) * 3);
        Assert.Equal(KgChange(-7_500), 3 * KgChange(-2_500));
    }
}
