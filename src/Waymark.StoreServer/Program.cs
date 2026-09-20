// Waymark.StoreServer — the authoritative process on the store premises.
//
// It owns the operational database, keeps it encrypted, and brings it up to date at start.
// The POS reaches it over HTTP only (CLAUDE.md §2.2): Phase 0.5 serves the product lookup and
// the cash sale.

using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Waymark.Application.Commands;
using Waymark.Application.IdGenerator;
using Waymark.Application.Sales;
using Waymark.Application.Time;
using Waymark.Contracts.Pos;
using Waymark.Domain;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Ids;
using Waymark.Domain.Privacy;
using Waymark.Domain.Sales;
using Waymark.Domain.Work;
using Waymark.Persistence;
using Waymark.Persistence.Catalogue;
using Waymark.Persistence.Privacy;
using Waymark.Persistence.Sales;
using Waymark.Pseudonymisation;
using Waymark.StoreServer.Catalogue;
using Waymark.StoreServer.Sales;

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

// The clock, injected everywhere time is read, so tests can hold it still.
builder.Services.AddSingleton(TimeProvider.System);

// The store's date (D-067). Its zone comes from the stores row, which is only readable once the
// database is open, so it is resolved in the startup block below and captured here. No row means
// the store is not commissioned: there are no prices to look up either, so asking is an error.
TimeZoneInfo? storeZone = null;
builder.Services.AddSingleton<IStoreCalendar>(services => new StoreCalendar(
    services.GetRequiredService<TimeProvider>(),
    storeZone ?? throw new InvalidOperationException(
        "This store has no row in stores, so it has no time zone. Commission it (or import a store) first.")));

// What the till may sell for a barcode (D-066). Scoped: one context, one request.
builder.Services.AddScoped<IProductLookup, Waymark.Persistence.Catalogue.ProductLookup>();

// Commands (D-050): one unit of work per request, which stages every row and the executor
// commits once. The same instance is the staging side and the committing side.
builder.Services.AddScoped<WaymarkUnitOfWork>();
builder.Services.AddScoped<IUnitOfWork>(services => services.GetRequiredService<WaymarkUnitOfWork>());
builder.Services.AddScoped<IStaging>(services => services.GetRequiredService<WaymarkUnitOfWork>());
builder.Services.AddScoped<IProcessingLog, ProcessingLogWriter>();
builder.Services.AddScoped<CommandExecutor>();

// Hop 2 (D-070): a cash sale.
builder.Services.AddScoped<ISalesLedger, SalesLedger>();
builder.Services.AddScoped<CompleteSaleHandler>();

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

    // The store's time zone (F-22, D-067). The stores filter returns this store's row alone. A zone
    // the map cannot resolve refuses the start: a wrong zone moves the day prices change on.
    var timezone = database.Stores.Select(store => store.Timezone).FirstOrDefault();
    if (timezone is not null)
    {
        storeZone = StoreTimeZones.Resolve(timezone);
        logger.LogInformation("Store time zone: {Zone} ({WindowsId})", timezone, storeZone.Id);
    }
}

// The POS uses this to decide whether the server is reachable before falling
// back to its Level-2 cache.
app.MapGet("/health", () => Results.Ok(new
{
    service = "Waymark.StoreServer",
    status = "up",
    checkedAt = DateTimeOffset.UtcNow
}));

// Hop 1 (D-066): what the till may sell for a barcode. Every answer is a 200, including "no such
// product" and "not sellable"; an error status only ever means the server failed, or a request
// with no code at all. The code is a query parameter, not a path segment: ASP.NET Core leaves
// "%2F" encoded in a route value, so a typed "12/34" would be looked up as "12%2F34", and decoding
// it again would double-decode every other code. A query string is decoded exactly once.
app.MapGet("/api/products/lookup", async (
    string? barcode, IProductLookup lookup, CancellationToken cancellationToken) =>
    string.IsNullOrWhiteSpace(barcode)
        ? Results.BadRequest("A barcode is required: /api/products/lookup?barcode=...")
        : Results.Ok(ProductLookupWire.ToWire(barcode, await lookup.FindForSaleAsync(barcode, cancellationToken))));

// Hop 2 (D-070): a cash sale. One at a time: the invoice number is read and staged inside the
// sale's transaction, and two sales interleaving would read the same last number (the unique
// index would then refuse the second, but as a failure rather than a sale). A refusal is an
// answer, a 200 like the lookup's; an error status only ever means the server failed.
var oneSaleAtATime = new SemaphoreSlim(1, 1);
app.MapPost("/api/sales", async (
    SaleRequest request, CommandExecutor executor, CompleteSaleHandler handler, CancellationToken cancellationToken) =>
{
    await oneSaleAtATime.WaitAsync(cancellationToken);
    try
    {
        var sale = await executor.ExecuteAsync(handler, SaleWire.ToCommand(request), cancellationToken);
        return Results.Ok(SaleWire.Completed(sale));
    }
    catch (SaleRefusedException refusal)
    {
        return Results.Ok(SaleWire.Refused(refusal.Message));
    }
    finally
    {
        oneSaleAtATime.Release();
    }
});

app.Run();
