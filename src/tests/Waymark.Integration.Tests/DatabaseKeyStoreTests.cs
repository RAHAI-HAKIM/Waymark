using System.Security.Cryptography;
using Waymark.Pseudonymisation;

namespace Waymark.Integration.Tests;

/// <summary>
/// The database key's custody (D-042, F-1): made once before the database, never made again
/// beside an existing one, wrapped under its own entropy, handed out as copies.
/// </summary>
public sealed class DatabaseKeyStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "waymark-dbkey", Guid.NewGuid().ToString("N"));
    private readonly EntropyBindingProtector _protector = new();

    private string Keys => Path.Combine(_root, "keys");

    [Fact]
    public void A_first_run_creates_a_32_byte_key_that_the_next_run_reads_back()
    {
        byte[] first;
        using (var created = DatabaseKeyStore.OpenOrCreate(Keys, _protector))
        {
            first = created.GetKey();
        }

        Assert.True(DatabaseKeyStore.Exists(Keys));
        Assert.Equal(32, first.Length);
        Assert.NotEqual(new byte[32], first);

        using var reopened = DatabaseKeyStore.OpenOrCreate(Keys, _protector);
        Assert.Equal(first, reopened.GetKey());

        using var opened = DatabaseKeyStore.Open(Keys, _protector);
        Assert.Equal(first, opened.GetKey());
    }

    [Fact]
    public void The_blob_on_disk_is_the_key_wrapped_under_the_database_keys_own_entropy()
    {
        using var store = DatabaseKeyStore.OpenOrCreate(Keys, _protector);
        var blob = File.ReadAllBytes(Path.Combine(Keys, DatabaseKeyStore.FileName));

        Assert.Equal(store.GetKey(), _protector.Unprotect(blob, EntropyBindingProtector.DatabaseEntropy));
        Assert.Throws<CryptographicException>(() => _protector.Unprotect(blob, EntropyBindingProtector.TenantEntropy));
    }

    [Fact]
    public void The_tenant_keys_blob_does_not_unwrap_as_the_database_key()
    {
        Directory.CreateDirectory(Keys);
        File.WriteAllBytes(
            Path.Combine(Keys, DatabaseKeyStore.FileName),
            _protector.Protect(RandomNumberGenerator.GetBytes(32), EntropyBindingProtector.TenantEntropy));

        var error = Assert.Throws<CryptographicException>(() => DatabaseKeyStore.Open(Keys, _protector));

        Assert.Contains("could not be unwrapped", error.Message, StringComparison.Ordinal);
        Assert.Contains("Do not delete this file", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_existing_database_never_gets_a_new_key()
    {
        // Open is what the host calls when the database file exists. A missing key there is a
        // restore gone wrong; a fresh key could not open the file and would hide the problem.
        var error = Assert.Throws<FileNotFoundException>(() => DatabaseKeyStore.Open(Keys, _protector));

        Assert.Contains("recovery code", error.Message, StringComparison.Ordinal);
        Assert.False(DatabaseKeyStore.Exists(Keys));
    }

    [Fact]
    public void Each_installation_gets_its_own_key()
    {
        using var first = DatabaseKeyStore.OpenOrCreate(Keys, _protector);
        using var second = DatabaseKeyStore.OpenOrCreate(Path.Combine(_root, "other"), _protector);

        Assert.NotEqual(first.GetKey(), second.GetKey());
    }

    [Fact]
    public void A_key_handed_out_is_a_copy_and_a_disposed_store_hands_out_nothing()
    {
        var store = DatabaseKeyStore.OpenOrCreate(Keys, _protector);
        var copy = store.GetKey();
        var original = (byte[])copy.Clone();

        // A caller zeroing its copy, as it should, must not zero the store's key.
        Array.Clear(copy);
        Assert.Equal(original, store.GetKey());

        store.Dispose();
        Assert.Throws<ObjectDisposedException>(() => store.GetKey());
    }

    [Fact]
    public void The_real_wrapping_round_trips_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            // The till is Windows; elsewhere there is nothing to assert.
            return;
        }

        var protector = new DpapiKeyProtector();
        byte[] key;
        using (var created = DatabaseKeyStore.OpenOrCreate(Keys, protector))
        {
            key = created.GetKey();
        }

        var blob = File.ReadAllBytes(Path.Combine(Keys, DatabaseKeyStore.FileName));
        Assert.True(blob.Length > 32);
        Assert.False(blob.AsSpan().IndexOf(key) >= 0, "The key is in the wrapped blob in the clear.");

        using var reopened = DatabaseKeyStore.Open(Keys, protector);
        Assert.Equal(key, reopened.GetKey());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
