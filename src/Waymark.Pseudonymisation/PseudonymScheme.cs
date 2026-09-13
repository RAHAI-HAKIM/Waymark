using System.Security.Cryptography;
using System.Text;
using Waymark.Domain.Privacy;

namespace Waymark.Pseudonymisation;

/// <summary>
/// The scheme itself, and the only place it is written down in code
/// (decisions.md D-039):
///
/// <code>
/// pseudonym = base32( HMAC-SHA256(tenant_key, prefix ‖ subject_id)[0..16] )
/// </code>
///
/// <para>
/// <b>Every part of that line is load-bearing.</b> HMAC rather than a plain
/// hash, because a plain hash of a phone number is reversed by enumerating phone
/// numbers — there are only so many, and the whole scheme would be theatre. The
/// prefix, because without it a staff id equal to a customer id produces one
/// pseudonym for two people. Truncation to 128 bits, so a pseudonym is 26
/// characters like a ULID; the loss is a collision probability around 2⁻⁶⁴ at a
/// store's scale, which is nothing against the benefit of uniform columns.
/// </para>
/// <para>
/// <b>The scheme does not rotate.</b> A leaked key re-identifies every customer
/// of that tenant, retroactively and permanently, and nothing undoes it (D-039,
/// accepted cost 3). The <c>v1</c> in each prefix is the only escape hatch:
/// changing it begins a new epoch in which no new pseudonym matches any old one,
/// which is a decision about the whole tenant's history and not a code change.
/// </para>
/// </summary>
internal static class PseudonymScheme
{
    /// <summary>Bytes of HMAC output kept. 128 bits, as D-039 specifies.</summary>
    internal const int TruncatedBytes = 16;

    /// <summary>
    /// The fixed string the key-check value is computed over (D-042).
    ///
    /// <para>
    /// It has no trailing colon and takes no subject, so it cannot collide with
    /// any subject prefix however strange a subject id is: the two diverge at
    /// the character after <c>waymark:</c>.
    /// </para>
    /// </summary>
    private const string KeyCheckMessage = "waymark:keycheck:v1";

    /// <summary>
    /// The domain-separation prefix for a population.
    ///
    /// <para>
    /// <b>It throws on a value it does not know</b> rather than falling back to
    /// the customer prefix. A default here would mean a new
    /// <see cref="SubjectDomain"/> member silently sharing the customer
    /// namespace — a collision between two populations, which is the one thing
    /// the prefix exists to prevent, arriving without a symptom.
    /// </para>
    /// </summary>
    internal static string PrefixFor(SubjectDomain domain) => domain switch
    {
        SubjectDomain.Customer => "waymark:customer:v1:",
        SubjectDomain.Staff => "waymark:staff:v1:",
        SubjectDomain.Supplier => "waymark:supplier:v1:",
        _ => throw new ArgumentOutOfRangeException(
            nameof(domain),
            domain,
            "No domain-separation prefix is defined for this subject domain. Add one to "
            + "PseudonymScheme rather than letting it share another population's namespace "
            + "(decisions.md D-039, condition 3)."),
    };

    /// <summary>Computes the pseudonym text for one subject.</summary>
    internal static string Compute(ReadOnlySpan<byte> tenantKey, SubjectDomain domain, string subjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        return Derive(tenantKey, PrefixFor(domain) + subjectId);
    }

    /// <summary>Computes the key-check value (D-042).</summary>
    internal static string ComputeKeyCheck(ReadOnlySpan<byte> tenantKey) =>
        Derive(tenantKey, KeyCheckMessage);

    private static string Derive(ReadOnlySpan<byte> tenantKey, string message)
    {
        Span<byte> mac = stackalloc byte[32];
        var written = HMACSHA256.HashData(tenantKey, Encoding.UTF8.GetBytes(message), mac);

        // HMAC-SHA256 is 32 bytes by definition; if this ever disagrees the
        // truncation below would quietly encode uninitialised stack.
        if (written != mac.Length)
        {
            throw new CryptographicException(
                $"HMAC-SHA256 produced {written} bytes rather than {mac.Length}.");
        }

        return CrockfordBase32.Encode(mac[..TruncatedBytes]);
    }
}
