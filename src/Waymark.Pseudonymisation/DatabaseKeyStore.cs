using System.Security.Cryptography;
using System.Text;
using Waymark.Domain.Privacy;

namespace Waymark.Pseudonymisation;

/// <summary>
/// The key that encrypts <c>waymark-store.db</c> (D-042, F-1): a wrapped blob,
/// <c>keys\store.key</c>, created on the first run before the database is.
///
/// <para>
/// It lives beside the tenant key because it is custody of the same kind, with the same
/// wrapping, but it protects something else. The database key protects <b>availability</b>:
/// losing it loses the store's history, and it may be rotated with <c>PRAGMA rekey</c>. The
/// tenant key protects <b>confidentiality</b> and never rotates. The two blobs are wrapped under
/// different entropy, so one never unwraps as the other.
/// </para>
/// <para>
/// Held in memory for the life of the process, like the tenant key, and zeroed on dispose:
/// every connection needs it, and unwrapping per connection would put DPAPI on every query.
/// </para>
/// </summary>
public sealed class DatabaseKeyStore : IDatabaseKeyProvider, IDisposable
{
    /// <summary>The wrapped blob's file name, inside the keys directory.</summary>
    public const string FileName = "store.key";

    /// <summary>
    /// Domain separation for the DPAPI entropy: a fixed constant, not a secret (O-18). It stops a
    /// tenant-key blob being unwrapped as the database key, or the reverse.
    /// </summary>
    internal static readonly byte[] Entropy = Encoding.UTF8.GetBytes("waymark:database-key:v1");

    private readonly byte[] _key;
    private bool _disposed;

    private DatabaseKeyStore(byte[] key) => _key = key;

    /// <summary>
    /// Loads the key, creating it if this installation has none. Only for a store whose database
    /// does not exist yet: a new key cannot open an existing file (<see cref="Open"/>).
    /// </summary>
    public static DatabaseKeyStore OpenOrCreate(string keysDirectory, IKeyProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keysDirectory);
        ArgumentNullException.ThrowIfNull(protector);

        // The host has already created it with its ACL; this only covers a caller that has not.
        Directory.CreateDirectory(keysDirectory);
        var path = Path.Combine(keysDirectory, FileName);
        return File.Exists(path)
            ? Open(keysDirectory, protector)
            : new DatabaseKeyStore(WrappedKeyFile.Create(path, Entropy, protector));
    }

    /// <summary>
    /// Loads the key of an existing database. Never creates one: a missing key file beside an
    /// existing database is a restore gone wrong, and a fresh key would only hide it.
    /// </summary>
    /// <exception cref="FileNotFoundException">There is no key file.</exception>
    /// <exception cref="CryptographicException">The blob does not unwrap on this machine.</exception>
    public static DatabaseKeyStore Open(string keysDirectory, IKeyProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keysDirectory);
        ArgumentNullException.ThrowIfNull(protector);

        var path = Path.Combine(keysDirectory, FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"""
                The store database exists but its key, {path}, does not.

                The database cannot be read without it. Restore the key from the printed
                recovery code (D-042); do not create a new one, which cannot open this file.
                """,
                path);
        }

        return new DatabaseKeyStore(WrappedKeyFile.Unwrap(
            path,
            Entropy,
            protector,
            $"""
            The database key at {path} could not be unwrapped.

            It was wrapped on a different machine, or the file is not the database key. This is
            what a restored image or a transplanted ProgramData directory looks like.

            Do not delete this file to make the error go away: without it the store's history
            cannot be read. Restore it from the printed recovery code instead (D-042).
            """));
    }

    /// <summary>Whether this installation already has a database key.</summary>
    public static bool Exists(string keysDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keysDirectory);
        return File.Exists(Path.Combine(keysDirectory, FileName));
    }

    /// <inheritdoc />
    public byte[] GetKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (byte[])_key.Clone();
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
        _disposed = true;
    }
}
