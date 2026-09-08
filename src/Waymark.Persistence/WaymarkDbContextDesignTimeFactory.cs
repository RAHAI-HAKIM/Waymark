using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Waymark.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build a context without running the application.
///
/// <para>
/// The EF tools normally construct a context by asking the startup project's
/// dependency injection container. <c>Waymark.StoreServer</c> registers nothing
/// yet, so without this every <c>dotnet ef</c> command fails with "Unable to
/// create a DbContext". EF prefers this factory when it finds one.
/// </para>
/// <para>
/// The path here is used for design-time work only — generating and scripting
/// migrations, which never open the file. It is deliberately a scratch path
/// rather than the real one from D-013, so a mistyped command cannot touch a
/// store's database.
/// </para>
/// </summary>
public sealed class WaymarkDbContextDesignTimeFactory : IDesignTimeDbContextFactory<WaymarkDbContext>
{
    public WaymarkDbContext CreateDbContext(string[] args)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "waymark-design-time", "waymark-store.db");

        // SQLite will not create a missing directory, and "unable to open
        // database file" does not say so.
        Directory.CreateDirectory(Path.GetDirectoryName(scratch)!);

        var options = new DbContextOptionsBuilder<WaymarkDbContext>()
            .UseWaymarkSqlite(scratch)
            .Options;

        return new WaymarkDbContext(options);
    }
}
