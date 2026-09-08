using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Waymark.Persistence;

/// <summary>
/// The one supported way to point a <see cref="WaymarkDbContext"/> at a file.
///
/// <para>
/// It exists because <c>UseSqlite</c> on its own is quietly wrong here. EF Core
/// emits no <c>STRICT</c>, so a context configured without
/// <see cref="StrictSqliteMigrationsSqlGenerator"/> produces migrations whose
/// tables accept a REAL into a money column — the build green, the tests green
/// (decisions.md D-016, cost 3).
/// </para>
/// <para>
/// This was not hypothetical. The first version of
/// <c>ModelMatchesSchemaTests</c> called <c>UseSqlite</c> directly and every
/// one of the 58 tables came back non-STRICT, while the design-time factory —
/// which did register the generator — produced them correctly. Two ways to
/// configure the same context, one of them silently wrong, is the thing this
/// method removes.
/// </para>
/// </summary>
public static class WaymarkSqliteOptionsExtensions
{
    /// <summary>
    /// Configures SQLite for the operational store database. Always use this
    /// rather than <c>UseSqlite</c>.
    /// </summary>
    /// <param name="builder">The options builder being configured.</param>
    /// <param name="databasePath">
    /// Full path to <c>waymark-store.db</c>. On a till this comes from
    /// configuration, never a constant (D-013).
    /// </param>
    /// <param name="enforceForeignKeys">
    /// Foreign key enforcement is per connection in SQLite and off by default.
    /// Tests that exercise one table in isolation turn it off deliberately.
    /// </param>
    public static DbContextOptionsBuilder UseWaymarkSqlite(
        this DbContextOptionsBuilder builder,
        string databasePath,
        bool enforceForeignKeys = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .UseSqlite($"Data Source={databasePath};Foreign Keys={enforceForeignKeys}")
            .ReplaceService<IMigrationsSqlGenerator, StrictSqliteMigrationsSqlGenerator>();
    }

    /// <summary>
    /// Typed overload, so <c>new DbContextOptionsBuilder&lt;WaymarkDbContext&gt;()</c>
    /// keeps its type and <c>.Options</c> still fits the context's constructor.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseWaymarkSqlite<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        string databasePath,
        bool enforceForeignKeys = true)
        where TContext : DbContext
    {
        UseWaymarkSqlite((DbContextOptionsBuilder)builder, databasePath, enforceForeignKeys);
        return builder;
    }
}
