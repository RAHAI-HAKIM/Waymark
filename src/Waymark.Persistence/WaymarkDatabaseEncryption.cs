using System.Data.Common;
using Microsoft.Data.Sqlite;
using Waymark.Domain.Privacy;

namespace Waymark.Persistence;

/// <summary>
/// SQLCipher for <c>waymark-store.db</c> (D-040, D-042, F-1): keying a connection, recognising a
/// plaintext file, and turning one into an encrypted copy.
///
/// <para>
/// <b>The key is the raw-key form</b>, <c>PRAGMA key = "x'…'"</c>, which skips SQLCipher's key
/// derivation: the key is already 32 random bytes, so stretching it buys nothing. Every other
/// cipher setting is SQLCipher's default, deliberately; a changed default is a file no other
/// build can open.
/// </para>
/// <para>
/// <b>The key never travels in a connection string.</b> Microsoft.Data.Sqlite's
/// <c>Password</c> keyword would put it where an EF log, a diagnostic listener or an exception
/// message can print it. It is issued as the first statement on each opened connection instead,
/// and the byte copy is zeroed straight after. The hex text of the statement is a managed string
/// and cannot be zeroed; that is the residue this design accepts.
/// </para>
/// </summary>
public static class WaymarkDatabaseEncryption
{
    /// <summary>The 16 bytes every plaintext SQLite file starts with, and no encrypted one does.</summary>
    private static readonly byte[] PlaintextHeader = "SQLite format 3\0"u8.ToArray();

    /// <summary>
    /// Keys <paramref name="connection"/> and proves the key opens the file. Must be the first
    /// statement on the connection: SQLCipher refuses anything that reaches the file before it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The key does not open the file, or the file is not encrypted. The message never contains
    /// the key.
    /// </exception>
    public static void ApplyKey(DbConnection connection, IDatabaseKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(keyProvider);

        var key = keyProvider.GetKey();
        try
        {
            if (key.Length != IDatabaseKeyProvider.KeyBytes)
            {
                throw new InvalidOperationException(
                    $"The database key is {key.Length} bytes; SQLCipher's raw key is {IDatabaseKeyProvider.KeyBytes}.");
            }

            using (var command = connection.CreateCommand())
            {
#pragma warning disable CA2100 // Hex digits of our own key, not input. PRAGMA takes no parameters.
                command.CommandText = $"PRAGMA key = \"x'{Convert.ToHexString(key)}'\";";
#pragma warning restore CA2100
                command.ExecuteNonQuery();
            }

            // PRAGMA key always succeeds; a wrong key shows at the first read. Read now, so the
            // failure is here and says what it means, rather than in whatever query came next.
            using var probe = connection.CreateCommand();
            probe.CommandText = "SELECT count(*) FROM sqlite_schema;";
            probe.ExecuteScalar();
        }
        catch (SqliteException error) when (error.SqliteErrorCode == 26)
        {
            throw new InvalidOperationException(
                "The store database could not be opened with its key: the key is wrong, or the "
                + "file is not encrypted. A plaintext store is imported, never opened in place "
                + "(WaymarkDatabaseEncryption.EncryptCopy). Do not delete the key file to make this "
                + "go away: without it the store's history cannot be read.",
                error);
        }
        finally
        {
            Array.Clear(key);
        }
    }

    /// <summary>Whether <paramref name="path"/> is an unencrypted SQLite file. False for a missing or empty file.</summary>
    public static bool IsPlaintext(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return false;
        }

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> header = stackalloc byte[PlaintextHeader.Length];
        return file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
               && header.SequenceEqual(PlaintextHeader);
    }

    /// <summary>
    /// Writes an encrypted copy of a plaintext store: SQLCipher's <c>sqlcipher_export</c> into a
    /// newly attached, keyed database. Never a file copy, which would still be plaintext.
    /// </summary>
    /// <exception cref="IOException"><paramref name="encryptedPath"/> already exists.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="plaintextPath"/> is not a plaintext SQLite file.</exception>
    public static void EncryptCopy(string plaintextPath, string encryptedPath, IDatabaseKeyProvider keyProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedPath);
        ArgumentNullException.ThrowIfNull(keyProvider);

        if (!IsPlaintext(plaintextPath))
        {
            throw new InvalidOperationException($"{plaintextPath} is not a plaintext SQLite database.");
        }

        if (File.Exists(encryptedPath))
        {
            throw new IOException($"{encryptedPath} already exists; an import never overwrites a store.");
        }

        var source = new SqliteConnectionStringBuilder
        {
            DataSource = plaintextPath,
            // ReadWriteCreate: an attached database inherits the open mode, and the copy is new.
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };

        using var connection = new SqliteConnection(source.ToString());
        connection.Open();

        var key = keyProvider.GetKey();
        try
        {
            using var attach = connection.CreateCommand();
            attach.CommandText = "ATTACH DATABASE $path AS encrypted KEY $key;";
            attach.Parameters.AddWithValue("$path", encryptedPath);
            attach.Parameters.AddWithValue("$key", $"x'{Convert.ToHexString(key)}'");
            attach.ExecuteNonQuery();
        }
        finally
        {
            Array.Clear(key);
        }

        using (var export = connection.CreateCommand())
        {
            export.CommandText = "SELECT sqlcipher_export('encrypted');";
            export.ExecuteScalar();
        }

        using var detach = connection.CreateCommand();
        detach.CommandText = "DETACH DATABASE encrypted;";
        detach.ExecuteNonQuery();
    }
}
