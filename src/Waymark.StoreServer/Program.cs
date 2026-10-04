// Waymark.StoreServer — the authoritative process on the store premises.
//
// It owns the operational database, keeps it encrypted, and brings it up to date at start.
// The POS reaches it over HTTP only (CLAUDE.md §2.2): Phase 0.5 serves the product lookup and
// the cash sale.

using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Waymark.Application.Commands;
using Waymark.Application.Engine;
using Waymark.Application.IdGenerator;
using Waymark.Application.Organisation;
using Waymark.Application.Sales;
using Waymark.Application.Statistics;
using Waymark.Application.Sync;
using Waymark.Application.Time;
using Waymark.Contracts.Pos;
using Waymark.Contracts.Recommendations;
using Waymark.Domain;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;
using Waymark.Domain.Engine;
using Waymark.Domain.Enums;
using Waymark.Domain.Ids;
using Waymark.Domain.Privacy;
using Waymark.Domain.Sales;
using Waymark.Domain.Statistics;
using Waymark.Domain.Sync;
using Waymark.Domain.Work;
using Waymark.Persistence;
using Waymark.Persistence.Catalogue;
using Waymark.Persistence.Engine;
using Waymark.Persistence.Privacy;
using Waymark.Persistence.Sales;
using Waymark.Persistence.Sync;
using Waymark.Pseudonymisation;
using Waymark.StoreServer.Catalogue;
using Waymark.StoreServer.Customers;
using Waymark.StoreServer.Organisation;
using Waymark.StoreServer.Reference;
using Waymark.StoreServer.Engine;
using Waymark.StoreServer.Sales;
using Waymark.StoreServer.Security;

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

// B1 (D-088): the name search, answered as a scan of each result would be; and the sales already
// made, read back for the "Tickets" list.
builder.Services.AddScoped<IProductSearch, Waymark.Persistence.Catalogue.ProductLookup>();
builder.Services.AddScoped<IPastTickets, Waymark.Persistence.Sales.PastTickets>();
builder.Services.AddScoped<IRefundLedger, Waymark.Persistence.Sales.RefundLedger>();

// The reasons the shop accepts for a discount, a void, a cash movement (A3). A read, like
// the lookup, and scoped for the same reason.
builder.Services.AddScoped<IReasonCodes, Waymark.Persistence.Reference.ReasonCodes>();

// Who and where the till is, for its top bar (A4). A read, scoped like the others.
builder.Services.AddScoped<ITillDirectory, Waymark.Persistence.Organisation.TillDirectory>();

// Sign-in (A5, D-083). The hasher is here, in the host, because nothing else may reach
// System.Security.Cryptography; the sessions are one per server, in memory, and a restart ends
// them all. The credentials read is scoped like every other read.
builder.Services.AddSingleton<IPinHasher, Argon2PinHasher>();
builder.Services.AddSingleton<TillSessions>();
builder.Services.AddScoped<IStaffCredentials, Waymark.Persistence.Organisation.StaffCredentials>();
builder.Services.AddScoped<SetStaffPinHandler>();

// How the store's scale labels are read (B3, D-090), set by --scale-format= until H2.
builder.Services.AddScoped<IStoreSettings, Waymark.Persistence.Organisation.StoreSettings>();
builder.Services.AddScoped<SetScaleLabelFormatHandler>();

// B7 (D-096): the tenant's settings in system_config, the customers and their tabs. The
// pseudonymiser names a customer in processing_log (D-045, D-061): the tenant key is opened, or made
// the first time, from the keys directory, which the startup checks below have passed by then.
builder.Services.AddScoped<ITenantConfiguration, Waymark.Persistence.Organisation.TenantConfigurationStore>();
builder.Services.AddScoped<SetTenantSettingsHandler>();
builder.Services.AddScoped<PublishInformationNoticeHandler>();
builder.Services.AddScoped<Waymark.Domain.Customers.ICustomerLedger, Waymark.Persistence.Customers.CustomerLedger>();
builder.Services.AddSingleton<IPseudonymiser>(_ => TenantKeyStore.OpenOrCreate(keysDirectory, new DpapiKeyProtector()));
builder.Services.AddScoped<Waymark.Application.Customers.TabCharges>();
builder.Services.AddScoped<Waymark.Application.Customers.FindCustomersHandler>();
builder.Services.AddScoped<Waymark.Application.Customers.CreateCustomerHandler>();
builder.Services.AddScoped<Waymark.Application.Customers.OpenTabHandler>();
builder.Services.AddScoped<Waymark.Application.Customers.ChangeCreditLimitHandler>();
builder.Services.AddScoped<Waymark.Application.Customers.RepayTabHandler>();

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
builder.Services.AddScoped<VoidTicketHandler>();
builder.Services.AddScoped<RefundSaleHandler>();
builder.Services.AddScoped<RecordCashMovementHandler>();
builder.Services.AddScoped<ToggleClockHandler>();
builder.Services.AddScoped<IShiftLedger, Waymark.Persistence.Organisation.ShiftLedger>();

// Hop 3 (D-072): the sale's anonymous basket goes to the outbox in the same transaction.
builder.Services.AddScoped<IOutboxSequence, OutboxSequence>();

// Hop 4 (D-065): statistics tier 2, stubbed in Phase 0.5 — the port is wired, the writer
// keeps nothing. Phase 2 writes the real DuckDB one, encrypted (O-23).
builder.Services.AddSingleton<ITier2Writer, NullTier2Writer>();

// Hop 6 (D-073): the expiry evaluator. It compares and nothing more (CLAUDE.md §5).
builder.Services.AddScoped<IExpiryLedger, ExpiryLedger>();
builder.Services.AddScoped<EvaluateExpiryHandler>();

// Hop 7 (D-074): the recommendation board and its decisions — the Integration Layer's store
// half. The role check itself is CardAudience, in Domain, and both the board and the decision
// go through the same copy of it.
builder.Services.AddScoped<IRecommendationBoard, RecommendationBoard>();
builder.Services.AddScoped<PendingCards>();
builder.Services.AddScoped<DecideRecommendationHandler>();

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

        // An approval stands for the store's day (D-105): the session store learns the zone here.
        app.Services.GetRequiredService<TillSessions>().Zone = storeZone;
        logger.LogInformation("Store time zone: {Zone} ({WindowsId})", timezone, storeZone.Id);
    }

    // The engine parameters a store needs before the engine has ever run for it (D-069, D-073).
    // Installed once and never repaired: a window the engine or a shopkeeper has since set is
    // theirs, and overwriting it every start would quietly undo their decision.
    var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
    if (ColdStartParameters.EnsureNearExpiryWindow(database, clock.GetUtcNow()))
    {
        logger.LogInformation(
            "Installed the cold-start near-expiry window: {Days} days for every category (a placeholder, D-069)",
            ColdStartParameters.NearExpiryWindowDays);
    }
}

// --set-pin=<staffId> (A5, D-083): set one person's PIN on the store just opened, and exit. After
// the startup checks, so it writes only to an encrypted, migrated store; before anything is served.
if (app.Configuration[SetPinSwitch.Setting] is { } pinFor)
{
    using var scope = app.Services.CreateScope();
    return await SetPinSwitch.RunAsync(
        pinFor,
        SetPinSwitch.ReadHidden,
        Console.Out,
        scope.ServiceProvider.GetRequiredService<CommandExecutor>(),
        scope.ServiceProvider.GetRequiredService<SetStaffPinHandler>());
}

// --scale-format=<preset or mask> (B3, D-090): how this store's scale labels are read, then exit.
// After the startup checks, like --set-pin; "list" prints the presets.
if (app.Configuration[ScaleFormatSwitch.Setting] is { } scaleFormat)
{
    using var scope = app.Services.CreateScope();
    return await ScaleFormatSwitch.RunAsync(
        scaleFormat,
        Console.Out,
        scope.ServiceProvider.GetRequiredService<CommandExecutor>(),
        scope.ServiceProvider.GetRequiredService<SetScaleLabelFormatHandler>());
}

// --customer-module, --max-credit-limit, --credit-overdue-days, --tab-as-part and
// --publish-information-notice (B7, D-096): the tenant's settings, then exit. After the startup
// checks, like --scale-format.
if (TenantSwitch.Asked(app.Configuration))
{
    using var scope = app.Services.CreateScope();
    return await TenantSwitch.RunAsync(
        app.Configuration,
        Console.Out,
        scope.ServiceProvider.GetRequiredService<CommandExecutor>(),
        scope.ServiceProvider.GetRequiredService<SetTenantSettingsHandler>(),
        scope.ServiceProvider.GetRequiredService<PublishInformationNoticeHandler>());
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
//
// B3 (D-090): `weight`, for a product sold by weight whose weight was typed, is decimal text in its
// selling unit, "0.556". A scale label needs none: its code carries its weight or price.
app.MapGet("/api/products/lookup", async (
    string? barcode, string? weight, IProductLookup lookup, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(barcode))
    {
        return Results.BadRequest("A barcode is required: /api/products/lookup?barcode=...");
    }

    long? thousandths = null;
    if (weight is not null)
    {
        if (!Waymark.StoreServer.WireText.TryThousandths(weight, out var typed))
        {
            return Results.BadRequest("A weight is decimal text in the selling unit: weight=0.556");
        }

        thousandths = typed;
    }

    return Results.Ok(ProductLookupWire.ToWire(barcode, await lookup.FindForSaleAsync(barcode, thousandths, cancellationToken)));
});

// B1 (D-088): products by name. Fewer than two characters is an empty answer, not an error: the
// till asks as the cashier types, and the first letter is not a mistake.
app.MapGet("/api/products/search", async (string? q, IProductSearch search, CancellationToken cancellationToken) =>
{
    var query = q?.Trim() ?? string.Empty;
    var hits = query.Length < NameSearch.MinimumLength ? [] : await search.SearchAsync(query, cancellationToken);
    return Results.Ok(ProductLookupWire.Search(query, hits));
});

// B1 (D-088): the finished sales of one store day. The session header says who asks and at which
// till; today at that till is anyone's, anything else needs rank 2, and a refusal is an answer.
app.MapGet("/api/tickets", async (
    string? day, bool? all, HttpRequest http, TillSessions sessions, IRecommendationBoard staff,
    IPastTickets tickets, IStoreCalendar calendar, CancellationToken cancellationToken) =>
{
    var today = calendar.Today;
    var asked = DateOnly.TryParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.None, out var parsed) ? parsed : today;
    var allTills = all == true;

    if (sessions.Resolve(http.Headers[TillSessionHeader.Name].ToString()) is not { } session)
    {
        return Results.Ok(TicketsWire.List(TicketOutcomes.NotSignedIn, asked, allTills, []));
    }

    var terminal = allTills ? null : session.TerminalId;
    var rank = (await staff.StaffAsync(session.StaffId, cancellationToken))?.Rank;
    if (!TicketsWire.MaySee(session, rank, today, asked, terminal))
    {
        return Results.Ok(TicketsWire.List(TicketOutcomes.NotAllowed, asked, allTills, []));
    }

    var (since, until) = TicketsWire.Bounds(asked, storeZone!);
    return Results.Ok(TicketsWire.List(TicketOutcomes.Answered, asked, allTills, await tickets.ListAsync(terminal, since, until, cancellationToken)));
});

// One past ticket, by its id or its number, read-only (D-088). The rank rule is asked of the
// ticket's own day and till, once it is found: an id is not a permission.
//
// D-109: an earlier day's or another till's ticket, asked by someone below rank 2, is answered
// "pin_required"; asked again citing a manager's authorisation for ViewOtherTickets, it opens.
app.MapGet("/api/tickets/one", async (
    string? ticket, string? authorisation, HttpRequest http, TillSessions sessions, IRecommendationBoard staff,
    IPastTickets tickets, IStoreCalendar calendar, ITenantConfiguration configuration, CancellationToken cancellationToken) =>
{
    var token = http.Headers[TillSessionHeader.Name].ToString();
    if (sessions.Resolve(token) is not { } session)
    {
        return Results.Ok(TicketsWire.Refused(TicketOutcomes.NotSignedIn));
    }

    if (string.IsNullOrWhiteSpace(ticket) || await tickets.FindAsync(ticket.Trim(), cancellationToken) is not { } found)
    {
        return Results.Ok(TicketsWire.Refused(TicketOutcomes.Unknown));
    }

    var rank = (await staff.StaffAsync(session.StaffId, cancellationToken))?.Rank;
    var day = TicketsWire.DayOf(found.OccurredAt, storeZone!);
    return Results.Ok(
        TicketsWire.MaySee(session, rank, calendar.Today, day, found.TerminalId)
        || sessions.AuthorisedBy(token, authorisation, Capability.ViewOtherTickets) is not null
            ? TicketsWire.Found(found, (await configuration.CurrentAsync(cancellationToken)).CustomerModule)
            : TicketsWire.Refused(TicketOutcomes.PinRequired));
});

// Session A4: the store, the till and the person selling, for the till's top bar. A terminal this
// store does not have is an answer ("unknown_terminal"), never another store's name: the global
// filter narrows the read to this store.
app.MapGet("/api/till/context", async (
    string? terminal, string? staff, ITillDirectory directory, ITenantConfiguration configuration, CancellationToken cancellationToken) =>
    string.IsNullOrWhiteSpace(terminal)
        ? Results.BadRequest("A terminal is required: /api/till/context?terminal=...&staff=...")
        : Results.Ok(TillContextWire.ToWire(
            await directory.DescribeAsync(terminal, staff, cancellationToken), await configuration.CurrentAsync(cancellationToken))));

// Session A5 (D-083): who may open this till. Names and roles, and whether a PIN is set; never a hash.
//
// With "may", who may approve that capability, as the shop raised it: the manager step lists the
// people whose PIN would be accepted, not every cashier (block B review). The rank is asked of
// StaffPermissions here, and never leaves the server.
app.MapGet("/api/till/staff", async (
    string? may, IStaffCredentials credentials, ITenantConfiguration configuration, CancellationToken cancellationToken) =>
{
    var candidates = await credentials.CandidatesAsync(cancellationToken);
    if (TillStaffWire.Capability(may) is { } capability)
    {
        var settings = await configuration.CurrentAsync(cancellationToken);
        candidates = [.. candidates.Where(candidate => TillStaffWire.MayApprove(candidate.Rank, capability, settings))];
    }

    return Results.Ok(new TillStaff([.. candidates.Select(candidate =>
        new TillStaffMember(candidate.StaffId, candidate.StaffName, candidate.RoleLabelFr, candidate.RoleLabelAr, candidate.HasPin))]));
});

// A PIN typed at a till. Every outcome is a 200 with its answer; the token in a "signed_in" one is
// what the till sends with each sale from now on.
app.MapPost("/api/till/sign-in", async (
    SignInRequest request, TillSessions sessions, ITillDirectory directory, IStaffCredentials credentials,
    CancellationToken cancellationToken) =>
{
    var terminalKnown = !string.IsNullOrWhiteSpace(request.TerminalId)
        && await directory.DescribeAsync(request.TerminalId, null, cancellationToken) is not null;

    return Results.Ok(await sessions.SignInAsync(request, terminalKnown, credentials, cancellationToken));
});

// B4 (D-091): may the seller do this, or does the manager whose PIN is typed? The rank is asked
// here, through StaffPermissions, never at the till; the PIN is checked with sign-in's lockout.
app.MapPost("/api/till/authorise", async (
    AuthoriseRequest request, HttpRequest http, TillSessions sessions, IStaffCredentials credentials,
    IRecommendationBoard staff, ITenantConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (TillStaffWire.Capability(request.Capability) is not { } capability)
    {
        return Results.Ok(new AuthoriseAnswer(AuthoriseOutcomes.UnknownCapability, null, null, null, null));
    }

    var answer = await sessions.AuthoriseAsync(
        http.Headers[TillSessionHeader.Name].ToString(),
        request,
        capability,
        credentials,
        async id => (await staff.StaffAsync(id, cancellationToken))?.Rank,
        // A refund and a paid-out need what the shop raised them to (B9, B10); everything else, the ladder alone.
        TillStaffWire.RaisedTo(capability, await configuration.CurrentAsync(cancellationToken)),
        cancellationToken);
    return Results.Ok(answer);
});

// Ends the session the header names. Always a 204: a token the server does not hold is already
// signed out, and saying so would tell a guesser which tokens exist.
app.MapPost("/api/till/sign-out", (HttpRequest http, TillSessions sessions) =>
{
    sessions.SignOut(http.Headers[TillSessionHeader.Name].ToString());
    return Results.NoContent();
});

// Session A3: the reasons this shop accepts for one kind of action. Every column that records
// *why* something happened is a foreign key into reason_codes, so this list is not decoration
// — it is the set of values the database will accept, and offering anything else fails at the
// moment of sale. A kind nobody knows is a bad request, not an empty list: "no reasons
// configured" and "no such kind" are different answers and a typo must not look like the
// first. Inactive codes never appear.
app.MapGet("/api/reason-codes", async (
    string? applies_to, IReasonCodes reasons, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(applies_to))
    {
        return Results.BadRequest("A kind is required: /api/reason-codes?applies_to=discount");
    }

    if (ReasonCodeWire.Parse(applies_to) is not { } kind)
    {
        return Results.BadRequest($"'{applies_to}' is not a kind of action reasons are recorded for.");
    }

    return Results.Ok(ReasonCodeWire.ToWire(kind, await reasons.ForAsync(kind, cancellationToken)));
});

// Hop 2 (D-070): a cash sale. One at a time: the invoice number is read and staged inside the
// sale's transaction, and two sales interleaving would read the same last number (the unique
// index would then refuse the second, but as a failure rather than a sale). A refusal is an
// answer, a 200 like the lookup's; an error status only ever means the server failed.
var oneSaleAtATime = new SemaphoreSlim(1, 1);
//
// The seller is whoever the session header's token signed in (A5, D-083); the request no longer
// names anyone. No session, or another till's, is "not_signed_in" and nothing is written.
app.MapPost("/api/sales", async (
    SaleRequest request, HttpRequest http, TillSessions sessions, IRecommendationBoard staff, CommandExecutor executor,
    CompleteSaleHandler handler, CancellationToken cancellationToken) =>
{
    var token = http.Headers[TillSessionHeader.Name].ToString();
    if (SaleWire.Seller(sessions.Resolve(token), request) is not { } seller)
    {
        return Results.Ok(SaleWire.NotSignedIn());
    }

    // D-106: whether the seller may cancel alone is asked only when a line was struck on a ticket
    // "Encaisser" had been opened on; every other sale never reads their rank.
    var sellerMayVoid = request is not { Removed.Count: > 0, PaymentOpenedAt: not null }
        || StaffPermissions.May((await staff.StaffAsync(seller, cancellationToken))?.Rank, Capability.VoidTransaction);

    await oneSaleAtATime.WaitAsync(cancellationToken);
    try
    {
        // A discount cites an authorisation given in this session (B4); who gave it is the server's.
        var command = SaleWire.ToCommand(
            request,
            seller,
            cited => sessions.AuthorisedBy(token, cited, Capability.ApplyDiscount),
            cited => sessions.AuthorisedBy(token, cited, Capability.OverridePrice),
            cited => sessions.AuthorisedBy(token, cited, Capability.ManageCredit),
            cited => sessions.AuthorisedBy(token, cited, Capability.VoidTransaction),
            sellerMayVoid);
        var sale = await executor.ExecuteAsync(handler, command, cancellationToken);
        return Results.Ok(SaleWire.Completed(sale));
    }
    catch (SaleRefusedException refusal)
    {
        return Results.Ok(SaleWire.Refused(refusal));
    }
    finally
    {
        oneSaleAtATime.Release();
    }
});

// B7 (D-096): the customers and their tabs.
app.MapCustomers();

// B8 (D-097): a ticket cancelled at the till, recorded before the till lets it go. Who cancels is the
// session's; whether they may alone is their rank's (VoidTransaction), asked here, never at the till;
// a cashier's cancel after "Encaisser" cites a manager's authorisation of this session. One at a time
// with the sales, which it shares a cash session with.
app.MapPost("/api/sales/void", async (
    VoidRequest request, HttpRequest http, TillSessions sessions, IRecommendationBoard staff, CommandExecutor executor,
    VoidTicketHandler handler, CancellationToken cancellationToken) =>
{
    var token = http.Headers[TillSessionHeader.Name].ToString();
    if (SaleWire.Seller(sessions.Resolve(token), new SaleRequest(request.TerminalId, request.Lines)) is not { } seller)
    {
        return Results.Ok(new VoidAnswer(VoidOutcomes.NotSignedIn, null, null, null));
    }

    var sellerMay = StaffPermissions.May((await staff.StaffAsync(seller, cancellationToken))?.Rank, Capability.VoidTransaction);
    var command = SaleWire.ToVoid(
        request, seller, sellerMay,
        cited => sessions.AuthorisedBy(token, cited, Capability.VoidTransaction),
        cited => sessions.AuthorisedBy(token, cited, Capability.ApplyDiscount));
    if (Voids.NeedsAuthorisation(sellerMay, request.PaymentOpenedAt is not null) && command.AuthorisedBy is null)
    {
        return Results.Ok(new VoidAnswer(VoidOutcomes.PinRequired, null, null, "This ticket was being paid: a manager authorises its cancellation."));
    }

    await oneSaleAtATime.WaitAsync(cancellationToken);
    try
    {
        return Results.Ok(SaleWire.Voided(await executor.ExecuteAsync(handler, command, cancellationToken)));
    }
    catch (SaleRefusedException refusal)
    {
        return Results.Ok(SaleWire.VoidRefused(refusal));
    }
    finally
    {
        oneSaleAtATime.Release();
    }
});

// B9 (D-098): a refund linked to its sale. Who refunds is the session's; whether they may alone is
// their rank against Refund as the tenant raised it (refund_min_rank), asked here, never at the till.
// The ticket is one the person may open (D-088's rule, asked of its own day and till). A quote writes
// nothing and needs no PIN; the refund itself is one at a time with the sales, whose number it shares.
app.MapPost("/api/sales/refund", async (
    RefundRequest request, HttpRequest http, TillSessions sessions, IRecommendationBoard staff, IPastTickets tickets,
    ITenantConfiguration configuration, IStoreCalendar calendar, CommandExecutor executor, RefundSaleHandler handler,
    CancellationToken cancellationToken) =>
{
    var token = http.Headers[TillSessionHeader.Name].ToString();
    if (sessions.Resolve(token) is not { } session || !string.Equals(session.TerminalId, request.TerminalId, StringComparison.Ordinal))
    {
        return Results.Ok(RefundWire.Refused(RefundOutcomes.NotSignedIn));
    }

    var rank = (await staff.StaffAsync(session.StaffId, cancellationToken))?.Rank;
    if (await tickets.FindAsync(request.Original, cancellationToken) is { } found
        && !TicketsWire.MaySee(session, rank, calendar.Today, TicketsWire.DayOf(found.OccurredAt, storeZone!), found.TerminalId)
        // D-109: or one a manager's PIN opened for this cashier.
        && sessions.AuthorisedBy(token, request.TicketAuthorisation, Capability.ViewOtherTickets) is null)
    {
        return Results.Ok(RefundWire.Refused(RefundOutcomes.NotAllowed, "Another day's or another till's ticket: a manager refunds it."));
    }

    var raisedTo = (await configuration.CurrentAsync(cancellationToken)).RefundMinRank;
    var sellerMay = raisedTo is null ? StaffPermissions.May(rank, Capability.Refund) : StaffPermissions.May(rank, Capability.Refund, raisedTo);
    var command = RefundWire.ToCommand(request, session.StaffId, sellerMay, cited => sessions.AuthorisedBy(token, cited, Capability.Refund));
    if (!command.Quote && !sellerMay && command.AuthorisedBy is null)
    {
        return Results.Ok(RefundWire.Refused(
            RefundOutcomes.PinRequired, "This shop asks for a manager to authorise a refund.", new Refusal(RefusalCodes.RefundNeedsManager)));
    }

    await oneSaleAtATime.WaitAsync(cancellationToken);
    try
    {
        return Results.Ok(RefundWire.Answered(await executor.ExecuteAsync(handler, command, cancellationToken)));
    }
    catch (SaleRefusedException refusal)
    {
        return Results.Ok(RefundWire.Refused(RefundOutcomes.Refused, refusal.Message, SaleWire.Coded(refusal.Code, refusal.Args)));
    }
    finally
    {
        oneSaleAtATime.Release();
    }
});

// B10 (D-102): cash into the drawer or out of it with no sale behind it. Who records it is the session's;
// a paid-out below the tenant's paid_out_min_rank cites an authorisation of this session. One at a time
// with the sales, whose cash session it shares.
app.MapPost("/api/cash/movements", async (
    CashMovementRequest request, HttpRequest http, TillSessions sessions, IRecommendationBoard staff, ITenantConfiguration configuration,
    CommandExecutor executor, RecordCashMovementHandler handler, CancellationToken cancellationToken) =>
{
    var token = http.Headers[TillSessionHeader.Name].ToString();
    if (sessions.Resolve(token) is not { } session || !string.Equals(session.TerminalId, request.TerminalId, StringComparison.Ordinal))
    {
        return Results.Ok(new CashMovementAnswer(CashMovementOutcomes.NotSignedIn));
    }

    // In or out, and nothing else: a direction nobody knows is refused, never taken as cash in.
    if (CashWire.Direction(request.Direction) is not { } direction)
    {
        return Results.Ok(new CashMovementAnswer(CashMovementOutcomes.Refused, Reason: "Cash goes in or out: 'in' or 'out'."));
    }

    var raisedTo = (await configuration.CurrentAsync(cancellationToken)).PaidOutMinRank;
    var rank = (await staff.StaffAsync(session.StaffId, cancellationToken))?.Rank;
    var sellerMay = direction == CashDirection.In
        || (raisedTo is null ? StaffPermissions.May(rank, Capability.PaidOut) : StaffPermissions.May(rank, Capability.PaidOut, raisedTo));
    var authorisedBy = sessions.AuthorisedBy(token, request.Authorisation, Capability.PaidOut);
    if (!sellerMay && authorisedBy is null)
    {
        return Results.Ok(new CashMovementAnswer(
            CashMovementOutcomes.PinRequired, Reason: "This shop asks for a manager to take cash out of the drawer.",
            Refusal: new Refusal(RefusalCodes.PaidOutNeedsManager)));
    }

    // What cannot be read is zero, which the handler refuses: never recorded as some other amount.
    var amount = Waymark.StoreServer.WireText.TryHundredths(request.Amount, out var minor) ? minor : 0;
    await oneSaleAtATime.WaitAsync(cancellationToken);
    try
    {
        var recorded = await executor.ExecuteAsync(
            handler,
            new RecordCashMovement(session.TerminalId, session.StaffId, direction, amount, request.ReasonCode, request.Note, sellerMay, authorisedBy),
            cancellationToken);
        return Results.Ok(new CashMovementAnswer(CashMovementOutcomes.Recorded, Waymark.StoreServer.WireText.Figure(recorded.Amount)));
    }
    catch (CashMovementRefusedException refusal)
    {
        return Results.Ok(new CashMovementAnswer(
            CashMovementOutcomes.Refused, Reason: refusal.Message, Refusal: SaleWire.Coded(refusal.Code, refusal.Args)));
    }
    finally
    {
        oneSaleAtATime.Release();
    }
});

// B10 (D-102): a person clocks in or out at a till with their own PIN, checked here with sign-in's
// lockout. The till must be signed in; the person clocking need not be the one selling.
app.MapPost("/api/till/clock", async (
    ClockRequest request, HttpRequest http, TillSessions sessions, IStaffCredentials credentials, CommandExecutor executor,
    ToggleClockHandler handler, CancellationToken cancellationToken) =>
{
    if (sessions.Resolve(http.Headers[TillSessionHeader.Name].ToString()) is not { } session
        || !string.Equals(session.TerminalId, request.TerminalId, StringComparison.Ordinal))
    {
        return Results.Ok(new ClockAnswer(ClockOutcomes.NotSignedIn));
    }

    var check = await sessions.VerifyPinAsync(request.StaffId, request.Pin, credentials, cancellationToken);
    if (check.Outcome != SignInOutcomes.SignedIn)
    {
        return Results.Ok(new ClockAnswer(check.Outcome, AttemptsLeft: check.AttemptsLeft, LockedUntil: check.LockedUntil));
    }

    var clocked = await executor.ExecuteAsync(handler, new ToggleClock(request.StaffId, session.TerminalId), cancellationToken);
    return Results.Ok(new ClockAnswer(clocked.In ? ClockOutcomes.ClockedIn : ClockOutcomes.ClockedOut, clocked.At, clocked.Since));
});

// Hop 6 (D-073): the expiry evaluator, run on request rather than on a schedule. The engine
// "ran last night" (CLAUDE.md §5) and a nightly job is Phase 1's; what the skeleton has to prove
// is that a comparison over real stock produces a card a human can be shown.
app.MapPost("/api/engine/expiry", async (
    CommandExecutor executor, EvaluateExpiryHandler handler, CancellationToken cancellationToken) =>
    Results.Ok(await executor.ExecuteAsync(handler, new EvaluateExpiry(), cancellationToken)));

// Hop 7 (D-074): what this staff member may act on. The staff member is named on the request
// — there is no login until Phase 1 (D-069) — and a name the store does not employ is an
// answer ("unknown_staff"), not an error. Cards above their rank are counted, never listed.
app.MapGet("/api/recommendations", async (
    string? staff, PendingCards board, CancellationToken cancellationToken) =>
    string.IsNullOrWhiteSpace(staff)
        ? Results.BadRequest("A staff member is required: /api/recommendations?staff=...")
        : Results.Ok(RecommendationWire.ToWire(await board.ForAsync(staff, cancellationToken))));

// And the one door a card leaves by. A refusal is a 200 with its reason, as everywhere else in
// the slice; an error status only ever means the server failed.
app.MapPost("/api/recommendations/decide", async (
    DecisionRequest request, CommandExecutor executor, DecideRecommendationHandler handler,
    CancellationToken cancellationToken) =>
{
    try
    {
        var command = new DecideRecommendation(
            request.RecommendationId ?? "", request.StaffId ?? "", ToDecision(request.Decision), request.OptionId);

        return Results.Ok(RecommendationWire.FromDecision(
            await executor.ExecuteAsync(handler, command, cancellationToken)));
    }
    catch (DecisionRefusedException refusal)
    {
        return Results.Ok(RecommendationWire.Refusal(refusal.Message));
    }
    catch (ArgumentException refusal)
    {
        return Results.Ok(RecommendationWire.Refusal(refusal.Message));
    }
});

app.Run();
return 0;

// Accept and dismiss only: adjust needs an amended payload and snooze a date, and neither is
// in the slice (D-069). An unknown word is refused here rather than mapped to something.
static Decision ToDecision(string? decision) => decision switch
{
    "accept" => Decision.Accept,
    "dismiss" => Decision.Dismiss,
    _ => throw new DecisionRefusedException($"'{decision}' is not a decision this store takes: accept, or dismiss."),
};

