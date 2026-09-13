using System.Security.Cryptography;
using Waymark.Pseudonymisation;

namespace Waymark.Integration.Tests;

/// <summary>
/// A tenant key with known bytes, so pseudonyms are reproducible across runs.
///
/// <para>
/// <c>TenantPseudonymiser</c>'s constructor is internal, and deliberately: the
/// only route to one is a key file on this machine. So a test cannot simply hand
/// it 32 bytes — it installs a key file and unwraps it through a substituted
/// <see cref="IKeyProtector"/>. That seam is the reason the protector is an
/// interface, and it keeps <c>Waymark.Pseudonymisation</c> free of
/// <c>InternalsVisibleTo</c> grants, which would be a strange thing for the
/// project holding the tenant key to hand out.
/// </para>
/// </summary>
public sealed class TenantKeyFixture : IDisposable
{
    /// <summary>
    /// A fixed key. Test material only: it is in a public repository, which is
    /// exactly why no production path can reach it.
    /// </summary>
    internal static readonly byte[] KeyBytes =
    [
        0x57, 0x61, 0x79, 0x6D, 0x61, 0x72, 0x6B, 0x54,
        0x65, 0x73, 0x74, 0x54, 0x65, 0x6E, 0x61, 0x6E,
        0x74, 0x4B, 0x65, 0x79, 0x2D, 0x44, 0x30, 0x33,
        0x39, 0x2F, 0x44, 0x30, 0x34, 0x32, 0x21, 0x00,
    ];

    /// <summary>The install id every fixture-built key is bound to.</summary>
    internal const string InstallId = "00000000-0000-0000-0000-00000000w7w7";

    private readonly List<string> _directories = [];
    private readonly List<TenantPseudonymiser> _open = [];

    public TenantKeyFixture() => Pseudonymiser = Build(KeyBytes, InstallId);

    /// <summary>Holds <see cref="KeyBytes"/>.</summary>
    public TenantPseudonymiser Pseudonymiser { get; }

    /// <summary>A pseudonymiser over an arbitrary key. Disposed with the fixture.</summary>
    internal static TenantPseudonymiser PseudonymiserFor(byte[] key) =>
        BuildIn(NewDirectory(out _), key, InstallId);

    /// <summary>Where this fixture's own key file lives.</summary>
    public string KeysDirectory { get; private set; } = string.Empty;

    private TenantPseudonymiser Build(byte[] key, string installId)
    {
        var directory = NewDirectory(out var path);
        _directories.Add(path);
        KeysDirectory = path;

        var pseudonymiser = BuildIn(directory, key, installId);
        _open.Add(pseudonymiser);
        return pseudonymiser;
    }

    private static string NewDirectory(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), "waymark-keys", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static TenantPseudonymiser BuildIn(string directory, byte[] key, string installId)
    {
        var protector = new EntropyBindingProtector();

        // Install the key by writing what the protector would have written, then
        // letting the store load it the way production does.
        File.WriteAllBytes(
            Path.Combine(directory, TenantKeyStore.FileName),
            protector.Protect(key, EntropyBindingProtector.EntropyFor(installId)));

        return TenantKeyStore.OpenOrCreate(directory, installId, protector);
    }

    public void Dispose()
    {
        foreach (var pseudonymiser in _open)
        {
            pseudonymiser.Dispose();
        }

        foreach (var directory in _directories.Where(Directory.Exists))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A locked file on a build agent must not fail the run.
            }
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// A stand-in for DPAPI that keeps the one property the tests need to exercise:
/// a blob unwraps only under the entropy it was wrapped with.
///
/// <para>
/// It is not encryption and does not pretend to be — the real wrapping is tested
/// against real DPAPI in <c>TenantKeyStoreTests</c>. What it substitutes for is
/// DPAPI's machine binding, which would otherwise make every test in this suite
/// unrepeatable on another machine.
/// </para>
/// </summary>
public sealed class EntropyBindingProtector : IKeyProtector
{
    internal static byte[] EntropyFor(string installId) =>
        System.Text.Encoding.UTF8.GetBytes("waymark:tenant-key:v1:" + installId);

    public byte[] Protect(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> entropy)
    {
        var tag = SHA256.HashData(entropy);
        return [.. tag, .. secret];
    }

    public byte[] Unprotect(ReadOnlySpan<byte> wrapped, ReadOnlySpan<byte> entropy)
    {
        var tag = SHA256.HashData(entropy);

        if (wrapped.Length < tag.Length
            || !CryptographicOperations.FixedTimeEquals(wrapped[..tag.Length], tag))
        {
            throw new CryptographicException("Wrapped under different entropy.");
        }

        return wrapped[tag.Length..].ToArray();
    }
}
