using Waymark.Domain.Sales;

namespace Waymark.Domain.Tests;

/// <summary>
/// A card or BaridiMob part's reference (B6, D-095). The silent failure: a card number typed in the
/// reference field, kept on the payment row and carried into every backup, with nothing saying so.
/// </summary>
public sealed class PaymentReferenceTests
{
    [Theory]
    [InlineData("4417")]           // the last four digits
    [InlineData("A1B2C3")]         // an authorisation number
    [InlineData("88213")]          // a transfer's id
    [InlineData("1234567890123")]  // 13 digits that fail the Luhn check: not a card
    public void An_ordinary_reference_is_kept(string typed)
    {
        Assert.Equal(ReferenceVerdict.Accepted, PaymentReference.Read(typed, out var kept));
        Assert.Equal(typed, kept);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_reference_is_fine_and_keeps_nothing(string? typed)
    {
        Assert.Equal(ReferenceVerdict.Accepted, PaymentReference.Read(typed, out var kept));
        Assert.Null(kept);
    }

    [Fact]
    public void A_reference_is_trimmed()
    {
        PaymentReference.Read("  4417 ", out var kept);

        Assert.Equal("4417", kept);
    }

    [Theory]
    [InlineData("4970101234567893")]      // 16 digits, Luhn-valid
    [InlineData("4970 1012 3456 7893")]   // with spaces
    [InlineData("4970-1012-3456-7893")]   // with dashes
    [InlineData("6280580000000000000")]   // 19 digits, Luhn-valid
    [InlineData("4222222222222")]         // 13 digits, Luhn-valid
    public void A_card_number_is_refused_and_nothing_is_kept(string typed)
    {
        Assert.Equal(ReferenceVerdict.LooksLikeACardNumber, PaymentReference.Read(typed, out var kept));
        Assert.Null(kept);
    }

    [Theory]
    [InlineData("4970101234567898")]      // one digit off: fails Luhn, an ordinary long number
    [InlineData("497010123456")]          // 12 digits: too short to be a card
    public void A_long_number_that_is_no_card_is_kept(string typed)
    {
        Assert.Equal(ReferenceVerdict.Accepted, PaymentReference.Read(typed, out _));
    }

    [Theory]
    [InlineData("réf#12")]
    [InlineData("123456789012345678901234567890123")] // 33 characters
    public void A_character_no_terminal_prints_or_a_reference_too_long_is_invalid(string typed)
    {
        Assert.Equal(ReferenceVerdict.Invalid, PaymentReference.Read(typed, out var kept));
        Assert.Null(kept);
    }
}
