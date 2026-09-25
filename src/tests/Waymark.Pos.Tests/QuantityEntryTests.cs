using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// A count typed between − and + (B2). The silent failures: a sign or a stray digit sold as a
/// quantity, and Arabic-Indic digits read as a number a till in French could never have shown.
/// </summary>
public sealed class QuantityEntryTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("25", 25)]
    [InlineData(" 240 ", 240)]
    [InlineData("9999", 9_999)]
    [InlineData("1\u202F200", 1_200)] // as the field shows a count it was given
    [InlineData("007", 7)]
    public void A_whole_count_from_one_to_the_cap_is_read(string typed, int expected)
    {
        Assert.True(QuantityEntry.TryParse(typed, out var count));
        Assert.Equal(expected, count);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("10000")]
    [InlineData("-3")]
    [InlineData("+3")]
    [InlineData("2,5")]
    [InlineData("2.5")]
    [InlineData("1 2")]
    [InlineData("١٢")]
    [InlineData("abc")]
    public void Anything_else_is_refused(string? typed)
    {
        Assert.False(QuantityEntry.TryParse(typed, out var count));
        Assert.Equal(0, count);
    }
}
