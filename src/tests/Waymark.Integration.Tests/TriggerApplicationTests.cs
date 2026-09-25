using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Waymark.Domain;
using Waymark.Domain.Values;
using Waymark.Persistence;

namespace Waymark.Integration.Tests;

/// <summary>
/// The append-only triggers survive the one thing that removes them.
///
/// <para>
/// EF cannot see triggers, and its table rebuild issues <c>DROP TABLE</c>,
/// which takes them along. Nothing in EF reports this. So the guarantee that
/// <c>consent_events</c> is append-only rests entirely on
/// <c>ApplyTriggers()</c> running after every migration, and these tests are
/// what say it does.
/// </para>
/// </summary>
public sealed class TriggerApplicationTests : IDisposable
{
    private readonly string _directory;

    public TriggerApplicationTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "waymark-triggers", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    private WaymarkDbContext NewContext(string name, IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<WaymarkDbContext>()
            .UseWaymarkSqlite(Path.Combine(_directory, name), keyProvider: null, enforceForeignKeys: false);

        if (interceptor is not null)
        {
            builder = builder.AddInterceptors(interceptor);
        }

        var options = builder.Options;

        // No store: this suite is about triggers, which the store filter does
        // not touch.
        return new WaymarkDbContext(
            options,
            new FixedCurrentStore(null),
            new FixedLedgerCurrency(Currency.Dzd));
    }

    /// <summary>
    /// Counts the transactions EF is asked to commit. Applying each trigger on its own leaves
    /// this at zero, because SQLite commits every loose statement itself and EF never opens a
    /// transaction at all; one explicit transaction around the set leaves it at one.
    /// </summary>
    private sealed class CommitCounter : DbTransactionInterceptor
    {
        public int Commits { get; private set; }

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
            Commits++;
    }

    /// <summary>
    /// How many triggers the script declares. The literal lives in one test,
    /// <see cref="The_script_declares_exactly_the_triggers_the_schema_defines"/>, which pins what
    /// the database promises; the tests of the mechanism compare with the script, so a new trigger
    /// changes one number, on purpose, rather than every count here (F-17).
    /// </summary>
    private static readonly int Declared = TriggerScript.DeclaredNames().Count;

    private static int CountTriggers(WaymarkDbContext context) =>
        context.Database
            .SqlQueryRaw<int>("SELECT count(*) AS Value FROM sqlite_schema WHERE type = 'trigger'")
            .AsEnumerable()
            .Single();

    [Fact]
    public void The_script_declares_exactly_the_triggers_the_schema_defines()
    {
        // triggers.sql began as the 11 triggers extracted from schema_v7_1.sql,
        // which is frozen. Later additions: processing_log's UPDATE guard
        // (D-045), rounding_variance (D-034), receivable_movements (F-16), and a
        // no-replace guard on each of the nine ledgers (Phase 0 final test), and
        // erasure_ledger's four fact and time-flow guards (D-060).
        // A change to this count is a change to what the database promises.
        var declared = TriggerScript.DeclaredNames();

        Assert.Equal(29, declared.Count);
        Assert.Contains("trg_consent_events_no_update", declared);
        Assert.Contains("trg_erasure_ledger_no_delete", declared);
        Assert.Equal(declared.Count, declared.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Migrate_alone_leaves_the_database_unprotected()
    {
        // Not a bug — a fact worth pinning down. This is why the supported
        // entry point is MigrateAndApplyTriggers and why `dotnet ef database
        // update` is not a creation path.
        using var context = NewContext("migrate-only.db");
        context.Database.Migrate();

        Assert.Equal(0, CountTriggers(context));
        Assert.Equal(Declared, context.FindMissingTriggers().Count);
    }

    [Fact]
    public void MigrateAndApplyTriggers_installs_all_of_them()
    {
        using var context = NewContext("full.db");
        context.MigrateAndApplyTriggers();

        Assert.Equal(Declared, CountTriggers(context));
        Assert.Empty(context.FindMissingTriggers());
    }

    [Fact]
    public void Applying_twice_changes_nothing()
    {
        // Every statement drops before it creates, so this has to be safe:
        // it runs after every migration for the life of the product.
        using var context = NewContext("twice.db");
        context.MigrateAndApplyTriggers();
        context.ApplyTriggers();
        context.ApplyTriggers();

        Assert.Equal(Declared, CountTriggers(context));
        Assert.Empty(context.FindMissingTriggers());
    }

    [Fact]
    public void Re_applying_restores_a_trigger_a_rebuild_would_have_dropped()
    {
        // The scenario this whole mechanism exists for, simulated directly.
        using var context = NewContext("restore.db");
        context.MigrateAndApplyTriggers();

#pragma warning disable EF1002 // A constant, not input.
        context.Database.ExecuteSqlRaw("DROP TRIGGER trg_consent_events_no_delete");
#pragma warning restore EF1002

        Assert.Equal(
            "trg_consent_events_no_delete",
            Assert.Single(context.FindMissingTriggers()));

        context.ApplyTriggers();

        Assert.Empty(context.FindMissingTriggers());
        Assert.Equal(Declared, CountTriggers(context));
    }

    // ------------------------------------------- one transaction (F-16)

    [Fact]
    public void The_whole_set_is_applied_in_one_transaction()
    {
        // Triggers applied one statement at a time are one durable commit each.
        // That is invisible on an SSD and cost a CI run 90 seconds of startup on
        // a slow disk, on a path that runs at every StoreServer start. The guard
        // is that ApplyTriggers opens exactly one transaction and commits once.
        var counter = new CommitCounter();
        using var context = NewContext("one-transaction.db", counter);
        context.Database.Migrate();
        var afterMigrate = counter.Commits;

        context.ApplyTriggers();

        Assert.Equal(afterMigrate + 1, counter.Commits);
        Assert.Equal(Declared, CountTriggers(context));
    }

    [Fact]
    public void A_caller_that_already_has_a_transaction_keeps_it()
    {
        // The fixtures call ApplyTriggers directly, and a caller may be inside a
        // transaction of its own. Opening a second one would throw, so the
        // method joins rather than begins, and does not commit what it did not
        // start.
        using var context = NewContext("ambient.db");
        context.Database.Migrate();

        using var transaction = context.Database.BeginTransaction();
        context.ApplyTriggers();

        Assert.NotNull(context.Database.CurrentTransaction);
        transaction.Commit();

        Assert.Equal(Declared, CountTriggers(context));
        Assert.Empty(context.FindMissingTriggers());
    }

    [Fact]
    public void A_rolled_back_caller_leaves_no_triggers_behind()
    {
        // The atomicity the single transaction buys: all of the guards or none,
        // never a database carrying half its append-only protection while
        // looking migrated.
        using var context = NewContext("rollback.db");
        context.Database.Migrate();

        using (var transaction = context.Database.BeginTransaction())
        {
            context.ApplyTriggers();
            transaction.Rollback();
        }

        Assert.Equal(0, CountTriggers(context));
    }

    [Fact]
    public void A_restored_trigger_actually_refuses_the_write()
    {
        // Present is not the same as working. After the drop-and-restore, the
        // guard has to bite, not merely appear in sqlite_schema.
        using var context = NewContext("bites.db");
        context.MigrateAndApplyTriggers();

#pragma warning disable EF1002
        context.Database.ExecuteSqlRaw("DROP TRIGGER trg_consent_events_no_delete");
        context.ApplyTriggers();

        context.Database.ExecuteSqlRaw("""
            INSERT INTO consent_events
                (consent_event_id, customer_id, occurred_at, action, consent_type, notice_version, method)
            VALUES ('01AFTER', '01CUST', '2026-01-01 00:00:00', 'granted', 'marketing', 'v1', 'verbal')
            """);

        var error = Assert.Throws<SqliteException>(() =>
            context.Database.ExecuteSqlRaw("DELETE FROM consent_events WHERE customer_id = '01CUST'"));
#pragma warning restore EF1002

        Assert.Contains("append-only", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrateAndApplyTriggers_refuses_inside_a_transaction()
    {
        using var context = NewContext("in-transaction.db");
        using var transaction = context.Database.BeginTransaction();

        var error = Assert.Throws<InvalidOperationException>(context.MigrateAndApplyTriggers);

        Assert.Contains("cannot run inside a transaction", error.Message, StringComparison.Ordinal);
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
}
