using Waymark.Domain.Values;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// A weight typed at the till (B3, D-090). The silent failures: "0,556" read as 556 kg; a French
/// comma refused; a gram typed into a unit sold to ten grams; a weight with a digit too many sold.
/// </summary>
public sealed class WeightEntryTests
{
    [Theory]
    [InlineData("0,556", 3, 556)]
    [InlineData("0.556", 3, 556)]
    [InlineData(" 1,5 ", 3, 1_500)]
    [InlineData("2", 3, 2_000)]
    [InlineData("12,05", 2, 12_050)]
    [InlineData("99,999", 3, 99_999)]
    [InlineData("0,001", 3, 1)]
    [InlineData("3", 0, 3_000)]
    public void A_weight_the_unit_can_take_is_read_as_thousandths(string typed, int decimals, long thousandths)
    {
        Assert.True(WeightEntry.TryParse(typed, decimals, out var read));
        Assert.Equal(thousandths, read);
    }

    [Theory]
    [InlineData("0,5555", 3)] // finer than a gram
    [InlineData("0,555", 2)]  // finer than ten grams
    [InlineData("1,5", 0)]    // a unit sold whole
    [InlineData("0", 3)]
    [InlineData("0,000", 3)]
    [InlineData("100", 3)]    // past 99,999 kg: a digit too many
    [InlineData("1,", 3)]     // unfinished
    [InlineData(",5", 3)]
    [InlineData("1,2,3", 3)]
    [InlineData("-1", 3)]
    [InlineData("1 kg", 3)]
    [InlineData("١,٥", 3)]    // Arabic-Indic digits, as a PIN refuses them
    [InlineData("", 3)]
    [InlineData(null, 3)]
    public void Anything_else_is_refused(string? typed, int decimals) =>
        Assert.False(WeightEntry.TryParse(typed, decimals, out _));

    [Theory]
    [InlineData(556, 3, "0,556")]
    [InlineData(1_500, 3, "1,500")]
    [InlineData(1_500, 2, "1,50")]
    [InlineData(1_555, 2, "1,555")] // never rounded: a finer weight shows every thousandth
    [InlineData(2_000, 0, "2")]
    [InlineData(12_345_000, 3, "12 345,000")]
    public void A_weight_is_shown_with_its_units_decimals(long thousandths, int decimals, string shown) =>
        Assert.Equal(shown, DisplayFigures.Weight(Quantity.FromThousandths(thousandths, "kg"), decimals));
}
