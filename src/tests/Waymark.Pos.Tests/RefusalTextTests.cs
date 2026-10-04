using System.Reflection;
using Waymark.Contracts.Pos;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// A server's refusal as the till says it (D-107). The silent failures: an English sentence on a
/// French or Arabic till; a code the server sends that one language has no sentence for; an amount
/// shown as the wire spells it.
/// </summary>
public sealed class RefusalTextTests
{
    private static string Plain(string shown) => shown.Replace(' ', ' ').Replace(' ', ' ');

    public static TheoryData<string> Codes() =>
        [.. typeof(RefusalCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => (string)field.GetRawConstantValue()!)];

    [Theory]
    [MemberData(nameof(Codes))]
    public void Every_code_the_server_may_send_has_its_sentence_in_both_languages(string code)
    {
        string[] names = ["Samira Benali", "100.00", "250.00"];

        foreach (var text in new[] { TillText.French, TillText.Arabic })
        {
            var said = text.Refusal(code, names);

            Assert.False(string.IsNullOrWhiteSpace(said), $"'{code}' has no sentence in {text.Language}.");
            Assert.DoesNotContain("?", said, StringComparison.Ordinal); // every name it asks for was given
        }
    }

    [Fact]
    public void A_refusal_names_its_amounts_the_tills_way()
    {
        var said = RefusalText.Say(TillText.French, new Refusal(RefusalCodes.TabAboveLimit, ["Samira Benali", "1714.00"]), "Samira Benali: past the tab's limit…");

        Assert.Equal("Carnet de Samira Benali : plafond dépassé, disponible 1 714,00 DA.", Plain(said));
    }

    [Fact]
    public void A_name_the_server_left_out_reads_as_a_question_mark_never_a_crash()
    {
        Assert.Equal("? n'a pas de carnet.", RefusalText.Say(TillText.French, new Refusal(RefusalCodes.TabNone), "x"));
    }

    [Fact]
    public void A_code_this_till_does_not_know_is_said_plainly_with_the_servers_words_behind_it()
    {
        var said = RefusalText.Say(TillText.French, new Refusal("a_rule_from_next_year", ["12"]), "A new rule refused this.");

        Assert.Equal("Refusé par le serveur · A new rule refused this.", said);
    }

    [Fact]
    public void A_refusal_with_no_code_keeps_the_servers_words_and_one_with_nothing_still_says_something()
    {
        Assert.Equal("Refusé par le serveur · A part of zero or less.", RefusalText.Say(TillText.French, null, "A part of zero or less."));
        Assert.Equal("Refusé par le serveur", RefusalText.Say(TillText.French, null, null));
        Assert.Equal("رفضه الخادم", RefusalText.Say(TillText.Arabic, null, " "));
    }
}
