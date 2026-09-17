using System.Security.Cryptography;

namespace Waymark.Pseudonymisation;

/// <summary>
/// A 32-byte key at rest in the keys directory, wrapped by an <see cref="IKeyProtector"/>. What
/// the tenant key and the database key have in common: generated here, written so the file is
/// absent or complete, and never read back without its wrapping (D-042).
/// </summary>
internal static class WrappedKeyFile
{
    /// <summary>32 bytes, from <see cref="RandomNumberGenerator"/>.</summary>
    public const int KeyBytes = 32;

    /// <summary>
    /// Suffix of the file a new key is written to before it is moved into place. One left behind
    /// by an interrupted creation is never read.
    /// </summary>
    public const string TemporarySuffix = ".tmp";

    /// <summary>Generates a key, writes it wrapped to <paramref name="path"/>, and returns it.</summary>
    /// <exception cref="IOException">The file appeared meanwhile: another process won the race.</exception>
    public static byte[] Create(string path, byte[] entropy, IKeyProtector protector)
    {
        var key = RandomNumberGenerator.GetBytes(KeyBytes);
        var wrapped = protector.Protect(key, entropy);

        // Written beside the target and moved into place, so the key file is either absent or
        // complete. Writing it in place would let a crash mid-write leave a truncated blob behind,
        // one whose unwrap error says, correctly for a real key, never to delete it. An
        // interrupted temp file is garbage nobody reads; a truncated key file is a gap.
        var temporary = Path.Combine(
            Path.GetDirectoryName(path)!,
            $"{Path.GetFileName(path)}.{Guid.NewGuid():N}{TemporarySuffix}");

        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(wrapped);
                file.Flush(flushToDisk: true);
            }

            // overwrite: false. Two processes racing at first start would otherwise both generate
            // a key and the second would replace the first, silently orphaning whatever the first
            // had already protected. Losing the race is an error, not a retry.
            File.Move(temporary, path, overwrite: false);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return key;
    }

    /// <summary>Reads and unwraps <paramref name="path"/>, explaining a failure with <paramref name="failure"/>.</summary>
    public static byte[] Unwrap(string path, byte[] entropy, IKeyProtector protector, string failure)
    {
        var wrapped = File.ReadAllBytes(path);

        try
        {
            var key = protector.Unprotect(wrapped, entropy);
            if (key.Length != KeyBytes)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new CryptographicException($"The key unwrapped to {key.Length} bytes, not {KeyBytes}.");
            }

            return key;
        }
        catch (CryptographicException error)
        {
            // Say what this means, because the consequence of guessing wrong and "fixing" it by
            // deleting the file is unrecoverable.
            throw new CryptographicException(failure, error);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrapped);
        }
    }
}
