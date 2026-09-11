using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain;
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

    private WaymarkDbContext NewContext(string name)
    {
        var options = new DbContextOptionsBuilder<WaymarkDbContext>()
            .UseWaymarkSqlite(Path.Combine(_directory, name), enforceForeignKeys: false)
            .Options;

        // No store: this suite is about triggers, which the store filter does
        // not touch.
        return new WaymarkDbContext(options, new FixedCurrentStore(null));
    }

    private static int CountTriggers(WaymarkDbContext context) =>
        context.Database
            .SqlQueryRaw<int>("SELECT count(*) AS Value FROM sqlite_schema WHERE type = 'trigger'")
            .AsEnumerable()
            .Single();

    [Fact]
    public void The_script_declares_exactly_the_triggers_the_schema_defines()
    {
        // triggers.sql was extracted from schema_v7_1.sql, which is now frozen.
        // If the two ever disagree, the extraction lost something.
        var declared = TriggerScript.DeclaredNames();

        Assert.Equal(11, declared.Count);
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
        Assert.Equal(11, context.FindMissingTriggers().Count);
    }

    [Fact]
    public void MigrateAndApplyTriggers_installs_all_of_them()
    {
        using var context = NewContext("full.db");
        context.MigrateAndApplyTriggers();

        Assert.Equal(11, CountTriggers(context));
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

        Assert.Equal(11, CountTriggers(context));
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
        Assert.Equal(11, CountTriggers(context));
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
