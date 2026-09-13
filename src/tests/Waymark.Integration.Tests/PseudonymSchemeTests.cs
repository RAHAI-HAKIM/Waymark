using System.Security.Cryptography;
using System.Text;
using Waymark.Domain.Privacy;
using Waymark.Pseudonymisation;

namespace Waymark.Integration.Tests;

/// <summary>
/// The pseudonym scheme (decisions.md D-039):
/// <c>base32( HMAC-SHA256(tenant_key, prefix ‖ subject_id)[0..16] )</c>.
///
/// <para>
/// These run against the real implementation with a fixed key, so the values are
/// reproducible. A scheme test that only asserts "two calls agree" would pass on
/// a constant.
/// </para>
/// </summary>
public sealed class PseudonymSchemeTests : IClassFixture<TenantKeyFixture>
{
    private readonly TenantKeyFixture _keys;

    public PseudonymSchemeTests(TenantKeyFixture keys) => _keys = keys;

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    // ------------------------------------------------------------- the shape

    [Fact]
    public void A_pseudonym_is_26_characters_of_crockford_base32()
    {
        // Same shape as a ULID, so the cloud's columns stay uniform (D-039).
        var pseudonym = _keys.Pseudonymiser.PseudonymFor(SubjectDomain.Customer, "cust-1").Value;

        Assert.Equal(26, pseudonym.Length);
        Assert.All(
            pseudonym,
            character => Assert.True(
                Alphabet.Contains(character, StringComparison.Ordinal),
                $"'{character}' is not in Crockford's alphabet."));
    }

    [Fact]
    public void The_alphabet_excludes_the_characters_that_are_misread()
    {
        // I, L and O are misread as 1, 1 and 0; U is excluded so the encoding
        // cannot spell an obscenity. This matters because a pseudonym is read
        // aloud and typed by hand during a data-subject request, where a
        // transcription error is indistinguishable from "no such subject".
        Assert.DoesNotContain('I', Alphabet);
        Assert.DoesNotContain('L', Alphabet);
        Assert.DoesNotContain('O', Alphabet);
        Assert.DoesNotContain('U', Alphabet);
        Assert.Equal(32, Alphabet.Length);
    }

    [Fact]
    public void The_leading_character_never_exceeds_seven()
    {
        // 26 x 5 = 130 bits of room for a 128-bit value, so the leading
        // character holds only 3 significant bits and never exceeds 7. This is
        // the same bound ULID has, for the same reason — its maximum value is
        // 7ZZZZZZZZZZZZZZZZZZZZZZZZZ. A first character of 8 or more would mean
        // the encoder had shifted bits off the far end.
        var leading = new HashSet<int>();

        foreach (var index in Enumerable.Range(0, 400))
        {
            var pseudonym = _keys.Pseudonymiser
                .PseudonymFor(SubjectDomain.Customer, $"cust-{index}").Value;

            var value = Alphabet.IndexOf(pseudonym[0], StringComparison.Ordinal);
            Assert.InRange(value, 0, 7);
            leading.Add(value);
        }

        // And all eight do occur, so the bound is the encoding's rather than an
        // accident of a narrow sample.
        Assert.Equal(8, leading.Count);
    }

    // ------------------------------------------------------ the derivation

    [Fact]
    public void The_pseudonym_is_exactly_the_documented_construction()
    {
        // Recomputed here from the primitives rather than compared to a stored
        // constant, so this test says what the scheme *is* and fails if the
        // implementation quietly changes any part of it.
        const string subjectId = "01CUSTOMERABCDEFGHJKMNPQRS";

        var expected = HMACSHA256.HashData(
            TenantKeyFixture.KeyBytes,
            Encoding.UTF8.GetBytes("waymark:customer:v1:" + subjectId));

        // The HMAC's first 16 bytes, read big-endian as one 128-bit number.
        var value = new System.Numerics.BigInteger(
            expected.AsSpan(0, 16), isUnsigned: true, isBigEndian: true);

        var characters = new char[26];
        for (var index = 25; index >= 0; index--)
        {
            characters[index] = Alphabet[(int)(value & 31)];
            value >>= 5;
        }

        Assert.Equal(
            new string(characters),
            _keys.Pseudonymiser.PseudonymFor(SubjectDomain.Customer, subjectId).Value);
    }

    [Fact]
    public void The_same_subject_under_the_same_key_always_gives_the_same_pseudonym()
    {
        // Determinism is what lets the cloud accumulate a history, and what makes
        // reverse lookup possible without a mapping table (D-039).
        var first = _keys.Pseudonymiser.PseudonymFor(SubjectDomain.Customer, "cust-stable");
        var second = _keys.Pseudonymiser.PseudonymFor(SubjectDomain.Customer, "cust-stable");

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_different_key_gives_a_different_pseudonym_for_the_same_subject()
    {
        // The key is the "additional information required to re-identify"
        // (DPIA §5.2). If the pseudonym did not depend on it, there would be
        // nothing to withhold.
        using var other = TenantKeyFixture.PseudonymiserFor(RandomNumberGenerator.GetBytes(32));

        Assert.NotEqual(
            _keys.Pseudonymiser.PseudonymFor(SubjectDomain.Customer, "cust-1"),
            other.PseudonymFor(SubjectDomain.Customer, "cust-1"));
    }

    // --------------------------------------------------- domain separation

    [Fact]
    public void The_same_id_in_two_populations_gives_two_pseudonyms()
    {
        // D-039 condition 3, and the reason it cannot be added later. Without the
        // prefix, a staff member whose id equalled a customer's would share that
        // customer's pseudonym and the cloud would merge two people — silently,
        // and for as long as the tenant exists.
        const string collidingId = "01SAMEIDINBOTHPOPULATIONS0";

        var asCustomer = _keys.Pseudonymiser.PseudonymFor(SubjectDomain.Customer, collidingId);
        var asStaff = _keys.Pseudonymiser.PseudonymFor(SubjectDomain.Staff, collidingId);
        var asSupplier = _keys.Pseudonymiser.PseudonymFor(SubjectDomain.Supplier, collidingId);

        Assert.Equal(3, new HashSet<Pseudonym> { asCustomer, asStaff, asSupplier }.Count);
    }

    [Fact]
    public void An_unknown_population_is_refused_rather_than_defaulted()
    {
        // A default would put a new SubjectDomain member into the customer
        // namespace — the exact collision the prefix exists to prevent, arriving
        // with no symptom.
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _keys.Pseudonymiser.PseudonymFor((SubjectDomain)99, "cust-1"));

        Assert.Contains("domain-separation prefix", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_declared_population_has_a_prefix()
    {
        // Adding a member to SubjectDomain without a prefix would compile and
        // then throw at the first real subject, which is the worst place to find
        // out.
        foreach (var domain in Enum.GetValues<SubjectDomain>())
        {
            var pseudonym = _keys.Pseudonymiser.PseudonymFor(domain, "subject-1");
            Assert.Equal(26, pseudonym.Value.Length);
        }
    }

    // --------------------------------------------------------- the refusals

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_subject_id_is_refused(string subjectId)
    {
        // An empty id would hash to a perfectly well-formed pseudonym that every
        // subjectless call site shares — one identity for everybody nobody named.
        Assert.Throws<ArgumentException>(() =>
            _keys.Pseudonymiser.PseudonymFor(SubjectDomain.Customer, subjectId));
    }

    [Fact]
    public void A_null_subject_id_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _keys.Pseudonymiser.PseudonymFor(SubjectDomain.Customer, null!));
    }

    // ------------------------------------------------------ the check value

    [Fact]
    public void The_key_check_value_identifies_the_key_and_not_a_subject()
    {
        // D-042: a restore with the wrong key otherwise accumulates two
        // identities per customer, silently. This is what StoreServer compares.
        using var other = TenantKeyFixture.PseudonymiserFor(RandomNumberGenerator.GetBytes(32));

        Assert.Equal(26, _keys.Pseudonymiser.CheckValue.Length);
        Assert.NotEqual(_keys.Pseudonymiser.CheckValue, other.CheckValue);

        // Stable across instances holding the same key.
        using var sameKey = TenantKeyFixture.PseudonymiserFor(TenantKeyFixture.KeyBytes);
        Assert.Equal(_keys.Pseudonymiser.CheckValue, sameKey.CheckValue);
    }

    [Fact]
    public void The_check_value_cannot_collide_with_any_subjects_pseudonym()
    {
        // The check value is published to the cloud tenant record. If a subject
        // id existed that produced the same input to the HMAC, that customer's
        // pseudonym would be sitting in a field labelled "key check".
        // "waymark:keycheck:v1" and "waymark:customer:v1:…" diverge at the
        // character after "waymark:", so no subject id can reach it.
        string[] attempts =
        [
            "", "v1", ":v1", "keycheck:v1", "waymark:keycheck:v1",
        ];

        foreach (var attempt in attempts.Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            foreach (var domain in Enum.GetValues<SubjectDomain>())
            {
                Assert.NotEqual(
                    _keys.Pseudonymiser.CheckValue,
                    _keys.Pseudonymiser.PseudonymFor(domain, attempt).Value);
            }
        }
    }
}
