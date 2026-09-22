using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;

namespace Waymark.Persistence;

/// <summary>
/// Bringing a store database up to date.
///
/// <para>
/// <see cref="MigrateAndApplyTriggers"/> is the only supported way. The name is
/// deliberately blunt: <c>Migrate()</c> on its own leaves a database with no
/// append-only guards, and that failure is silent — the till works, the reports
/// look right, and <c>consent_events</c> quietly accepts an UPDATE that the
/// DPIA says is impossible. Anyone reaching for <c>Database.Migrate()</c> in a
/// codebase where this method exists should stop and wonder why.
/// </para>
/// </summary>
public static class WaymarkDatabaseExtensions
{
    /// <summary>
    /// Applies pending migrations, restores the append-only triggers, and
    /// refuses to return if any are still missing.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// If called inside a transaction, or if a trigger is absent afterwards.
    /// </exception>
    public static void MigrateAndApplyTriggers(this WaymarkDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // EF Core 9 and later start their own transaction and use an execution
        // strategy; an ambient one throws from deep inside Migrate with a
        // message about retrying execution strategies, which does not point
        // here. Say it plainly instead (CLAUDE.md §3.7).
        if (context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "MigrateAndApplyTriggers cannot run inside a transaction. EF Core "
                + "manages its own for migrations. Commit or roll back first.");
        }

        context.Database.Migrate();
        context.ApplyTriggers();

        // Every migration has run, so every table a trigger names must be here.
        // One that is not means triggers.sql is out of step with the schema —
        // a renamed table, most likely — and ApplyTriggers would have skipped
        // it without a word.
        var orphaned = context.FindTriggersWithNoTable();
        if (orphaned.Count > 0)
        {
            throw new InvalidOperationException(
                "triggers.sql names tables this database does not have, so those "
                + "triggers were skipped:\n  "
                + string.Join("\n  ", orphaned));
        }

        var missing = context.FindMissingTriggers();
        if (missing.Count > 0)
        {
            // Refusing here is the point. A database missing an append-only
            // guard is worse than one that will not open: it accepts writes
            // that the DPIA promises are impossible, and nothing downstream
            // will notice.
            throw new InvalidOperationException(
                "The database is missing append-only triggers after migration, so "
                + "audit tables are unprotected:\n  "
                + string.Join("\n  ", missing));
        }
    }

    /// <summary>
    /// Re-applies every trigger in <c>triggers.sql</c> whose table exists.
    ///
    /// <para>
    /// Idempotent, because each statement drops before it creates. Safe to run
    /// against a database that already has them and against one that has just
    /// lost them to a table rebuild.
    /// </para>
    /// <para>
    /// <b>Triggers on tables that do not exist yet are skipped, not applied.</b>
    /// A database can legitimately be part-way through its migrations — the
    /// baseline test fixture builds exactly that — and
    /// <c>CREATE TRIGGER … ON a_table_that_does_not_exist</c> is an error, not a
    /// no-op. Without this, the first ledger introduced by a later migration
    /// would break every test built on a partial database, permanently.
    /// </para>
    /// <para>
    /// This does not weaken the guarantee. <see cref="MigrateAndApplyTriggers"/>
    /// runs every migration first, so by the time it checks, every table a
    /// trigger names must exist — and it says so if one does not.
    /// </para>
    /// </summary>
    public static void ApplyTriggers(this WaymarkDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tables = ExistingTables(context);

        // One transaction for all of them, never one each (F-16). Outside a
        // transaction SQLite makes every statement its own durable commit, so
        // 29 triggers cost 29 fsyncs on an encrypted file: unmeasurable on an
        // SSD, 270-1240 ms apiece on a slow disk, and this runs on every
        // StoreServer start, not just the first. It also makes the set atomic,
        // so a crash part-way no longer leaves some append-only guards
        // installed and the rest missing.
        //
        // A caller that already has one keeps it. Migrate() must stay outside a
        // transaction (CLAUDE.md 3.7), which is why MigrateAndApplyTriggers
        // refuses an ambient one before it runs; ApplyTriggers on its own has no
        // such constraint, and the fixtures call it directly.
        var owned = context.Database.CurrentTransaction is null
            ? context.Database.BeginTransaction()
            : null;

        try
        {
            foreach (var trigger in TriggerScript.DeclaredTriggers())
            {
                if (!tables.Contains(trigger.Table))
                {
                    continue;
                }

#pragma warning disable EF1002 // The script is an embedded constant, not input.
                context.Database.ExecuteSqlRaw(trigger.Sql);
#pragma warning restore EF1002
            }

            owned?.Commit();
        }
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>
    /// Tables a trigger names that the database does not have. After every
    /// migration has run this must be empty; anything in it is a trigger left
    /// behind by a renamed or dropped table, which would otherwise be skipped
    /// in silence.
    /// </summary>
    public static IReadOnlyList<string> FindTriggersWithNoTable(this WaymarkDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tables = ExistingTables(context);

        return [.. TriggerScript.DeclaredTriggers()
            .Where(trigger => !tables.Contains(trigger.Table))
            .Select(trigger => $"{trigger.Name} (no table '{trigger.Table}')")
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Triggers that <c>triggers.sql</c> declares but the database does not
    /// have. Empty is the only acceptable answer on a live store.
    /// </summary>
    public static IReadOnlyList<string> FindMissingTriggers(this WaymarkDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var present = Names(context, "SELECT name FROM sqlite_schema WHERE type = 'trigger'");

        // A trigger whose table is not here yet is not missing; it is not due.
        // FindTriggersWithNoTable is what reports those, and only after a full
        // migration is that a fault rather than a partial database.
        var tables = ExistingTables(context);

        return [.. TriggerScript.DeclaredTriggers()
            .Where(trigger => tables.Contains(trigger.Table))
            .Where(trigger => !present.Contains(trigger.Name))
            .Select(trigger => trigger.Name)];
    }

    private static HashSet<string> ExistingTables(WaymarkDbContext context) =>
        Names(context, "SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%'");

    /// <summary>
    /// The first column of <paramref name="sql"/>, on a connection EF opened.
    ///
    /// <para>
    /// Through <c>Database.OpenConnection()</c>, never <c>GetDbConnection().Open()</c>: only an
    /// open EF performs runs the connection interceptor, and a connection opened any other way
    /// reaches an encrypted file without its key (F-1). EF counts the opens, so a connection the
    /// caller already holds open stays open.
    /// </para>
    /// </summary>
    private static HashSet<string> Names(WaymarkDbContext context, string sql)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
        }
        finally
        {
            context.Database.CloseConnection();
        }

        return names;
    }
}
