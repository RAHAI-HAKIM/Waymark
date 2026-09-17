using System.Security.Cryptography;
using Waymark.Domain.Privacy;
using Waymark.Pseudonymisation;

namespace Waymark.Integration.Tests;

/// <summary>
/// Key custody (decisions.md D-042): 32 random bytes, wrapped at machine scope
/// under the tenant key's own entropy, in their own directory, created once and
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

    // ------------------------------------------------------------- creation

    [Fact]
    public void A_first_run_creates_a_key_and_the_directory_that_holds_it()
    {
        var keys = NewKeysDirectory();

        Assert.False(TenantKeyStore.Exists(keys));

        using var pseudonymiser = TenantKeyStore.OpenOrCreate(keys, new EntropyBindingProtector());

        Assert.True(TenantKeyStore.Exists(keys));
        Assert.True(File.Exists(Path.Combine(keys, TenantKeyStore.FileName)));
        Assert.Equal(26, pseudonymiser.CheckValue.Length);
    }

    [Fact]
    public void A_key_is_written_aside_and_moved_into_place_leaving_nothing_else()
    {
        // The key file must be absent or complete. A truncated one from a crash
        // mid-write carries an error telling the reader never to delete it.
        // What is left of an interrupted creation is a temp file nobody reads,
        // and it must not stop the next start from creating the key.
        var keys = NewKeysDirectory();
        Directory.CreateDirectory(keys);
        var interrupted = Path.Combine(keys, TenantKeyStore.FileName + ".crashed" + TenantKeyStore.TemporarySuffix);
        File.WriteAllBytes(interrupted, [1, 2, 3]);

        using var pseudonymiser = TenantKeyStore.OpenOrCreate(keys, new EntropyBindingProtector());

        Assert.Equal(
            [TenantKeyStore.FileName, Path.GetFileName(interrupted)],
            Directory.GetFiles(keys).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(
            32 + TenantKeyStore.KeyBytes,
            File.ReadAllBytes(Path.Combine(keys, TenantKeyStore.FileName)).Length);
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
            using var pseudonymiser = TenantKeyStore.OpenOrCreate(keys, protector);

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

        using var first = TenantKeyStore.OpenOrCreate(keys, protector);
        var before = File.ReadAllBytes(Path.Combine(keys, TenantKeyStore.FileName));

        using var second = TenantKeyStore.OpenOrCreate(keys, protector);
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
    public void The_database_keys_blob_does_not_unwrap_as_the_tenant_key()
    {
        // Domain separation (O-18): each key is wrapped under its own constant. A swapped file,
        // from a restore script or a mistake at a keyboard, must be an error rather than a
        // tenant key that is silently the database key.
        var keys = NewKeysDirectory();
        Directory.CreateDirectory(keys);
        var protector = new EntropyBindingProtector();
        File.WriteAllBytes(
            Path.Combine(keys, TenantKeyStore.FileName),
            protector.Protect(RandomNumberGenerator.GetBytes(32), EntropyBindingProtector.DatabaseEntropy));

        var error = Assert.Throws<CryptographicException>(() => TenantKeyStore.OpenOrCreate(keys, protector));

        Assert.Contains("could not be unwrapped", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tenant_key_is_wrapped_under_its_own_fixed_entropy()
    {
        // The entropy is a constant, not the install id: the blob unwraps on this machine with
        // nothing else to supply, and it unwraps only under the tenant key's constant.
        var keys = NewKeysDirectory();
        var protector = new EntropyBindingProtector();
        using (TenantKeyStore.OpenOrCreate(keys, protector)) { }

        var blob = File.ReadAllBytes(Path.Combine(keys, TenantKeyStore.FileName));

        Assert.Equal(32, protector.Unprotect(blob, EntropyBindingProtector.TenantEntropy).Length);
        Assert.Throws<CryptographicException>(() => protector.Unprotect(blob, EntropyBindingProtector.DatabaseEntropy));
    }

    [Fact]
    public void The_failure_message_tells_the_reader_not_to_delete_the_file()
    {
        // The obvious way to "fix" an unwrap failure is to delete the key and let
        // it regenerate. That is unrecoverable, so the message has to say so at
        // the moment somebody is deciding.
        var keys = NewKeysDirectory();
        Directory.CreateDirectory(keys);
        var protector = new EntropyBindingProtector();
        File.WriteAllBytes(Path.Combine(keys, TenantKeyStore.FileName), [1, 2, 3]);

        var error = Assert.Throws<CryptographicException>(() => TenantKeyStore.OpenOrCreate(keys, protector));

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

        using (var created = TenantKeyStore.OpenOrCreate(keys, protector))
        {
            checkValue = created.CheckValue;
            pseudonym = created.PseudonymFor(SubjectDomain.Customer, "cust-dpapi");
        }

        // The blob on disk is not the key.
        var wrapped = File.ReadAllBytes(Path.Combine(keys, TenantKeyStore.FileName));
        Assert.True(wrapped.Length > TenantKeyStore.KeyBytes);

        using var reopened = TenantKeyStore.OpenOrCreate(keys, protector);
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
        var pseudonymiser = TenantKeyStore.OpenOrCreate(keys, new EntropyBindingProtector());

        pseudonymiser.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            pseudonymiser.PseudonymFor(SubjectDomain.Customer, "cust-1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_keys_directory_is_refused(string keysDirectory) =>
        Assert.Throws<ArgumentException>(() =>
            TenantKeyStore.OpenOrCreate(keysDirectory, new EntropyBindingProtector()));

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
