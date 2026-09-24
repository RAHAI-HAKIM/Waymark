using Waymark.Domain.Organisation;

namespace Waymark.Domain.Tests;

/// <summary>
/// Which stored PIN hashes can never authenticate anybody (session A2).
///
/// <para>
/// The generator writes <see cref="StaffPin.NeverUsable"/> into every synthetic staff row,
/// and a generated store is what the demo, the tests and every walkthrough run against. Once
/// a real verifier exists, that string has to fail closed <b>on purpose</b> — a verifier that
/// merely hashes the offered PIN and finds it does not equal <c>"synthetic:no-login"</c>
/// refuses today by luck, and starts accepting the day the stored format changes.
/// </para>
/// </summary>
public sealed class StaffPinTests
{
    [Fact]
    public void The_synthetic_sentinel_can_never_be_used()
    {
        Assert.False(StaffPin.IsUsable(StaffPin.NeverUsable));
    }

    [Fact]
    public void The_sentinel_is_the_literal_the_generator_writes()
    {
        // Pinned as a literal, not as a reference to the constant, because the constant is
        // what is under test. Waymark.Generator cannot be referenced by anything that ships
        // (D-054), so this string exists in two places and the two must not drift.
        Assert.Equal("synthetic:no-login", StaffPin.NeverUsable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_hash_that_is_absent_or_blank_can_never_be_used(string? storedHash)
    {
        // A blank pin_hash is a broken row, not an account with an empty PIN. The column is
        // NOT NULL, so null only happens if something upstream stopped filling it — which is
        // precisely when failing open would be worst.
        Assert.False(StaffPin.IsUsable(storedHash));
    }

    [Fact]
    public void A_real_looking_hash_is_usable()
    {
        // The counter-case, so "always false" does not pass this suite. What a real hash
        // looks like is Hakim's choice of algorithm; that it is not the sentinel and not
        // blank is all this rule knows.
        Assert.True(StaffPin.IsUsable("v1$210000$Ks9Xq2sT1pQ7$0f3c8a1d9e7b4a25c6d0f8e3b1a4c7d9"));
    }

    [Fact]
    public void The_check_is_about_the_stored_value_not_the_offered_pin()
    {
        // A sanity guard on the shape of the API: IsUsable takes one argument. If a PIN ever
        // gets passed in here the question has been confused with verification, which is a
        // different function with a different failure mode.
        Assert.False(StaffPin.IsUsable(StaffPin.NeverUsable));
        Assert.True(StaffPin.IsUsable("anything-else"));
    }

    // ------------------------------------------------- the PIN itself (A5, D-083)

    [Theory]
    [InlineData("1234")]
    [InlineData("0000")]
    [InlineData("12345678")]
    [InlineData("908172")]
    public void Four_to_eight_digits_is_a_pin(string pin) =>
        Assert.True(StaffPin.IsWellFormed(pin));

    [Theory]
    [InlineData("123")]
    [InlineData("123456789")]
    [InlineData("")]
    [InlineData(null)]
    public void Too_short_too_long_or_absent_is_not(string? pin) =>
        Assert.False(StaffPin.IsWellFormed(pin));

    [Theory]
    [InlineData("12a4")]
    [InlineData("12 4")]
    [InlineData(" 1234")]
    [InlineData("1234 ")]
    [InlineData("-123")]
    [InlineData("12.4")]
    public void Anything_but_digits_is_not(string pin) =>
        Assert.False(StaffPin.IsWellFormed(pin));

    [Theory]
    // Arabic-Indic and Eastern Arabic digits: char.IsDigit says yes to both. The pad can only
    // type '0' to '9', so a PIN set in these could never be typed again.
    [InlineData("١٢٣٤")]
    [InlineData("۱۲۳۴")]
    [InlineData("１２３４")]
    public void Digits_from_another_script_are_not(string pin) =>
        Assert.False(StaffPin.IsWellFormed(pin));
}
