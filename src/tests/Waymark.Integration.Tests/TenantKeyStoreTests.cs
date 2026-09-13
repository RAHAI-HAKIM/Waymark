using System.Security.Cryptography;
using Waymark.Domain.Privacy;
using Waymark.Pseudonymisation;

namespace Waymark.Integration.Tests;

/// <summary>
/// Key custody (decisions.md D-042): 32 random bytes, wrapped at machine scope
/// with entropy bound to the install, in their own directory, created once and
/// never replaced.
/// </summary>
public sealed class TenantKeyStoreTests : IDisposable
{
    private readonly List<string> _directories = [];

    private string NewKeysDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(), "waymark-keystore", Guid.NewGuid().ToString("N"));
        _directories.Add(path);
        return path;
    }

    private const string InstallId = "install-w7";

    // ------------------------------------------------------------- creation

    [Fact]
    public void A_first_run_creates_a_key_and_the_directory_that_holds_it()
    {
        var keys = NewKeysDirectory();

        Assert.False(TenantKeyStore.Exists(keys));

        using var pseudonymiser = TenantKeyStore.OpenOrCreate(
            keys, InstallId, new EntropyBindingProtector());

        Assert.True(TenantKeyStore.Exists(keys));
        Assert.True(File.Exists(Path.Combine(keys, TenantKeyStore.FileName)));
        Assert.Equal(26, pseudonymiser.CheckValue.Length);
    }

    [Fact]
    public void A_generated_key_is_32_bytes_and_not_a_constant()
    {
        // 32 bytes from RandomNumberGenerator (D-042). Two installs must not
        // share a key: the whole scheme is per tenant, and a shared key would
        // let one retailer compute another's pseudonyms.
        var protector = new EntropyBindingProtector();
        var blobs = new List<string>();

        foreach (var _ in Enumerable.Range(0, 8))
        {
            var keys = NewKeysDirectory();
            using var pseudonymiser = TenantKeyStore.OpenOrCreate(keys, InstallId, protector);

            // The fake protector prefixes a 32-byte tag, so what follows is the
            // key itself — which is why this assertion can see its length.
            var wrapped = File.ReadAllBytes(Path.Combine(keys, TenantKeyStore.FileName));
            Assert.Equal(32 + TenantKeyStore.KeyBytes, wrapped.Length);

            blobs.Add(pseudonymiser.CheckValue);
        }

        Assert.Equal(8, blobs.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Opening_twice_returns_the_same_key_rather_than_making_a_second()
    {
        // The failure this rules out is the worst one available here: a second
        // key would give every customer a new pseudonym and orphan the entire
        // cloud history for the tenant, with no error anywhere (D-039).
        var keys = NewKeysDirectory();
        var protector = new EntropyBindingProtector();

        using var first = TenantKeyStore.OpenOrCreate(keys, InstallId, protector);
        var before = File.ReadAllBytes(Path.Combine(keys, TenantKeyStore.FileName));

        using var second = TenantKeyStore.OpenOrCreate(keys, InstallId, protector);
        var after = File.ReadAllBytes(Path.Combine(keys, TenantKeyStore.FileName));

        Assert.Equal(first.CheckValue, second.CheckValue);
        Assert.Equal(before, after);
        Assert.Equal(
            first.PseudonymFor(SubjectDomain.Customer, "cust-1"),
            second.PseudonymFor(SubjectDomain.Customer, "cust-1"));
    }

    [Fact]
    public void There_is_no_way_to_replace_an_existing_key()
    {
        // Not "there is no reason to" — there is no method. The scheme does not
        // rotate, and beginning a new epoch is a decision about a tenant's whole
        // history rather than something reachable from a call site.
        var replacements = typeof(TenantKeyStore)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(method => method.Name)
            .Where(name => name.Contains("Rotate", StringComparison.OrdinalIgnoreCase)
                           || name.Contains("Replace", StringComparison.OrdinalIgnoreCase)
                           || name.Contains("Reset", StringComparison.OrdinalIgnoreCase)
                           || name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                           || name.Contains("Overwrite", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            replacements.Count == 0,
            "TenantKeyStore gained a way to replace the key: " + string.Join(", ", replacements)
            + "\n\nThe tenant key does not rotate (D-039, accepted cost 3). Replacing it "
            + "orphans every pseudonym already in the cloud, irreversibly.");
    }

    // -------------------------------------------------------- the binding

    [Fact]
    public void A_key_wrapped_under_one_install_does_not_unwrap_under_another()
    {
        // The entropy is bound to the install id (D-042). This is what a
        // transplanted ProgramData directory looks like, and it must be an error
        // rather than a silently different key.
        var keys = NewKeysDirectory();
        var protector = new EntropyBindingProtector();

        using (TenantKeyStore.OpenOrCreate(keys, InstallId, protector)) { }

        var error = Assert.Throws<CryptographicException>(() =>
            TenantKeyStore.OpenOrCreate(keys, "a-different-install", protector));

        Assert.Contains("could not be unwrapped", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_failure_message_tells_the_reader_not_to_delete_the_file()
    {
        // The obvious way to "fix" an unwrap failure is to delete the key and let
        // it regenerate. That is unrecoverable, so the message has to say so at
        // the moment somebody is deciding.
        var keys = NewKeysDirectory();
        var protector = new EntropyBindingProtector();
        using (TenantKeyStore.OpenOrCreate(keys, InstallId, protector)) { }

        var error = Assert.Throws<CryptographicException>(() =>
            TenantKeyStore.OpenOrCreate(keys, "other", protector));

        Assert.Contains("Do not delete this file", error.Message, StringComparison.Ordinal);
        Assert.Contains("recovery code", error.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ real DPAPI

    [Fact]
    public void The_real_wrapping_round_trips_on_windows()
    {
        // The fake protector substitutes for DPAPI's machine binding so the rest
        // of the suite is repeatable. This is the one test that exercises the
        // production wrapper, so that the substitution is not also hiding a
        // broken real path.
        if (!OperatingSystem.IsWindows())
        {
            // Waymark's till is Windows. On any other platform there is nothing
            // to assert, and pretending otherwise would be a passing test that
            // checked nothing.
            return;
        }

        var keys = NewKeysDirectory();
        var protector = new DpapiKeyProtector();

        string checkValue;
        Pseudonym pseudonym;

        using (var created = TenantKeyStore.OpenOrCreate(keys, InstallId, protector))
        {
            checkValue = created.CheckValue;
            pseudonym = created.PseudonymFor(SubjectDomain.Customer, "cust-dpapi");
        }

        // The blob on disk is not the key.
        var wrapped = File.ReadAllBytes(Path.Combine(keys, TenantKeyStore.FileName));
        Assert.True(wrapped.Length > TenantKeyStore.KeyBytes);

        using var reopened = TenantKeyStore.OpenOrCreate(keys, InstallId, protector);
        Assert.Equal(checkValue, reopened.CheckValue);
        Assert.Equal(pseudonym, reopened.PseudonymFor(SubjectDomain.Customer, "cust-dpapi"));
    }

    // ------------------------------------------------------------ refusals

    [Fact]
    public void A_disposed_pseudonymiser_refuses_rather_than_hashing_zeroes()
    {
        // Dispose zeroes the key. Continuing to serve would produce pseudonyms
        // under an all-zero key — well-formed, wrong, and identical across every
        // tenant that ever did it.
        var keys = NewKeysDirectory();
        var pseudonymiser = TenantKeyStore.OpenOrCreate(
            keys, InstallId, new EntropyBindingProtector());

        pseudonymiser.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            pseudonymiser.PseudonymFor(SubjectDomain.Customer, "cust-1"));
    }

    [Theory]
    [InlineData("", "install")]
    [InlineData("   ", "install")]
    public void A_missing_keys_directory_is_refused(string keysDirectory, string installId) =>
        Assert.Throws<ArgumentException>(() =>
            TenantKeyStore.OpenOrCreate(keysDirectory, installId, new EntropyBindingProtector()));

    [Fact]
    public void A_missing_install_id_is_refused() =>
        Assert.Throws<ArgumentException>(() =>
            TenantKeyStore.OpenOrCreate(NewKeysDirectory(), "  ", new EntropyBindingProtector()));

    public void Dispose()
    {
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
