using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Customers;
using Waymark.Domain.Enums;
using Waymark.Domain.Ids;
using Waymark.Domain.Privacy;
using Waymark.Domain.Sync;
using Waymark.Persistence.Privacy;
using Waymark.Pseudonymisation;

namespace Waymark.Integration.Tests;

/// <summary>
/// <b>The most important test in W7, and the one D-039 names as the condition
/// the whole decision rests on.</b>
///
/// <para>
/// `waymark-identity.db` used to be excluded from the cloud backup because it
/// was a <i>file</i>, and excluding a file is configuration rather than a rule
/// anyone has to remember. D-039 removed that file and accepted, as cost 1, that
/// exclusion would become "a 32-byte secret must not be put into a payload" —
/// which is a rule someone can forget. D-042 won back the file-level half by
/// putting the keys in their own directory behind an allowlist. <b>This suite is
/// the other half.</b> It is what stands between Waymark-the-company holding
/// identifiers in a backup and pseudonyms in the cloud, which is the single
/// circumstance the design exists to prevent (DPIA §5.2).
/// </para>
/// <para>
/// It searches for the key in <b>four representations</b> — raw bytes, hex in
/// both cases, and base64 — because a leak is rarely a memcpy. It is somebody
/// logging a diagnostic, serialising a settings object, or putting the key in an
/// error message, and every one of those converts it to text first.
/// </para>
/// <para>
/// <b>It must also search for the wrapped blob, not only the key.</b> D-042 says
/// so explicitly: DPAPI at machine scope is unwrappable by anything on that
/// machine, so shipping the blob off the till is very nearly shipping the key —
/// and a test that looked only for the plaintext would pass while the blob went
/// to the cloud in every backup.
/// </para>
/// </summary>
public sealed class TenantKeyNeverLeavesTests : IClassFixture<MigratedDatabaseFixture>, IDisposable
{
    private readonly MigratedDatabaseFixture _database;
    private readonly string _keysDirectory;
    private readonly TenantPseudonymiser _pseudonymiser;
    private readonly byte[] _keyBytes;
    private readonly byte[] _wrappedBlob;

    public TenantKeyNeverLeavesTests(MigratedDatabaseFixture database)
    {
        _database = database;

        _keysDirectory = Path.Combine(
            Path.GetTempPath(), "waymark-leak", Guid.NewGuid().ToString("N"));

        // A real key, generated the production way, so this is not a test
        // against a constant somebody could have special-cased.
        _keyBytes = RandomNumberGenerator.GetBytes(TenantKeyStore.KeyBytes);

        var protector = new EntropyBindingProtector();
        Directory.CreateDirectory(_keysDirectory);
        _wrappedBlob = protector.Protect(
            _keyBytes, EntropyBindingProtector.TenantEntropy);
        File.WriteAllBytes(
            Path.Combine(_keysDirectory, TenantKeyStore.FileName), _wrappedBlob);

        _pseudonymiser = TenantKeyStore.OpenOrCreate(_keysDirectory, protector);
    }

    private sealed class FixedIds(string id) : IIdGenerator
    {
        public string NewId() => id;
    }

    /// <summary>
    /// Every way 32 bytes plausibly reach a file: as themselves, as hex in
    /// either case, or as base64.
    /// </summary>
    private static IEnumerable<(string Name, byte[] Needle)> Representations(string label, byte[] secret) =>
    [
        ($"{label} (raw bytes)", secret),
        ($"{label} (lowercase hex)", Encoding.UTF8.GetBytes(Convert.ToHexString(secret).ToLowerInvariant())),
        ($"{label} (uppercase hex)", Encoding.UTF8.GetBytes(Convert.ToHexString(secret))),
        ($"{label} (base64)", Encoding.UTF8.GetBytes(Convert.ToBase64String(secret))),
    ];

    /// <summary>
    /// Writes the kinds of row that would carry a leak if one existed: a
    /// customer, pseudonymised log entries, and outbox messages with JSON
    /// payloads — the things that actually go to the cloud.
    /// </summary>
    private void WriteTheDataThatCrossesTheBoundary(string tag)
    {
        using var context = _database.NewContext(enforceForeignKeys: false);
        var customerId = "01LEAKCUSTOMER" + tag;

        context.Customers.Add(new Customer
        {
            CustomerId = customerId,
            CustomerName = "Leak Probe",
            JoinDate = new DateOnly(2026, 1, 1),
            Status = CustomerStatus.Active,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        });

        var writer = new ProcessingLogWriter(
            context,
            new FixedIds("01LEAKLOGENTRY" + tag),
            new Waymark.Domain.FixedCurrentStore(null),
            TimeProvider.System);

        writer.Record(new ProcessingEvent(
            Operation.Pseudonymisation,
            ProcessingLogEntrySubjectType.Customer,
            _pseudonymiser.PseudonymFor(SubjectDomain.Customer, customerId),
            ActorType.System,
            ActorId: null,
            ProcessingPurpose.AnalyticsPseudonymised,
            ProcessingLegalBasis.LegitimateInterest,
            SourceModule: "Waymark.Pseudonymisation"));

        context.Outbox.Add(new OutboxMessage
        {
            OutboxId = "01LEAKOUTBOX" + tag,
            SequenceNumber = Random.Shared.NextInt64(1, long.MaxValue),
            Channel = OutboxMessageChannel.AStatistics,
            MessageType = "customer_period_record",
            EntityType = "customer",
            EntityId = null,
            PayloadJson =
                $$"""
                {"pseudonym":"{{_pseudonymiser.PseudonymFor(SubjectDomain.Customer, customerId)}}",
                 "key_check":"{{_pseudonymiser.CheckValue}}","visits":4}
                """,
            IsPriority = false,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });

        context.SaveChanges();
    }

    /// <summary>Every byte of the database and its write-ahead siblings.</summary>
    private IEnumerable<(string File, byte[] Bytes)> TheBackedUpFiles()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _database.DatabasePath + suffix;
            if (!File.Exists(path))
            {
                continue;
            }

            // SQLite still holds the file. Share everything — this only reads,
            // and an exclusive open throws rather than proving anything.
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            yield return (Path.GetFileName(path), buffer.ToArray());
        }
    }

    // ------------------------------------------------------------ the search

    [Fact]
    public void The_tenant_key_is_nowhere_in_the_database_that_gets_backed_up()
    {
        WriteTheDataThatCrossesTheBoundary("A");

        var found = new List<string>();

        foreach (var (file, bytes) in TheBackedUpFiles())
        {
            foreach (var (name, needle) in Representations("tenant key", _keyBytes))
            {
                if (bytes.AsSpan().IndexOf(needle) >= 0)
                {
                    found.Add($"{name} found in {file}");
                }
            }
        }

        Assert.True(
            found.Count == 0,
            $"""
            The tenant key reached waymark-store.db, which is backed up to
            Waymark's cloud:
              {string.Join(Environment.NewLine + "  ", found)}

            Waymark would then hold the identifiers in the backup and the
            pseudonyms in the cloud — the one circumstance D-039 exists to
            prevent. This is not a bug to be fixed later.
            """);
    }

    [Fact]
    public void The_wrapped_blob_is_nowhere_in_it_either()
    {
        // D-042, in as many words: the test must assert on the wrapped blob as
        // well as the raw bytes, or it passes while the DPAPI blob ships. A
        // machine-scope blob plus the machine is the key.
        WriteTheDataThatCrossesTheBoundary("B");

        var found = new List<string>();

        foreach (var (file, bytes) in TheBackedUpFiles())
        {
            foreach (var (name, needle) in Representations("wrapped key blob", _wrappedBlob))
            {
                if (bytes.AsSpan().IndexOf(needle) >= 0)
                {
                    found.Add($"{name} found in {file}");
                }
            }
        }

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
    }

    [Fact]
    public void No_outbox_payload_carries_the_key_in_any_form()
    {
        // The narrower, sharper version of the two above: the outbox is what is
        // deliberately transmitted, rather than what merely sits on disk.
        WriteTheDataThatCrossesTheBoundary("C");

        using var context = _database.NewContext(enforceForeignKeys: false);
        var payloads = context.Outbox.IgnoreQueryFilters()
            .Select(message => message.PayloadJson)
            .ToList();

        Assert.NotEmpty(payloads);

        foreach (var payload in payloads)
        {
            var bytes = Encoding.UTF8.GetBytes(payload);

            foreach (var (name, needle) in
                     Representations("tenant key", _keyBytes)
                         .Concat(Representations("wrapped key blob", _wrappedBlob)))
            {
                Assert.True(
                    bytes.AsSpan().IndexOf(needle) < 0,
                    $"An outbox payload carries the {name}.");
            }
        }
    }

    // -------------------------------------------------- the structural half

    [Fact]
    public void The_keys_directory_is_not_inside_the_directory_that_is_backed_up()
    {
        // D-042's file-level control. If keys lived under data/, a backup job
        // that takes a directory would sweep the key up without anybody writing
        // a line of code wrong.
        var root = Path.Combine(Path.GetTempPath(), "waymark-paths-w7");
        var data = Waymark.Persistence.WaymarkStoragePaths.DefaultDataDirectory;
        var keys = Waymark.Persistence.WaymarkStoragePaths.DefaultKeysDirectory;

        Assert.False(
            keys.StartsWith(data + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            $"The keys directory ({keys}) is inside the data directory ({data}). "
            + "They are siblings for a reason (D-013, D-042).");

        Assert.NotEqual(data, keys, StringComparer.OrdinalIgnoreCase);
        _ = root;
    }

    [Fact]
    public void Nothing_that_holds_the_key_offers_a_way_to_read_it()
    {
        // The same device that keeps a public constructor off Pseudonym and a
        // string overload off IProcessingLog. The key enters through an internal
        // constructor and leaves only as HMAC output; a member handing out bytes
        // would be the first step to it appearing in a payload, and it would be
        // added in good faith by somebody writing a diagnostic.
        Type[] byteShaped =
        [
            typeof(byte[]), typeof(ReadOnlyMemory<byte>), typeof(Memory<byte>),
            typeof(ReadOnlySpan<byte>), typeof(Span<byte>), typeof(IReadOnlyList<byte>),
        ];

        var offenders = typeof(TenantPseudonymiser)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(member => (member, returned: ReturnTypeOf(member)))
            .Where(pair => pair.returned is not null && byteShaped.Contains(pair.returned))
            .Select(pair => $"{pair.member.Name} returns {pair.returned!.Name}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"""
            TenantPseudonymiser gained a way to hand out key material:
              {string.Join(Environment.NewLine + "  ", offenders)}

            The key enters by an internal constructor and leaves only as HMAC
            output. Whatever needs bytes does not need these bytes.
            """);
    }

    private static Type? ReturnTypeOf(MemberInfo member) => member switch
    {
        MethodInfo method => method.ReturnType,
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => null,
    };

    public void Dispose()
    {
        _pseudonymiser.Dispose();

        try
        {
            Directory.Delete(_keysDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A locked file on a build agent must not fail the run.
        }

        GC.SuppressFinalize(this);
    }
}
