using System.Runtime.Versioning;
using Waymark.Domain.Organisation;
using Waymark.StoreServer.Security;

namespace Waymark.Integration.Tests;

/// <summary>
/// Staff PINs, hashed with Argon2id (session A5, D-083). The silent failures: a sentinel that
/// is refused only by accident, a hash compared byte by byte so its timing leaks, parameters read
/// from constants so raising them later locks every cashier out, and a malformed row that crashes
/// the sign-in for everybody.
/// </summary>
[SupportedOSPlatform("windows")] // StoreServer is Windows-only (D-017), and so is its code.
public sealed class Argon2PinHasherTests
{
    private static readonly Argon2PinHasher Hasher = new();

    [Fact]
    public void A_pin_verifies_against_its_own_hash()
    {
        var stored = Hasher.Hash("4821");

        Assert.True(Hasher.Verify("4821", stored));
    }

    [Theory]
    [InlineData("4822")]
    [InlineData("48210")]
    [InlineData("1284")]
    public void Another_pin_does_not(string wrong)
    {
        Assert.False(Hasher.Verify(wrong, Hasher.Hash("4821")));
    }

    [Fact]
    public void The_stored_value_is_a_PHC_string_with_the_chosen_parameters()
    {
        var stored = Hasher.Hash("4821");

        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", stored, StringComparison.Ordinal);
        Assert.Equal(6, stored.Split('$').Length);
        Assert.DoesNotContain("4821", stored, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_pin_hashes_differently_every_time()
    {
        // A fresh salt per hash: two cashiers who chose 1234 must not have the same row.
        Assert.NotEqual(Hasher.Hash("1234"), Hasher.Hash("1234"));
    }

    [Fact]
    public void The_parameters_are_read_from_the_stored_value_not_the_constants()
    {
        // Rewrite a real hash's parameters to cheaper ones and recompute: if Verify used the
        // constants it would compute with m=19456 and refuse. Built by hand here, so the test
        // does not depend on a Hash overload that does not exist.
        var cheap = Hash("4821", memoryKib: 8_192, iterations: 1, out var salt);
        var stored = $"$argon2id$v=19$m=8192,t=1,p=1${Convert.ToBase64String(salt).TrimEnd('=')}${Convert.ToBase64String(cheap).TrimEnd('=')}";

        Assert.True(Hasher.Verify("4821", stored));
        Assert.False(Hasher.Verify("4822", stored));
    }

    [Fact]
    public void The_synthetic_sentinel_never_verifies()
    {
        // D-077: refused because it is the sentinel, before any hashing.
        Assert.False(Hasher.Verify("1234", StaffPin.NeverUsable));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$bm90LWJhc2U2NA")]
    [InlineData("$argon2id$v=19$m=abc,t=2,p=1$c2FsdHNhbHQ$aGFzaGhhc2g")]
    [InlineData("$argon2i$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$bcrypt$something")]
    public void A_malformed_or_foreign_stored_value_is_false_never_an_exception(string? stored)
    {
        Assert.False(Hasher.Verify("1234", stored));
    }

    [Fact]
    public void A_pin_that_is_not_well_formed_never_verifies()
    {
        var stored = Hasher.Hash("1234");

        Assert.False(Hasher.Verify("١٢٣٤", stored));
        Assert.False(Hasher.Verify(" 1234", stored));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("abcd")]
    [InlineData("١٢٣٤")]
    public void A_pin_that_is_not_well_formed_cannot_be_set(string pin)
    {
        Assert.Throws<ArgumentException>(() => Hasher.Hash(pin));
    }

    [Fact]
    public void A_tampered_hash_does_not_verify()
    {
        var stored = Hasher.Hash("4821");
        var parts = stored.Split('$');
        var bytes = Convert.FromBase64String(Pad(parts[5]));
        bytes[0] ^= 0x01;
        parts[5] = Convert.ToBase64String(bytes).TrimEnd('=');

        Assert.False(Hasher.Verify("4821", string.Join('$', parts)));
    }

    // ------------------------------------------------------------ helpers

    private static byte[] Hash(string pin, int memoryKib, int iterations, out byte[] salt)
    {
        salt = [.. Enumerable.Range(1, 16).Select(i => (byte)i)];
        using var argon = new Konscious.Security.Cryptography.Argon2id(System.Text.Encoding.UTF8.GetBytes(pin))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = 1,
        };
        return argon.GetBytes(32);
    }

    private static string Pad(string base64) => base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
}
