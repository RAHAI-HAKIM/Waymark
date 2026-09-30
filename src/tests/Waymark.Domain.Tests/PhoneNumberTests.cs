using Waymark.Domain.Customers;

namespace Waymark.Domain.Tests;

/// <summary>
/// A customer's telephone number in one form (B7, D-096). The silent failure: the same person
/// stored as "0550 12 34 56" and found again as "0550123456", or not at all, so a second customer
/// is created with a second tab and each looks within its limit.
/// </summary>
public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("0550123456")]
    [InlineData("0550 12 34 56")]
    [InlineData("0550-12-34-56")]
    [InlineData("0550.12.34.56")]
    [InlineData("+213550123456")]
    [InlineData("+213 550 12 34 56")]
    [InlineData("00213550123456")]
    [InlineData("(0550) 12 34 56")]
    public void Every_way_of_writing_one_mobile_is_the_same_number(string typed)
    {
        Assert.True(PhoneNumber.TryNormalise(typed, out var normalised));
        Assert.Equal("+213550123456", normalised);
    }

    [Theory]
    [InlineData("0661234567", "+213661234567")]
    [InlineData("0770123456", "+213770123456")]
    [InlineData("021 23 45 67 8", "+213212345678")] // a fixed line in Algiers
    [InlineData("0381234567", "+213381234567")]
    public void A_mobile_or_a_fixed_line_is_kept(string typed, string expected)
    {
        Assert.True(PhoneNumber.TryNormalise(typed, out var normalised));
        Assert.Equal(expected, normalised);
    }

    [Theory]
    [InlineData("+33612345678")]
    [InlineData("+491701234567")]
    public void A_number_from_abroad_is_kept_as_typed(string typed)
    {
        Assert.True(PhoneNumber.TryNormalise(typed, out var normalised));
        Assert.Equal(typed, normalised);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("055012345")]     // a digit short
    [InlineData("05501234567")]   // a digit too many
    [InlineData("0150123456")]    // no such prefix
    [InlineData("0850123456")]
    [InlineData("0550l23456")]    // a letter l for a 1
    [InlineData("550123456")]     // no leading 0
    [InlineData("+21355012345")]  // +213 a digit short
    [InlineData("+123")]
    public void Anything_else_is_not_a_number(string? typed)
    {
        Assert.False(PhoneNumber.TryNormalise(typed, out var normalised));
        Assert.Null(normalised);
    }
}
