using System.Security.Cryptography;
using System.Text;

namespace Waymark.Pseudonymisation;

/// <summary>
/// Where the tenant key comes from: a wrapped blob in the keys directory, made
/// once and never replaced (decisions.md D-039, D-042).
///
/// <para>
/// <b>The key is generated on the retailer's machine and never issued by
/// Waymark's cloud.</b> That is not an implementation detail — if the cloud
/// issued it, Waymark would hold both the identifiers in a backup and the
/// pseudonyms in the cloud, which is the single circumstance the whole design
/// exists to prevent (DPIA §5.2).
/// </para>
/// </summary>
public static class TenantKeyStore
{
    /// <summary>32 bytes, from <see cref="RandomNumberGenerator"/> (D-042).</summary>
    public const int KeyBytes = 32;

    /// <summary>The wrapped blob's file name, inside the keys directory.</summary>
    public const string FileName = "tenant.key";

    /// <summary>
    /// Domain separation for the DPAPI entropy.
    ///
    /// <para>
    /// The database key is wrapped under its own prefix. Without that, the two
    /// blobs would be interchangeable: swapping the files — by a restore script,
    /// a backup that caught the wrong directory, or a mistake at a keyboard —
    /// would unwrap cleanly and Waymark would start pseudonymising under the
    /// database key. Every customer would get a new identity and nothing would
    /// report an error.
    /// </para>
    /// </summary>
    private const string EntropyPrefix = "waymark:tenant-key:v1:";

    /// <summary>
    /// Loads the tenant key, creating one on first run.
    ///
    /// <para>
    /// <b>An existing key is never replaced.</b> There is no rotate, no
    /// overwrite and no force flag anywhere in this class, because the scheme
    /// does not rotate: replacing the key would give every customer a second
    /// pseudonymous identity and orphan the entire cloud history for that tenant,
    /// irreversibly and with no error (D-039, accepted cost 3). Beginning a new
    /// epoch deliberately is a decision about a tenant's whole history, and it
    /// does not belong behind a method call.
    /// </para>
    /// </summary>
    /// <param name="keysDirectory">
    /// <c>%ProgramData%\Waymark\keys</c> in production. Its own directory
    /// because the cloud backup set is an allowlist of directories, so a key
    /// cannot be swept into a backup by someone adding a pattern (D-042).
    /// </param>
    /// <param name="installId">
    /// Binds the wrapping to this installation, as the DPAPI optional-entropy
    /// parameter.
    /// </param>
    /// <param name="protector">How the blob is wrapped. <see cref="DpapiKeyProtector"/> in production.</param>
    public static TenantPseudonymiser OpenOrCreate(
        string keysDirectory,
        string installId,
        IKeyProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keysDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(installId);
        ArgumentNullException.ThrowIfNull(protector);

        Directory.CreateDirectory(keysDirectory);
        var path = Path.Combine(keysDirectory, FileName);
        var entropy = Encoding.UTF8.GetBytes(EntropyPrefix + installId);

        return File.Exists(path)
            ? new TenantPseudonymiser(Unwrap(path, entropy, protector))
            : new TenantPseudonymiser(Create(path, entropy, protector));
    }

    /// <summary>
    /// Whether this installation already has a tenant key. For an installer
    /// deciding whether to show the recovery-code ceremony, and for a restore
    /// checking it is not about to start a second epoch.
    /// </summary>
    public static bool Exists(string keysDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keysDirectory);
        return File.Exists(Path.Combine(keysDirectory, FileName));
    }

    private static byte[] Create(string path, byte[] entropy, IKeyProtector protector)
    {
        var key = RandomNumberGenerator.GetBytes(KeyBytes);
        var wrapped = protector.Protect(key, entropy);

        // CreateNew, not Create or WriteAllBytes: two processes racing at first
        // start would otherwise both generate a key and the second would
        // overwrite the first, silently orphaning anything already pseudonymised
        // under it. Losing the race is an error, not a retry.
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            file.Write(wrapped);
            file.Flush(flushToDisk: true);
        }

        return key;
    }

    private static byte[] Unwrap(string path, byte[] entropy, IKeyProtector protector)
    {
        var wrapped = File.ReadAllBytes(path);

        try
        {
            return protector.Unprotect(wrapped, entropy);
        }
        catch (CryptographicException error)
        {
            // Say what this means, because the consequence of guessing wrong and
            // "fixing" it by deleting the file is unrecoverable.
            throw new CryptographicException(
                $"""
                The tenant key at {path} could not be unwrapped.

                It was wrapped on a different machine, or under a different
                install id. This is what a restored image or a transplanted
                ProgramData directory looks like.

                Do not delete this file to make the error go away. It is the only
                thing that links this store's customers to their history in the
                cloud, it cannot be regenerated, and a new one starts a new
                pseudonym epoch for the whole tenant (decisions.md D-039). Restore
                it from the printed recovery code instead (D-042).
                """,
                error);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrapped);
        }
    }
}
