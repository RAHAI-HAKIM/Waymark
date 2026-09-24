using Waymark.Domain.Values;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// Figures as the cashier reads them (G1 kit §2). The silent failures: a thousands separator
/// that is a plain space, which the bidirectional algorithm treats as a word break so an Arabic
/// line can print "320,80 3"; a figure that went through a double; a minus that is a hyphen and
/// knocks a column out of line.
/// </summary>
public sealed class DisplayFiguresTests
{
    private const char Narrow = '\u202F';

    private static Money Dzd(long minorUnits) => Money.FromMinorUnits(minorUnits, Currency.Dzd);

    [Theory]
    [InlineData(332_080, "3\u202F320,80")]
    [InlineData(0, "0,00")]
    [InlineData(5, "0,05")]
    [InlineData(100_000, "1\u202F000,00")]
    [InlineData(99_999, "999,99")]
    [InlineData(123_456_789, "1\u202F234\u202F567,89")]
    [InlineData(-4_200, "\u221242,00")]
    [InlineData(-80, "\u22120,80")]
    public void An_amount_is_written_the_French_way(long minorUnits, string expected) =>
        Assert.Equal(expected, DisplayFigures.Amount(Dzd(minorUnits)));

    [Fact]
    public void Thousands_are_never_separated_by_a_breaking_or_plain_space()
    {
        // The rule the kit states outright: U+202F, never a space. A regular space or a no-break
        // space here reads fine in French and scrambles the digits in Arabic.
        var text = DisplayFigures.Amount(Dzd(123_456_789));

        Assert.DoesNotContain(' ', text);
        Assert.DoesNotContain('\u00A0', text);
        Assert.Equal(2, text.Count(c => c == Narrow));
    }

    [Fact]
    public void A_negative_amount_carries_a_true_minus_not_a_hyphen()
    {
        var text = DisplayFigures.Amount(Dzd(-4_200));

        Assert.StartsWith("\u2212", text, StringComparison.Ordinal);
        Assert.DoesNotContain('-', text);
    }

    [Fact]
    public void The_currency_follows_the_language_after_a_no_break_space()
    {
        Assert.Equal("3\u202F320,80\u00A0DA", DisplayFigures.AmountWithCurrency(Dzd(332_080), TillText.French));
        Assert.Equal("3\u202F320,80\u00A0د.ج", DisplayFigures.AmountWithCurrency(Dzd(332_080), TillText.Arabic));
    }

    [Theory]
    [InlineData(4, "4")]
    [InlineData(1_240, "1\u202F240")]
    [InlineData(-3, "\u22123")]
    public void A_count_is_grouped_like_every_other_figure(long count, string expected) =>
        Assert.Equal(expected, DisplayFigures.Count(count));

    [Fact]
    public void Clock_and_day_are_fixed_formats()
    {
        var moment = new DateTimeOffset(2026, 9, 23, 14, 32, 59, TimeSpan.FromHours(1));

        Assert.Equal("14:32", DisplayFigures.Clock(moment));
        Assert.Equal("25/09", DisplayFigures.DayAndMonth(new DateOnly(2026, 9, 25)));
    }
}
