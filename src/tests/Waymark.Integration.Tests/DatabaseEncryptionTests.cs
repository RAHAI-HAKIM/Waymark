using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Waymark.Domain;
using Waymark.Domain.Enums;
using Waymark.Domain.Privacy;
using Waymark.Domain.Reference;
using Waymark.Domain.Values;
using Waymark.Persistence;

namespace Waymark.Integration.Tests;

/// <summary>
/// <c>waymark-store.db</c> encrypted at rest (F-1, D-040, D-042): SQLCipher keyed with a raw
/// 32-byte key as the first statement on every connection, never through the connection string.
/// </summary>
public sealed class DatabaseEncryptionTests : IDisposable
{
    /// <summary>A label no page of an encrypted file may contain in the clear.</summary>
    private const string Canary = "WAYMARK-CANARY-7f3a91";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "waymark-cipher", Guid.NewGuid().ToString("N"));
    private readonly FixedKey _key = new(RandomNumberGenerator.GetBytes(32));

    public DatabaseEncryptionTests() => Directory.CreateDirectory(_directory);

    private string NewPath(string name = "waymark-store.db") => Path.Combine(_directory, name);

    private static WaymarkDbContext Context(string path, IDatabaseKeyProvider? key, bool enforceForeignKeys = true, Action<DbContextOptionsBuilder>? before = null)
    {
        var builder = new DbContextOptionsBuilder<WaymarkDbContext>();
        before?.Invoke(builder);
        builder.UseWaymarkSqlite(path, key, enforceForeignKeys);
        return new WaymarkDbContext(builder.Options, new FixedCurrentStore(null), new FixedLedgerCurrency(Currency.Dzd));
    }

    /// <summary>A migrated database holding one reason code labelled with the canary.</summary>
    private string CreateStore(IDatabaseKeyProvider? key, string name = "waymark-store.db")
    {
        var path = NewPath(name);
        using var context = Context(path, key);
        context.MigrateAndApplyTriggers();
        context.ReasonCodes.Add(new ReasonCode
        {
            ReasonCodeValue = "CANARY",
            AppliesTo = ReasonCodeAppliesTo.Discount,
            LabelAr = Canary,
            LabelFr = Canary,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        context.SaveChanges();

        // Fold the WAL into the main file, so the bytes checked below are all there is.
        context.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE);");
        return path;
    }

    private static string? ReadCanary(WaymarkDbContext context) =>
        context.ReasonCodes.Where(r => r.ReasonCodeValue == "CANARY").Select(r => r.LabelFr).SingleOrDefault();

    [Fact]
    public void Migrations_run_end_to_end_against_an_encrypted_file()
    {
        var path = CreateStore(_key);

        using var context = Context(path, _key);
        context.MigrateAndApplyTriggers();

        Assert.Empty(context.FindMissingTriggers());
        Assert.Empty(context.Database.GetPendingMigrations());
        Assert.Equal(Canary, ReadCanary(context));
        Assert.False(WaymarkDatabaseEncryption.IsPlaintext(path));
    }

    [Fact]
    public void The_file_holds_no_SQLite_header_no_schema_and_no_data_in_the_clear()
    {
        var path = CreateStore(_key);
        SqliteConnection.ClearAllPools();

        var bytes = Directory.GetFiles(_directory).SelectMany(File.ReadAllBytes).ToArray();
        var asText = Encoding.Latin1.GetString(bytes);

        Assert.True(bytes.Length > 4096, "The encrypted store is suspiciously small.");
        Assert.DoesNotContain("SQLite format 3", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE TABLE", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("reason_codes", asText, StringComparison.Ordinal);
        Assert.DoesNotContain(Canary, asText, StringComparison.Ordinal);

        // The control: the same store in plaintext does show all of it, so the search can find it.
        var plain = File.ReadAllBytes(CreateStore(null, "plain.db"));
        Assert.Contains(Canary, Encoding.Latin1.GetString(plain), StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_without_the_key_fails()
    {
        var path = CreateStore(_key);

        using var raw = new SqliteConnection($"Data Source={path};Pooling=False");
        raw.Open();
        var error = Assert.Throws<SqliteException>(() => Scalar(raw, "SELECT count(*) FROM sqlite_schema"));
        Assert.Equal(26, error.SqliteErrorCode);

        using var context = Context(path, key: null);
        Assert.ThrowsAny<SqliteException>(() => ReadCanary(context));
    }

    [Fact]
    public void Opening_with_the_wrong_key_fails_and_the_error_names_no_key()
    {
        var path = CreateStore(_key);
        var wrong = new FixedKey(RandomNumberGenerator.GetBytes(32));

        using var context = Context(path, wrong);
        var error = Assert.Throws<InvalidOperationException>(() => ReadCanary(context));

        Assert.Contains("could not be opened with its key", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(wrong.Hex, error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_key.Hex, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_key_is_the_first_statement_on_a_connection_and_foreign_keys_come_after()
    {
        var path = CreateStore(_key);
        var tracer = new StatementTracer();

        using var context = Context(path, _key, before: builder => builder.AddInterceptors(tracer));
        Assert.Equal(Canary, ReadCanary(context));

        Assert.StartsWith("PRAGMA key = \"x'", tracer.Statements[0], StringComparison.Ordinal);
        Assert.Equal("SELECT count(*) FROM sqlite_schema;", tracer.Statements[1]);
        Assert.Equal("PRAGMA foreign_keys = ON;", tracer.Statements[2]);

        // Nothing reaches the file before the key: the connection string carries the path alone,
        // so Microsoft.Data.Sqlite issues no statement of its own while opening.
        var connectionString = new SqliteConnectionStringBuilder(context.Database.GetConnectionString());
        Assert.Equal(path, connectionString.DataSource);
        Assert.Equal(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString(), connectionString.ToString());
    }

    [Fact]
    public void The_key_never_appears_in_the_connection_string_or_the_EF_log()
    {
        var path = CreateStore(_key);
        var log = new StringBuilder();

        using (var context = Context(path, _key, before: builder => builder
                   .LogTo(line => log.AppendLine(line), Microsoft.Extensions.Logging.LogLevel.Trace)
                   .EnableSensitiveDataLogging()))
        {
            Assert.Equal(Canary, ReadCanary(context));
            Assert.DoesNotContain(_key.Hex, context.Database.GetConnectionString()!, StringComparison.OrdinalIgnoreCase);
        }

        Assert.True(log.Length > 0, "Nothing was logged, so the search below searched nothing.");
        Assert.DoesNotContain(_key.Hex, log.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRAGMA key", log.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_handle_unlocked_by_one_key_is_never_reused_under_another()
    {
        // Microsoft.Data.Sqlite pools by connection string, and the key is not in it. With pooling
        // on, a context holding the wrong key, or none, was handed a handle the right key had
        // already unlocked, and read the store. Each open must be a fresh, freshly keyed handle.
        var path = CreateStore(_key);

        for (var i = 0; i < 3; i++)
        {
            using (var right = Context(path, _key))
            {
                Assert.Equal(Canary, ReadCanary(right));
                Assert.Equal(Canary, ReadCanary(right));
            }

            using (var wrong = Context(path, new FixedKey(RandomNumberGenerator.GetBytes(32))))
            {
                Assert.Throws<InvalidOperationException>(() => ReadCanary(wrong));
            }

            using var none = Context(path, key: null);
            Assert.ThrowsAny<SqliteException>(() => ReadCanary(none));
        }

        Assert.False(new SqliteConnectionStringBuilder(Context(path, _key).Database.GetConnectionString()).Pooling);
    }

    [Fact]
    public void Foreign_keys_are_still_enforced_on_an_encrypted_file_and_can_be_switched_off()
    {
        var path = CreateStore(_key);
        const string orphan = "INSERT INTO receivable_movements (movement_id, store_id, customer_id, movement_type, amount, occurred_at, payment_id) "
            + "VALUES ('orphan', 'no-store', 'no-customer', 'charge', 100, '2026-09-16T10:00:00Z', 'no-payment')";

        using (var enforced = Context(path, _key))
        {
            var error = Assert.Throws<SqliteException>(() => enforced.Database.ExecuteSqlRaw(orphan));
            Assert.Contains("FOREIGN KEY", error.Message, StringComparison.Ordinal);
        }

        using var relaxed = Context(path, _key, enforceForeignKeys: false);
        Assert.Equal(1, relaxed.Database.ExecuteSqlRaw(orphan));
    }

    [Fact]
    public void Rekey_replaces_the_key_and_the_old_one_stops_working()
    {
        var path = CreateStore(_key);
        var next = new FixedKey(RandomNumberGenerator.GetBytes(32));

        using (var connection = Open(path, _key))
        {
            Execute(connection, $"PRAGMA rekey = \"x'{next.Hex}'\";");
        }

        using (var old = Context(path, _key))
        {
            Assert.Throws<InvalidOperationException>(() => ReadCanary(old));
        }

        using var current = Context(path, next);
        Assert.Equal(Canary, ReadCanary(current));
    }

    [Fact]
    public void A_plaintext_store_is_imported_as_an_encrypted_copy_with_its_rows_and_triggers()
    {
        var plain = CreateStore(null, "plain.db");
        var encrypted = NewPath("imported.db");

        Assert.True(WaymarkDatabaseEncryption.IsPlaintext(plain));
        WaymarkDatabaseEncryption.EncryptCopy(plain, encrypted, _key);

        Assert.False(WaymarkDatabaseEncryption.IsPlaintext(encrypted));
        Assert.DoesNotContain(Canary, Encoding.Latin1.GetString(File.ReadAllBytes(encrypted)), StringComparison.Ordinal);

        using var context = Context(encrypted, _key);
        Assert.Equal(Canary, ReadCanary(context));
        Assert.Empty(context.FindMissingTriggers());
        Assert.Empty(context.Database.GetPendingMigrations());
    }

    [Fact]
    public void An_import_never_overwrites_and_never_takes_an_encrypted_source()
    {
        var plain = CreateStore(null, "plain.db");
        var encrypted = CreateStore(_key, "encrypted.db");

        Assert.Throws<IOException>(() => WaymarkDatabaseEncryption.EncryptCopy(plain, encrypted, _key));
        Assert.Throws<InvalidOperationException>(() => WaymarkDatabaseEncryption.EncryptCopy(encrypted, NewPath("copy.db"), _key));
        Assert.False(WaymarkDatabaseEncryption.IsPlaintext(NewPath("missing.db")));
    }

    [Fact]
    public void A_key_of_the_wrong_length_is_refused_before_it_reaches_the_file()
    {
        var path = NewPath();

        using var context = Context(path, new FixedKey(new byte[16]));
        var error = Assert.Throws<InvalidOperationException>(() => context.MigrateAndApplyTriggers());

        Assert.Contains("16 bytes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_key_copy_handed_out_is_zeroed_after_use()
    {
        var key = new FixedKey(RandomNumberGenerator.GetBytes(32));
        var path = CreateStore(key);

        using (var context = Context(path, key))
        {
            Assert.Equal(Canary, ReadCanary(context));
        }

        Assert.NotEmpty(key.HandedOut);
        Assert.All(key.HandedOut, copy => Assert.All(copy, b => Assert.Equal(0, b)));
    }

    [Fact]
    public void Pages_still_in_the_write_ahead_log_are_encrypted_too()
    {
        var path = CreateStore(_key);

        // Hold a connection open so the new rows stay in the -wal file rather than being folded in.
        using var context = Context(path, _key);
        context.Database.OpenConnection();
        Assert.Equal("wal", context.Database.SqlQueryRaw<string>("PRAGMA journal_mode").AsEnumerable().Single());
        for (var i = 0; i < 20; i++)
        {
            context.ReasonCodes.Add(new ReasonCode
            {
                ReasonCodeValue = $"WAL-{i}",
                AppliesTo = ReasonCodeAppliesTo.Discount,
                LabelAr = Canary + "-wal",
                LabelFr = Canary + "-wal",
                CreatedAt = DateTimeOffset.UnixEpoch,
            });
        }

        context.SaveChanges();

        var wal = path + "-wal";
        Assert.True(File.Exists(wal) && new FileInfo(wal).Length > 0, "Nothing was written to the WAL, so nothing was checked.");
        Assert.DoesNotContain(Canary, Encoding.Latin1.GetString(ReadShared(wal)), StringComparison.Ordinal);
        Assert.DoesNotContain(Canary, Encoding.Latin1.GetString(ReadShared(path)), StringComparison.Ordinal);
    }

    [Fact]
    public void An_import_takes_the_rows_a_plaintext_store_still_holds_in_its_write_ahead_log()
    {
        var plain = CreateStore(null, "plain.db");

        using var writer = Context(plain, key: null);
        writer.Database.OpenConnection();
        writer.ReasonCodes.Add(new ReasonCode
        {
            ReasonCodeValue = "LATE",
            AppliesTo = ReasonCodeAppliesTo.Discount,
            LabelAr = "late",
            LabelFr = "late",
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        writer.SaveChanges();
        Assert.True(new FileInfo(plain + "-wal").Length > 0);

        var encrypted = NewPath("imported.db");
        WaymarkDatabaseEncryption.EncryptCopy(plain, encrypted, _key);

        using var reader = Context(encrypted, _key);
        Assert.Equal(["CANARY", "LATE"], reader.ReasonCodes.Select(r => r.ReasonCodeValue).OrderBy(code => code).ToList());
    }

    [Fact]
    public void Temporary_tables_and_sorts_stay_in_memory_where_they_cannot_leak_plaintext()
    {
        // SQLCipher encrypts the database and its journals; a temporary file on disk is a separate
        // question. The bundle keeps temp storage in memory unless a pragma says otherwise.
        var path = CreateStore(_key);
        using var connection = Open(path, _key);

        Assert.Equal("0", Convert.ToString(Scalar(connection, "PRAGMA temp_store"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("TEMP_STORE=2", Convert.ToString(Scalar(connection, "SELECT group_concat(compile_options, ',') FROM pragma_compile_options"), System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static byte[] ReadShared(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream();
        file.CopyTo(copy);
        return copy.ToArray();
    }

    private static SqliteConnection Open(string path, IDatabaseKeyProvider key)
    {
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        WaymarkDatabaseEncryption.ApplyKey(connection, key);
        return connection;
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A locked file on a build agent must not fail the run.
        }
    }

    /// <summary>A key with known bytes, which records every copy it hands out.</summary>
    private sealed class FixedKey(byte[] key) : IDatabaseKeyProvider
    {
        public string Hex { get; } = Convert.ToHexString(key);

        public List<byte[]> HandedOut { get; } = [];

        public byte[] GetKey()
        {
            var copy = (byte[])key.Clone();
            HandedOut.Add(copy);
            return copy;
        }
    }

    /// <summary>
    /// Registered before Waymark's interceptor, so it sees the connection first and installs a
    /// SQLite trace that records every statement sent afterwards, whoever sends it.
    /// </summary>
    private sealed class StatementTracer : DbConnectionInterceptor
    {
        public List<string> Statements { get; } = [];

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            if (Statements.Count == 0)
            {
                SQLitePCL.raw.sqlite3_trace(((SqliteConnection)connection).Handle, (_, statement) => Statements.Add(statement), null);
            }
        }
    }
}
