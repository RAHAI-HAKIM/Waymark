namespace Waymark.Domain.Privacy;

/// <summary>
/// The key that encrypts <c>waymark-store.db</c> at rest (D-042, F-1).
///
/// <para>
/// A port, so Persistence can key a connection without knowing where the key comes from:
/// the production implementation unwraps a DPAPI blob from the keys directory, which is a
/// Windows API and has no place in Domain or Persistence. Tests supply a fixed key.
/// </para>
/// <para>
/// <b>What it protects is availability.</b> Losing the key loses the store's history, and
/// unlike the tenant key it may be rotated, by <c>PRAGMA rekey</c> from an admin command.
/// </para>
/// </summary>
public interface IDatabaseKeyProvider
{
    /// <summary>The number of bytes in a database key.</summary>
    public const int KeyBytes = 32;

    /// <summary>
    /// A fresh copy of the 32-byte key. The caller zeroes it once used, so a copy does not
    /// outlive the statement that needed it.
    /// </summary>
    byte[] GetKey();
}
