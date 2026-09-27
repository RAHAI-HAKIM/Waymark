using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// One field for everything (session B1, D-088). The silent failures: a code typed by hand sent to
/// the name search, a ticket number looked up as a barcode, and "3*" read as a code.
/// </summary>
public sealed class FieldInputTests
{
    [Theory]
    [InlineData("6130000000017")]
    [InlineData("4011")]
    [InlineData(" 2000000000015 ")]
    [InlineData("12/34")]
    public void A_code_typed_by_hand_is_a_code(string typed) =>
        Assert.Equal(FieldKind.Code, FieldInput.Read(typed).Kind);

    [Theory]
    [InlineData("lait")]
    [InlineData("Lait 1L")]
    [InlineData("demi-écrémé")]
    [InlineData("Coca-Cola 1.5")]
    [InlineData("مرق")]
    public void Text_with_a_letter_is_a_name(string typed) =>
        Assert.Equal(FieldKind.Name, FieldInput.Read(typed).Kind);

    [Theory]
    [InlineData("S-2026-000142")]
    [InlineData("GDZ-001-2025-000001")]
    [InlineData("s-2026-0142")]
    public void A_dash_and_four_digits_at_the_end_is_a_ticket_number(string typed) =>
        Assert.Equal(FieldKind.Ticket, FieldInput.Read(typed).Kind);

    [Theory]
    [InlineData("3*", 3)]
    [InlineData("12*", 12)]
    [InlineData(" 9999* ", 9_999)]
    [InlineData("*5", 5)] // the right-to-left field reads the star first
    [InlineData("*12", 12)]
    public void A_count_and_a_star_is_the_next_count(string typed, int count)
    {
        var entry = FieldInput.Read(typed);

        Assert.Equal((FieldKind.Multiplier, count), (entry.Kind, entry.Count));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("l")]
    [InlineData("0*")]
    [InlineData("10000*")]
    [InlineData("*")]
    [InlineData("a*")]
    [InlineData("**")]
    public void Nothing_to_act_on_is_nothing(string typed) =>
        Assert.Equal(FieldKind.Nothing, FieldInput.Read(typed).Kind);
}
