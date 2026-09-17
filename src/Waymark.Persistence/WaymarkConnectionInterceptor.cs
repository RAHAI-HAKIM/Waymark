using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Waymark.Domain.Privacy;

namespace Waymark.Persistence;

/// <summary>
/// Prepares every connection EF Core opens to the store database, before anything else reaches
/// it: the key first, when the file is encrypted, then foreign key enforcement (F-1).
///
/// <para>
/// <b>Why here and not in the connection string.</b> Microsoft.Data.Sqlite issues
/// <c>Foreign Keys=True</c> as a statement during <c>Open</c>, and SQLCipher refuses any
/// statement that arrives before the key, so the keyword breaks an encrypted file. Both pragmas
/// are therefore issued here, in order, and the connection string carries the path alone.
/// </para>
/// <para>
/// <b>It fires only when EF opens the connection.</b> Code that takes
/// <c>GetDbConnection()</c> and calls <c>Open()</c> itself bypasses it and meets "file is not a
/// database". Open through <c>Database.OpenConnection()</c>.
/// </para>
/// </summary>
internal sealed class WaymarkConnectionInterceptor(IDatabaseKeyProvider? keyProvider, bool enforceForeignKeys)
    : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        Prepare(connection);

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Prepare(connection);
        return Task.CompletedTask;
    }

    private void Prepare(DbConnection connection)
    {
        if (keyProvider is not null)
        {
            WaymarkDatabaseEncryption.ApplyKey(connection, keyProvider);
        }

        using var command = connection.CreateCommand();
        command.CommandText = enforceForeignKeys ? "PRAGMA foreign_keys = ON;" : "PRAGMA foreign_keys = OFF;";
        command.ExecuteNonQuery();
    }
}
