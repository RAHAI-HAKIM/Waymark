// Waymark.StoreServer — the authoritative process on the store premises.
//
// Phase 0. It owns the operational database, keeps it encrypted, and brings it up to date at
// start; the API the POS calls arrives in Phase 1.

using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Waymark.Application.IdGenerator;
using Waymark.Domain;
using Waymark.Domain.Ids;
using Waymark.Domain.Privacy;
using Waymark.Persistence;
using Waymark.Pseudonymisation;

// The till is Windows (D-017), and the keys are wrapped with DPAPI, which exists nowhere else.
[assembly: SupportedOSPlatform("windows")]

if (!OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException("StoreServer runs on Windows: its keys are wrapped with DPAPI (D-042).");
}

var builder = WebApplication.CreateBuilder(args);

// The paths are configuration, not constants: %ProgramData%\Waymark\… on a till, a temporary
// directory under test (D-013). A host that hardcoded them could not be pointed anywhere else.
var dataDirectory = builder.Configuration[WaymarkStoragePaths.DataDirectorySetting]
    ?? WaymarkStoragePaths.DefaultDataDirectory;
var keysDirectory = builder.Configuration[WaymarkStoragePaths.KeysDirectorySetting]
    ?? WaymarkStoragePaths.DefaultKeysDirectory;
var importFrom = builder.Configuration[WaymarkStoragePaths.ImportPlaintextSetting];

WaymarkStoragePaths.EnsureDataDirectory(dataDirectory);
var databasePath = WaymarkStoragePaths.StoreDatabase(dataDirectory);

// Which store this till is. Unset until a store row exists, and unset means
// store-scoped tables read as empty rather than as everything — a tenancy
// filter that opens up when unconfigured fails silently, and one that returns
// nothing fails in the first minute (CLAUDE.md §3.3, DPIA risk R9).
builder.Services.AddSingleton<ICurrentStore>(
    new FixedCurrentStore(builder.Configuration["Waymark:Store:StoreId"]));

// The currency the books are kept in. Money columns are a bare INTEGER count of
// minor units and most carry no currency column at all, so the value converter
// has to get it from outside the row (D-035). Set at commissioning; changing it
// would reinterpret every historical row.
builder.Services.AddSingleton<ILedgerCurrency>(
    FixedLedgerCurrency.FromCode(builder.Configuration["Waymark:Store:Currency"]));

// Ids are minted here, never inside an entity constructor. A singleton because
// Ulid.NewUlid() is thread-safe; the seeded implementation is for the synthetic
// store generator and for tests, and is deliberately not registered (D-038).
builder.Services.AddSingleton<IIdGenerator, UlidGenerator>();

// The database key (D-042, F-1). Resolved only inside the startup block below, after the keys
// directory has passed its check. A new key is made only when there is no database yet: an
// existing file with no key is a restore gone wrong, and a fresh key would hide it.
builder.Services.AddSingleton<IDatabaseKeyProvider>(_ => File.Exists(databasePath)
    ? DatabaseKeyStore.Open(keysDirectory, new DpapiKeyProtector())
    : DatabaseKeyStore.OpenOrCreate(keysDirectory, new DpapiKeyProtector()));

builder.Services.AddDbContext<WaymarkDbContext>((services, options) =>
    // UseWaymarkSqlite, never UseSqlite: the latter omits the STRICT generator
    // and tables created without it accept a REAL into a money column. The key
    // is issued on every connection, never through the connection string.
    options.UseWaymarkSqlite(databasePath, services.GetRequiredService<IDatabaseKeyProvider>()));

var app = builder.Build();

// Before anything is served: the keys are protected, the database is encrypted and up to date.
//
// Each check refuses to start rather than warn. A till that will not start is a phone call; a
// till whose key any cashier can read, or whose audit tables quietly accept edits, is a finding.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Waymark.StoreServer.Startup");

    // O-18. LocalMachine DPAPI unwraps for any process on the machine, so the directory's ACL is
    // what keeps a cashier's account from the keys. Created locked down if absent (the installer's
    // job, until there is an installer); never repaired if present, only refused.
    KeysDirectoryAccess.EnsureCreated(keysDirectory);
    var aclProblems = KeysDirectoryAccess.Problems(keysDirectory);
    if (aclProblems.Count > 0)
    {
        var problems = string.Join(" ", aclProblems);
        logger.LogCritical("The keys directory is not protected: {Problems}", problems);
        throw new InvalidOperationException(
            $"StoreServer will not start: the keys directory is not protected. {problems} "
            + "Recreate it with inheritance disabled and access for SYSTEM, Administrators and the service account only (O-18).");
    }

    // A plaintext store (a generated one, or one from before F-1) is never opened in place, and
    // is refused before any key is read or made for it.
    if (WaymarkDatabaseEncryption.IsPlaintext(databasePath))
    {
        throw new InvalidOperationException(
            $"{databasePath} is not encrypted. StoreServer does not open a plaintext store (F-1). Move it "
            + $"aside and set {WaymarkStoragePaths.ImportPlaintextSetting} to its path to import it.");
    }

    var keyProvider = scope.ServiceProvider.GetRequiredService<IDatabaseKeyProvider>();

    // It is imported instead: an encrypted copy, made when there is no store yet.
    if (importFrom is not null && !File.Exists(databasePath))
    {
        logger.LogInformation("Importing the plaintext store at {Source} into an encrypted database", importFrom);
        WaymarkDatabaseEncryption.EncryptCopy(importFrom, databasePath, keyProvider);
    }

    var database = scope.ServiceProvider.GetRequiredService<WaymarkDbContext>();
    logger.LogInformation("Opening the store database at {Path}", databasePath);

    // MigrateAndApplyTriggers throws if an append-only trigger is missing
    // afterwards. Not inside a transaction, and not `Database.Migrate()` on its
    // own — see CLAUDE.md §3.7.
    database.MigrateAndApplyTriggers();

    var triggerCount = TriggerScript.DeclaredNames().Count;
    logger.LogInformation("Database ready and encrypted: {Triggers} append-only triggers in place", triggerCount);

    // The ledger currency is configuration; the store row is the record. If they
    // disagree, every money column has just been read back with the wrong label
    // — the integer is identical and nothing else would notice.
    var ledger = scope.ServiceProvider.GetRequiredService<ILedgerCurrency>();
    var storeCurrency = database.Stores.IgnoreQueryFilters()
        .Select(store => store.Currency)
        .FirstOrDefault();

    if (storeCurrency is not null && !string.Equals(storeCurrency, ledger.Currency.Code, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"Waymark:Store:Currency is '{ledger.Currency.Code}' but the store row says "
            + $"'{storeCurrency}'. Every money column would be read back in the wrong "
            + "currency, with the right number. Fix the configuration rather than the row.");
    }

    logger.LogInformation("Ledger currency: {Currency}", ledger.Currency.Code);
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
