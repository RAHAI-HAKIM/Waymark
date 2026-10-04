using Waymark.Domain.Catalogue;

namespace Waymark.Domain.Tests;

/// <summary>
/// Finding a product by what the cashier typed (session B1, D-088). The silent failures: an accent
/// that hides a product ("creme" not finding "Crème"), a half word that finds nothing, a list whose
/// first result is not the product the cashier meant, and one letter answering with the whole shop.
/// </summary>
public sealed class NameSearchTests
{
    [Theory]
    [InlineData("lait dem", "Lait UHT Candia Grand Lait Demi-écrémé Brique 1L")]
    [InlineData("LAIT", "Lait UHT Candia")]
    [InlineData("creme", "Crème fraîche")]
    [InlineData("crème", "Creme fraiche")]
    [InlineData("ecreme", "Demi-écrémé")]
    [InlineData("oeuf", "Œufs frais")]
    [InlineData("1l", "Brique 1L")]
    [InlineData("candia lait", "Lait UHT Candia")]
    public void Every_word_typed_starts_a_word_of_the_name(string typed, string name) =>
        Assert.NotNull(NameSearch.Score(typed, name));

    [Theory]
    [InlineData("lait choc", "Lait UHT Candia")]
    [InlineData("andia", "Lait UHT Candia")]
    [InlineData("ait", "Lait")]
    [InlineData("lait-x", "Lait UHT")]
    public void A_word_that_starts_no_word_of_the_name_finds_nothing(string typed, string name) =>
        Assert.Null(NameSearch.Score(typed, name));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("l")]
    [InlineData("-")]
    [InlineData(null)]
    public void Fewer_than_two_characters_search_nothing(string? typed) =>
        Assert.Null(NameSearch.Score(typed, "Lait"));

    [Fact]
    public void A_name_holding_the_word_whole_comes_before_one_that_only_starts_with_it()
    {
        // "Petit Lait" sorts last by name: only the rank can put it first.
        var ranked = NameSearch.Rank("lait", ["Laitue batavia", "Laitage", "Petit Lait"], name => name);

        Assert.Equal(["Petit Lait", "Laitage", "Laitue batavia"], ranked);
    }

    [Fact]
    public void Among_names_that_answer_equally_the_ones_the_typed_word_begins_come_first()
    {
        // Entrée sells the first result: "lait" sold biscuits, which the name's order put first (block B review).
        var ranked = NameSearch.Rank("lait", ["Biscuits au lait", "Café au lait", "Lait UHT Candia", "Laitue"], name => name);

        Assert.Equal(["Lait UHT Candia", "Biscuits au lait", "Café au lait", "Laitue"], ranked);
    }

    [Theory]
    [InlineData("lait", "Lait UHT Candia", true)]
    [InlineData("lai", "Laitue batavia", true)]
    [InlineData("LAIT", "lait entier", true)]
    [InlineData("lait", "Biscuits au lait", false)]
    [InlineData("candia lait", "Lait UHT Candia", false)] // the first word typed, not any of them
    [InlineData("", "Lait", false)]
    [InlineData("lait", "", false)]
    public void A_name_is_led_by_the_first_word_typed_when_it_begins_with_it(string typed, string name, bool leads) =>
        Assert.Equal(leads, NameSearch.Leads(typed, name));

    [Fact]
    public void Ties_are_in_the_order_of_the_name()
    {
        var ranked = NameSearch.Rank("br", ["Brique Candia", "Brioche", "Bretzel"], name => name);

        Assert.Equal(["Bretzel", "Brioche", "Brique Candia"], ranked);
    }

    [Fact]
    public void The_list_stops_at_twenty()
    {
        var many = Enumerable.Range(1, 30).Select(i => $"Lait {i:D2}");

        Assert.Equal(NameSearch.MaximumResults, NameSearch.Rank("lait", many, name => name).Count);
    }

    [Fact]
    public void Words_are_folded_and_split_at_anything_but_letters_and_digits()
    {
        Assert.Equal(["demi", "ecreme", "brique", "1l"], NameSearch.Words("Demi-écrémé (Brique 1L)"));
    }
}
