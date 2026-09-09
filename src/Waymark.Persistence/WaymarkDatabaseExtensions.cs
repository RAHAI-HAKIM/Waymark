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
    /// Re-applies every trigger in <c>triggers.sql</c>.
    ///
    /// <para>
    /// Idempotent, because each statement drops before it creates. Safe to run
    /// against a database that already has them and against one that has just
    /// lost them to a table rebuild.
    /// </para>
    /// </summary>
    public static void ApplyTriggers(this WaymarkDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

#pragma warning disable EF1002 // The script is an embedded constant, not input.
        context.Database.ExecuteSqlRaw(TriggerScript.Read());
#pragma warning restore EF1002
    }

    /// <summary>
    /// Triggers that <c>triggers.sql</c> declares but the database does not
    /// have. Empty is the only acceptable answer on a live store.
    /// </summary>
    public static IReadOnlyList<string> FindMissingTriggers(this WaymarkDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var present = new HashSet<string>(StringComparer.Ordinal);

        var connection = context.Database.GetDbConnection();
        var opened = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
            opened = true;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'trigger'";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                present.Add(reader.GetString(0));
            }
        }
        finally
        {
            if (opened)
            {
                connection.Close();
            }
        }

        return TriggerScript.DeclaredNames()
            .Where(name => !present.Contains(name))
            .ToList();
    }
}
