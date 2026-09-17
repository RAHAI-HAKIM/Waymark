using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Waymark.Domain.Privacy;

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
/// <para>
/// <b>Encryption is a required argument</b> (F-1). Every caller says which key opens the file,
/// or says <c>null</c> for a plaintext one, so a store database is never left unencrypted by
/// omission. StoreServer always passes its key; plaintext is for tests and for the synthetic
/// store generator, which may not reach the key (D-054).
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
    /// <param name="keyProvider">
    /// The database key (D-042), or null for a plaintext file. Issued as the first statement on
    /// every connection EF opens, never through the connection string.
    /// </param>
    /// <param name="enforceForeignKeys">
    /// Foreign key enforcement is per connection in SQLite and off by default.
    /// Tests that exercise one table in isolation turn it off deliberately.
    /// </param>
    public static DbContextOptionsBuilder UseWaymarkSqlite(
        this DbContextOptionsBuilder builder,
        string databasePath,
        IDatabaseKeyProvider? keyProvider,
        bool enforceForeignKeys = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .UseSqlite(ConnectionString(databasePath))
            .AddInterceptors(new WaymarkConnectionInterceptor(keyProvider, enforceForeignKeys))
            .ReplaceService<IMigrationsSqlGenerator, StrictSqliteMigrationsSqlGenerator>()
            // The money converters are built from the ledger currency, so the
            // currency has to be part of what identifies a cached model.
            .ReplaceService<IModelCacheKeyFactory, WaymarkModelCacheKeyFactory>();
    }

    /// <summary>
    /// Typed overload, so <c>new DbContextOptionsBuilder&lt;WaymarkDbContext&gt;()</c>
    /// keeps its type and <c>.Options</c> still fits the context's constructor.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseWaymarkSqlite<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        string databasePath,
        IDatabaseKeyProvider? keyProvider,
        bool enforceForeignKeys = true)
        where TContext : DbContext
    {
        UseWaymarkSqlite((DbContextOptionsBuilder)builder, databasePath, keyProvider, enforceForeignKeys);
        return builder;
    }

    /// <summary>
    /// The path, and pooling off.
    ///
    /// <para>
    /// No <c>Foreign Keys</c> keyword, which Microsoft.Data.Sqlite issues as a statement before
    /// the key can be; no <c>Password</c>, which would put the key where a log can print it.
    /// <see cref="WaymarkConnectionInterceptor"/> issues both pragmas.
    /// </para>
    /// <para>
    /// <b>No pooling</b>, because the pool is keyed by connection string and the key is not in
    /// it. A pooled handle already unlocked by one key was handed, still unlocked, to a context
    /// holding a different key or none, and a second <c>PRAGMA key</c> on it is silently ignored:
    /// the wrong key read the store. Every open is therefore a fresh handle, keyed by its own
    /// provider. The cost is a file open per EF connection, which a single till does not feel.
    /// </para>
    /// </summary>
    internal static string ConnectionString(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        return new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
    }
}
