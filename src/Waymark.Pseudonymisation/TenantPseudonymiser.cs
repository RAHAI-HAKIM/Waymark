using System.Security.Cryptography;
using Waymark.Domain.Privacy;

namespace Waymark.Pseudonymisation;

/// <summary>
/// Holds the tenant key and computes pseudonyms from it. The only object in the
/// system that can do either (CLAUDE.md §3.5).
///
/// <para>
/// <b>Nothing here returns key material, and nothing ever should.</b> The key
/// enters through an internal constructor and leaves only as HMAC output.
/// <c>TenantKeyNeverLeavesTests</c> fails if a public member of this type starts
/// handing out bytes, which is the same device that keeps a string overload off
/// <c>IProcessingLog</c> and a public constructor off <c>Pseudonym</c>: the
/// compiler is a better reviewer than review.
/// </para>
/// <para>
/// Register it as a singleton. It is immutable, the HMAC is a pure function of
/// the key and the message, and <see cref="HMACSHA256.HashData(byte[], byte[])"/>
/// keeps no state between calls — so it is safe on the several threads a till
/// serves.
/// </para>
/// </summary>
public sealed class TenantPseudonymiser : IPseudonymiser, ITenantKeyCheck, IDisposable
{
    private readonly byte[] _tenantKey;
    private bool _disposed;

    /// <summary>
    /// Takes ownership of <paramref name="tenantKey"/>. Internal, so the only
    /// route to one of these is <see cref="TenantKeyStore"/> — which means the
    /// only route is through a key file on this machine.
    /// </summary>
    internal TenantPseudonymiser(byte[] tenantKey)
    {
        if (tenantKey.Length != TenantKeyStore.KeyBytes)
        {
            throw new CryptographicException(
                $"A tenant key is {TenantKeyStore.KeyBytes} bytes; got {tenantKey.Length}.");
        }

        _tenantKey = tenantKey;
        CheckValue = PseudonymScheme.ComputeKeyCheck(_tenantKey);
    }

    /// <inheritdoc />
    public string CheckValue { get; }

    /// <inheritdoc />
    public Pseudonym PseudonymFor(SubjectDomain domain, string subjectId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The one place in the system where a Pseudonym is constructed. The
        // constructor is internal to Domain and this is the only project Domain
        // grants InternalsVisibleTo, so a call site wanting to name a subject in
        // processing_log has to come through here (D-045, D-048).
        return new Pseudonym(PseudonymScheme.Compute(_tenantKey, domain, subjectId));
    }

    /// <summary>
    /// Clears the key from memory.
    ///
    /// <para>
    /// Worth doing and worth not overstating: it shortens the window in which a
    /// crash dump or a page file contains the key, and it does nothing about a
    /// copy the garbage collector already moved. It is not what protects the key
    /// — the file ACL and DPAPI are (D-042).
    /// </para>
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_tenantKey);
        _disposed = true;
    }
}
