// Waymark.StoreServer — the authoritative process on the store premises.
//
// Phase 0. It owns the operational database and brings it up to date at start;
// the API the POS calls arrives in Phase 1.

using Microsoft.EntityFrameworkCore;
using Waymark.Application.IdGenerator;
using Waymark.Domain;
using Waymark.Domain.Ids;
using Waymark.Persistence;

var builder = WebApplication.CreateBuilder(args);

// The database path is configuration, not a constant: %ProgramData%\Waymark\data
// on a till, a temporary directory under test (D-013). A host that hardcoded it
// could not be pointed anywhere else.
var dataDirectory = builder.Configuration[WaymarkStoragePaths.DataDirectorySetting]
    ?? WaymarkStoragePaths.DefaultDataDirectory;

WaymarkStoragePaths.EnsureDataDirectory(dataDirectory);

// Which store this till is. Unset until a store row exists, and unset means
// store-scoped tables read as empty rather than as everything — a tenancy
// filter that opens up when unconfigured fails silently, and one that returns
// nothing fails in the first minute (CLAUDE.md §3.3, DPIA risk R9).
builder.Services.AddSingleton<ICurrentStore>(
    new FixedCurrentStore(builder.Configuration["Waymark:Store:StoreId"]));

// Ids are minted here, never inside an entity constructor. A singleton because
// Ulid.NewUlid() is thread-safe; the seeded implementation is for the synthetic
// store generator and for tests, and is deliberately not registered (D-038).
builder.Services.AddSingleton<IIdGenerator, UlidGenerator>();

builder.Services.AddDbContext<WaymarkDbContext>(options =>
    // UseWaymarkSqlite, never UseSqlite: the latter omits the STRICT generator
    // and tables created without it accept a REAL into a money column.
    options.UseWaymarkSqlite(WaymarkStoragePaths.StoreDatabase(dataDirectory)));

var app = builder.Build();

// Bring the database up to date before anything is served.
//
// MigrateAndApplyTriggers throws if an append-only trigger is missing
// afterwards, so a store whose audit tables are unprotected fails to start
// rather than running and quietly accepting writes the DPIA says are
// impossible. A till that will not start is a phone call; a till that silently
// stopped enforcing consent history is a finding.
//
// Not inside a transaction, and not `Database.Migrate()` on its own — see
// CLAUDE.md §3.7.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Waymark.StoreServer.Startup");
    var database = scope.ServiceProvider.GetRequiredService<WaymarkDbContext>();

    // Computed before the call rather than inside it: CA1873 objects to work
    // done for a log line that may be switched off.
    var databasePath = WaymarkStoragePaths.StoreDatabase(dataDirectory);
    logger.LogInformation("Opening the store database at {Path}", databasePath);

    database.MigrateAndApplyTriggers();

    var triggerCount = TriggerScript.DeclaredNames().Count;
    logger.LogInformation("Database ready: {Triggers} append-only triggers in place", triggerCount);
}

// The POS uses this to decide whether the server is reachable before falling
// back to its Level-2 cache.
app.MapGet("/health", () => Results.Ok(new
{
    service = "Waymark.StoreServer",
    status = "up",
    checkedAt = DateTimeOffset.UtcNow
}));

app.Run();
