using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain;
using Waymark.Domain.Values;
using Waymark.Persistence;

namespace Waymark.Generator.Writing;

/// <summary>
/// The store database a run writes into, created exactly the way a till creates its own.
///
/// <para>
/// <see cref="WaymarkDbContext"/>, <see cref="WaymarkSqliteOptionsExtensions.UseWaymarkSqlite"/>
/// and <see cref="WaymarkDatabaseExtensions.MigrateAndApplyTriggers"/>, with foreign keys on.
/// Every CHECK, every foreign key and every append-only trigger applies to generated rows,
/// so a generator bug fails at insert instead of producing plausible wrong data (D-046).
/// </para>
/// <para>
/// The generator is a host in the sense of CLAUDE.md §2.1 — an executable at the edge — so
/// it may name the context. It writes entities directly rather than through Application
/// handlers: it simulates what happened, it is not the application.
/// </para>
/// </summary>
internal sealed class StoreDatabase : IDisposable
{
    private StoreDatabase(string path, WaymarkDbContext context)
    {
        FilePath = path;
        Context = context;
    }

    /// <summary>The <c>waymark-store.db</c> file.</summary>
    public string FilePath { get; }

    /// <summary>The context, scoped to the generated store.</summary>
    public WaymarkDbContext Context { get; }

    /// <summary>
    /// Creates a fresh database in <paramref name="outputDirectory"/>. Refuses an existing
    /// one: a run never overwrites an earlier run's store.
    /// </summary>
    public static StoreDatabase Create(string outputDirectory, string storeId, Currency ledgerCurrency)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = WaymarkStoragePaths.StoreDatabase(outputDirectory);

        if (File.Exists(path))
        {
            throw new GeneratorInputException(
                $"{path} already exists. A run never overwrites a previous store; choose another --out.");
        }

        // Plaintext, deliberately: the generator may not reach the database key or any
        // cryptography (D-054). A generated store is fake data; StoreServer imports it into an
        // encrypted file rather than opening it in place (F-1).
        var options = new DbContextOptionsBuilder<WaymarkDbContext>()
            .UseWaymarkSqlite(path, keyProvider: null)
            .Options;

        var context = new WaymarkDbContext(options, new FixedCurrentStore(storeId), new FixedLedgerCurrency(ledgerCurrency));

        try
        {
            context.MigrateAndApplyTriggers();

            // One connection for the whole run. A generated store is disposable until it is
            // finished, so durability per statement buys nothing; speed does.
            context.Database.OpenConnection();
            context.Database.ExecuteSqlRaw("PRAGMA synchronous = OFF;");

            // Generated entities are init-only and never modified once added, so there is nothing
            // for change detection to find; scanning every tracked entity at each save is pure cost.
            context.ChangeTracker.AutoDetectChangesEnabled = false;
        }
        catch
        {
            context.Dispose();
            throw;
        }

        return new StoreDatabase(path, context);
    }

    /// <summary>Writes everything staged, then forgets it, so memory does not grow with the history.</summary>
    public int Save()
    {
        var written = Context.SaveChanges();
        Context.ChangeTracker.Clear();
        return written;
    }

    /// <summary>The open connection, for reads that need raw SQL (counts, the canonical dump).</summary>
    public SqliteConnection Connection => (SqliteConnection)Context.Database.GetDbConnection();

    public void Dispose()
    {
        Context.Database.CloseConnection();
        Context.Dispose();

        // Pooled connections keep the file open, which stops a test deleting its directory
        // and leaves a -wal file beside a store someone is about to copy.
        SqliteConnection.ClearAllPools();
    }
}
