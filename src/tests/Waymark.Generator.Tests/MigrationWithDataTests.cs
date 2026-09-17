using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Waymark.Domain;
using Waymark.Domain.Values;
using Waymark.Persistence;
using static Waymark.Generator.Tests.Sql;

namespace Waymark.Generator.Tests;

/// <summary>
/// Migrations run against a store that holds a history (Phase 0 final test). Every other
/// migration test starts from an empty file, where a rebuild that loses rows, or leaves a
/// dangling reference, looks exactly like one that works. A generated store is the history.
/// </summary>
public sealed class MigrationWithDataTests(MiniSalesRun mini) : IClassFixture<MiniSalesRun>, IDisposable
{
    private readonly ScratchDirectory _scratch = new();

    /// <summary>Tables the down migrations remove or empty, whose rows cannot survive the round trip.</summary>
    private static readonly HashSet<string> RemovedOnTheWayDown = new(StringComparer.Ordinal)
    {
        "receivable_movements", "rounding_variance", "processing_counters", "__EFMigrationsHistory",
    };

    private string Copy()
    {
        var path = Path.Combine(_scratch.Path, Guid.NewGuid().ToString("N") + ".db");
        using (var source = new SqliteConnection($"Data Source={mini.Result.DatabasePath};Mode=ReadOnly;Pooling=False"))
        using (var target = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            source.Open();
            target.Open();
            source.BackupDatabase(target);
        }

        return path;
    }

    private static WaymarkDbContext Context(string path) => new(
        new DbContextOptionsBuilder<WaymarkDbContext>().UseWaymarkSqlite(path, keyProvider: null).Options,
        new FixedCurrentStore(null),
        new FixedLedgerCurrency(Currency.Dzd));

    private static Dictionary<string, long> Counts(string path)
    {
        using var db = Open(path);
        return Column(db, "SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name")
            .ToDictionary(table => table, table => Scalar(db, $"SELECT count(*) FROM \"{table}\""), StringComparer.Ordinal);
    }

    [Fact]
    public void Migrating_a_store_that_is_already_current_changes_no_row()
    {
        var path = Copy();
        var before = Counts(path);

        using (var context = Context(path))
        {
            context.MigrateAndApplyTriggers();
        }

        Assert.Equal(before, Counts(path));
    }

    [Fact]
    public void A_rollback_the_data_cannot_fit_fails_and_leaves_the_store_as_it_was()
    {
        // The mini store refunded a sale to the tab, which the schema before AddReceivables
        // cannot hold. Refusing is right; what matters is that the refusal changes nothing.
        var path = Copy();
        var before = Counts(path);
        using (var db = Open(path))
        {
            Assert.True(Scalar(db, "SELECT count(*) FROM returns WHERE refund_method = 'on_account'") > 0);
        }

        List<string> migrations;
        int receivables;
        using (var context = Context(path))
        {
            migrations = [.. context.Database.GetMigrations()];
            receivables = migrations.FindIndex(m => m.EndsWith("_AddReceivables", StringComparison.Ordinal));
            Assert.ThrowsAny<SqliteException>(() => context.Database.GetService<IMigrator>().Migrate(migrations[receivables - 1]));
        }

        // EF rolls back one migration at a time: the ones after AddReceivables fit the data and
        // went; AddReceivables itself refused, and is still applied, whole.
        using (var context = Context(path))
        {
            Assert.Equal(migrations.Skip(receivables + 1), context.Database.GetPendingMigrations());
            Assert.Contains(migrations[receivables], context.Database.GetAppliedMigrations());
            context.MigrateAndApplyTriggers();
            Assert.Empty(context.FindMissingTriggers());
        }

        Assert.Equal(before, Counts(path));
        AssertIntact(path, before, "after the refused rollback");
    }

    [Fact]
    public void Every_migration_rolls_back_and_forward_over_a_real_history_without_losing_a_row()
    {
        var path = Copy();

        // A history the oldest schema can hold: no refund to the tab.
        using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE returns SET refund_method = 'store_credit' WHERE refund_method = 'on_account'";
            command.ExecuteNonQuery();
        }

        var before = Counts(path);
        string[] migrations;

        using (var context = Context(path))
        {
            migrations = [.. context.Database.GetMigrations()];
        }

        // Down one step at a time to the baseline, checking the file each time, then back up.
        for (var i = migrations.Length - 2; i >= 0; i--)
        {
            using (var context = Context(path))
            {
                context.Database.GetService<IMigrator>().Migrate(migrations[i]);
            }

            AssertIntact(path, before, $"after rolling back to {migrations[i]}");
        }

        using (var context = Context(path))
        {
            context.MigrateAndApplyTriggers();
            Assert.Empty(context.Database.GetPendingMigrations());
            Assert.Empty(context.FindMissingTriggers());
        }

        AssertIntact(path, before, "after migrating back to head");
    }

    /// <summary>Every table that survives keeps every row; no reference dangles; the file is sound.</summary>
    private static void AssertIntact(string path, Dictionary<string, long> before, string when)
    {
        var after = Counts(path);
        foreach (var (table, count) in before)
        {
            if (RemovedOnTheWayDown.Contains(table) || !after.TryGetValue(table, out var now))
            {
                continue;
            }

            Assert.True(count == now, $"{table} had {count} rows and has {now} {when}.");
        }

        using var db = Open(path);
        Assert.Equal(["ok"], Column(db, "PRAGMA integrity_check"));
        var dangling = Column(db, "SELECT \"table\" || ' -> ' || parent FROM pragma_foreign_key_check");
        Assert.True(dangling.Count == 0, $"Dangling references {when}: {string.Join(", ", dangling.Distinct())}");
    }

    public void Dispose() => _scratch.Dispose();
}
