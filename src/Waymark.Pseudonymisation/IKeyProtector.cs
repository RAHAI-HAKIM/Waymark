using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Waymark.Pseudonymisation;

/// <summary>
/// Wraps and unwraps a secret at rest, so a copy of the file is not a copy of
/// the key.
///
/// <para>
/// An interface rather than a direct DPAPI call because DPAPI is Windows-only
/// and machine-bound, and a suite that can only run on the machine that created
/// the key is a suite nobody runs. It holds no key of its own: it protects
/// whatever it is handed, so possessing one grants nothing.
/// </para>
/// <para>
/// <b>What this defends is narrower than it looks.</b> `LocalMachine` DPAPI is
/// recoverable offline from a disk image plus the SYSTEM and SECURITY hives, so
/// a stolen powered-on till is a compromise and this does not change that; the
/// control that would is full-disk encryption, unavailable on the Windows Home
/// machines Waymark deploys to, and the DPIA states it as an accepted risk
/// rather than papering over it. What it does defend is the case that actually
/// happens — files copied off by a repair technician, a USB grab of
/// <c>ProgramData</c>, or the local backup staging folder (D-042).
/// </para>
/// </summary>
public interface IKeyProtector
{
    /// <summary>Wraps <paramref name="secret"/>, bound to <paramref name="entropy"/>.</summary>
    byte[] Protect(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> entropy);

    /// <summary>
    /// Unwraps a blob produced by <see cref="Protect"/> under the same entropy.
    /// </summary>
    /// <exception cref="CryptographicException">
    /// The blob was not produced on this machine, or the entropy differs.
    /// </exception>
    byte[] Unprotect(ReadOnlySpan<byte> wrapped, ReadOnlySpan<byte> entropy);
}

/// <summary>
/// The production wrapper: Windows DPAPI at <c>LocalMachine</c> scope (D-042).
///
/// <para>
/// Machine scope rather than user scope because StoreServer runs as a service
/// and several cashiers share the till; a user-scoped blob would be unreadable
/// by the account that needs it, and the failure would arrive at the first sale
/// rather than at install.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiKeyProtector : IKeyProtector
{
    public byte[] Protect(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> entropy) =>
        ProtectedData.Protect(secret.ToArray(), entropy.ToArray(), DataProtectionScope.LocalMachine);

    public byte[] Unprotect(ReadOnlySpan<byte> wrapped, ReadOnlySpan<byte> entropy) =>
        ProtectedData.Unprotect(wrapped.ToArray(), entropy.ToArray(), DataProtectionScope.LocalMachine);
}
