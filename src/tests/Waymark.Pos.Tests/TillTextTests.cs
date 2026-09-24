using System.Reflection;
using Waymark.Contracts.Pos;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// The till's words, in both languages (G1). The silent failures: a string forgotten in one
/// language, which shows blank or French in an Arabic till; a refusal reason with no words,
/// which shows its raw code; and Arabic plurals, which take four forms and look right in
/// French-trained eyes when they are wrong.
/// </summary>
public sealed class TillTextTests
{
    public static TheoryData<TillLanguage> Languages() => new() { TillLanguage.French, TillLanguage.Arabic };

    [Theory]
    [MemberData(nameof(Languages))]
    public void No_word_is_missing_in_either_language(TillLanguage language)
    {
        var text = TillText.For(language);
        var missing = typeof(TillText)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string))
            .Where(property => string.IsNullOrWhiteSpace((string?)property.GetValue(text)))
            .Select(property => property.Name)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Arabic_is_not_French_left_untranslated()
    {
        // A property overridden with the French words would pass the test above.
        var same = typeof(TillText)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string))
            .Where(property => Equals(property.GetValue(TillText.French), property.GetValue(TillText.Arabic)))
            .Select(property => property.Name)
            .ToList();

        Assert.Empty(same);
    }

    public static TheoryData<string, TillLanguage> RefusalReasons()
    {
        var data = new TheoryData<string, TillLanguage>();
        foreach (var field in typeof(NotSellableReason).GetFields())
        {
            data.Add((string)field.GetValue(null)!, TillLanguage.French);
            data.Add((string)field.GetValue(null)!, TillLanguage.Arabic);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RefusalReasons))]
    public void Every_refusal_reason_has_words_in_both_languages(string reason, TillLanguage language)
    {
        // A reason added to the contract without words would fall through to "(reason_code)",
        // which a cashier cannot act on.
        var words = TillText.For(language).NotSellableReason(reason);

        Assert.DoesNotContain(reason, words, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "1 ligne")]
    [InlineData(2, "2 lignes")]
    [InlineData(14, "14 lignes")]
    public void French_counts_lines_with_one_plural(int count, string expected) =>
        Assert.Equal(expected, TillText.French.Lines(count));

    [Theory]
    // The four forms, three of them exactly as Hakim's Arabic board writes them.
    [InlineData(1, "سطر واحد")]
    [InlineData(2, "سطران")]
    [InlineData(3, "3 أسطر")]
    [InlineData(10, "10 أسطر")]
    [InlineData(14, "14 سطرًا")]
    public void Arabic_counts_lines_in_its_four_forms(int count, string expected) =>
        Assert.Equal(expected, TillText.Arabic.Lines(count));

    [Theory]
    [InlineData(1, "بطاقة واحدة بانتظار المسؤول")]
    [InlineData(2, "بطاقتان بانتظار المسؤول")]
    public void Arabic_counts_cards_in_its_four_forms(int count, string expected) =>
        Assert.Equal(expected, TillText.Arabic.CardsAwaitingManager(count));

    [Fact]
    public void The_Arabic_board_words_are_the_ones_used()
    {
        // Spot checks against Hakim's Arabic board: where it has the word, the till uses it,
        // unless he has since changed it (the pay key, 23/09).
        var ar = TillText.Arabic;

        Assert.Equal("التذكرة الحالية", ar.CurrentTicket);
        Assert.Equal("الدفع", ar.Collect);
        Assert.Equal("متصل", ar.Online);
        Assert.Equal("يتجاوز المخزون المسجّل", ar.BeyondRecordedStock);
        Assert.Equal("المجموع المستحق", ar.TotalToPay);
    }

    [Fact]
    public void Arabic_runs_right_to_left_and_French_does_not()
    {
        Assert.True(TillText.Arabic.RightToLeft);
        Assert.False(TillText.French.RightToLeft);
    }
}
