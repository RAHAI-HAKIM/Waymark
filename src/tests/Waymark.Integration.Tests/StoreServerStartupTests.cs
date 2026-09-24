using System.Diagnostics;
using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain;
using Waymark.Domain.Enums;
using Waymark.Domain.Privacy;
using Waymark.Domain.Reference;
using Waymark.Domain.Values;
using Waymark.Persistence;
using Waymark.Pseudonymisation;

namespace Waymark.Integration.Tests;

/// <summary>
/// StoreServer run as the real process (F-1, O-18): it will not start on a keys directory other
/// accounts can read, will not open a plaintext store, imports a generated one, and reopens its
/// own encrypted store after a restart. Windows only.
/// </summary>
public sealed class StoreServerStartupTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "waymark-server", Guid.NewGuid().ToString("N"));

    private string Data => Path.Combine(_root, "data");

    private string Keys => Path.Combine(_root, "keys");

    [Fact]
    public void It_starts_on_a_fresh_machine_with_an_encrypted_store_and_a_locked_down_keys_directory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var run = Run();

        Assert.True(run.Started, "StoreServer did not start:\n" + run.Output);
        Assert.Contains("Database ready and encrypted", run.Output, StringComparison.Ordinal);
        Assert.True(DatabaseKeyStore.Exists(Keys));
        Assert.Empty(KeysDirectoryAccess.Problems(Keys));

        var database = WaymarkStoragePaths.StoreDatabase(Data);
        Assert.True(File.Exists(database));
        Assert.False(WaymarkDatabaseEncryption.IsPlaintext(database));
    }

    [Fact]
    public void It_refuses_to_start_when_the_keys_directory_inherits_its_permissions()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(Keys);

        var run = Run();

        Assert.False(run.Started, "StoreServer started with a keys directory every local account can read.");
        Assert.Contains("keys directory is not protected", run.Output, StringComparison.Ordinal);
        Assert.False(DatabaseKeyStore.Exists(Keys), "A key was written into an unprotected directory.");
        Assert.False(File.Exists(WaymarkStoragePaths.StoreDatabase(Data)));
    }

    [Fact]
    public void It_refuses_to_start_when_users_can_read_the_keys()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        KeysDirectoryAccess.EnsureCreated(Keys);
        var directory = new DirectoryInfo(Keys);
        var security = directory.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Read, AccessControlType.Allow));
        directory.SetAccessControl(security);

        var run = Run();

        Assert.False(run.Started, "StoreServer started with a keys directory Users can read.");
        Assert.Contains("Users can access", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void It_refuses_a_plaintext_store_and_imports_one_when_told_to()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var plaintext = Path.Combine(_root, "generated", "waymark-store.db");
        Directory.CreateDirectory(Path.GetDirectoryName(plaintext)!);
        CreatePlaintextStore(plaintext);

        // In place: refused.
        Directory.CreateDirectory(Data);
        File.Copy(plaintext, WaymarkStoragePaths.StoreDatabase(Data));
        var refused = Run();
        Assert.False(refused.Started, "StoreServer opened a plaintext store.");
        Assert.Contains("is not encrypted", refused.Output, StringComparison.Ordinal);

        // Imported: an encrypted copy that holds the same rows.
        File.Delete(WaymarkStoragePaths.StoreDatabase(Data));
        var imported = Run($"--{WaymarkStoragePaths.ImportPlaintextSetting}={plaintext}");
        Assert.True(imported.Started, "StoreServer did not start on an imported store:\n" + imported.Output);

        var database = WaymarkStoragePaths.StoreDatabase(Data);
        Assert.False(WaymarkDatabaseEncryption.IsPlaintext(database));

        using var key = DatabaseKeyStore.Open(Keys, new DpapiKeyProtector());
        using var context = new WaymarkDbContext(
            new DbContextOptionsBuilder<WaymarkDbContext>().UseWaymarkSqlite(database, key).Options,
            new FixedCurrentStore(null),
            new FixedLedgerCurrency(Currency.Dzd));
        Assert.Equal("IMPORTED", context.ReasonCodes.Select(r => r.ReasonCodeValue).Single(r => r == "IMPORTED"));
    }

    [Fact]
    public void A_generated_store_is_imported_served_and_reopened_after_a_restart()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var generated = Generate();
        var source = Path.Combine(generated, "waymark-store.db");
        string[] store = [$"--Waymark:Store:StoreId={StoreIdOf(generated)}", "--Waymark:Store:Currency=DZD"];
        var before = CountTransactions(source, key: null);
        var outboxBefore = CountOutbox(source, key: null);
        Assert.True(before > 0);

        var barcode = SellableBarcodeIn(source);
        var (terminal, staff) = TillOf(source);
        var first = Run(
            address =>
            {
                AssertHealthy(address);
                AssertLookup(address, barcode);
                AssertReasonCodes(address);
                AssertTillContext(address, terminal, staff);
                AssertNobodyCanSellYet(address, barcode, terminal, staff);
                AssertExpiryEvaluation(address);
            },
            [.. store, $"--{WaymarkStoragePaths.ImportPlaintextSetting}={source}"]);
        Assert.True(first.Started, "The generated store was not imported and served:\n" + first.Output);
        Assert.Contains("Store time zone: Africa/Algiers", first.Output, StringComparison.Ordinal);

        // A5 (D-083): the maintenance switch sets the cashier's PIN on the encrypted store, typed
        // twice on the console, and exits without serving anything.
        var setPin = RunToExit($"{DemoPin}\n{DemoPin}\n", [.. store, $"--set-pin={staff}"]);
        Assert.True(setPin.ExitCode == 0, "--set-pin did not set the PIN:\n" + setPin.Output);
        Assert.Contains("PIN set", setPin.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Now listening", setPin.Output, StringComparison.Ordinal);

        // A second start: no import, an existing encrypted file, an existing key — and the PIN.
        var second = Run(
            address =>
            {
                AssertHealthy(address);
                AssertSignInAndSale(address, barcode, terminal, staff);
            },
            store);
        Assert.True(second.Started, "StoreServer did not reopen its own encrypted store:\n" + second.Output);
        Assert.DoesNotContain("Importing", second.Output, StringComparison.Ordinal);

        using var key = DatabaseKeyStore.Open(Keys, new DpapiKeyProtector());
        // Every generated sale survived the import, plus the one AssertSale made.
        Assert.Equal(before + 1, CountTransactions(WaymarkStoragePaths.StoreDatabase(Data), key));

        // And that sale put its anonymous basket in the outbox, in the same transaction (D-072).
        Assert.Equal(outboxBefore + 1, CountOutbox(WaymarkStoragePaths.StoreDatabase(Data), key));
        Assert.True(WaymarkDatabaseEncryption.IsPlaintext(source), "The import changed the generated file.");
    }

    [Fact]
    public void It_refuses_an_encrypted_store_whose_key_is_gone_and_never_makes_a_new_one()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.True(Run().Started);
        var keyFile = Path.Combine(Keys, DatabaseKeyStore.FileName);
        File.Delete(keyFile);

        var run = Run();

        Assert.False(run.Started, "StoreServer started on a store it has no key for.");
        Assert.Contains("its key", run.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(keyFile), "A new key was made beside an existing database; it could never open it.");
    }

    [Fact]
    public void It_refuses_a_store_whose_currency_disagrees_with_its_configuration()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var generated = Generate();
        var run = Run(
            whileRunning: null,
            $"--Waymark:Store:StoreId={StoreIdOf(generated)}",
            "--Waymark:Store:Currency=EUR",
            $"--{WaymarkStoragePaths.ImportPlaintextSetting}={Path.Combine(generated, "waymark-store.db")}");

        Assert.False(run.Started, "StoreServer read a DZD store as EUR.");
        Assert.Contains("store row says 'DZD'", run.Output, StringComparison.Ordinal);
    }

    private static void AssertHealthy(Uri address)
    {
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
        using var response = client.GetAsync(new Uri("/health", UriKind.Relative)).GetAwaiter().GetResult();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"up\"", response.Content.ReadAsStringAsync().GetAwaiter().GetResult(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Session A4 end to end on the real process: the generated store's own till and cashier are
    /// named for the top bar, and a terminal this store does not have is an answer, not an error.
    /// </summary>
    private static void AssertTillContext(Uri address, string terminal, string staff)
    {
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };

        using var found = client.GetAsync(new Uri($"/api/till/context?terminal={terminal}&staff={staff}", UriKind.Relative)).GetAwaiter().GetResult();
        var body = found.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.True(found.StatusCode == HttpStatusCode.OK, $"The till context failed ({found.StatusCode}): " + body);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("found", json.RootElement.GetProperty("outcome").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("store_name").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("staff_name").GetString()));

        using var unknown = client.GetAsync(new Uri("/api/till/context?terminal=no-such-till", UriKind.Relative)).GetAwaiter().GetResult();
        using var unknownJson = JsonDocument.Parse(unknown.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        Assert.Equal("unknown_terminal", unknownJson.RootElement.GetProperty("outcome").GetString());
    }

    /// <summary>
    /// Session A3 end to end on the real process: the generated store's own reason codes come
    /// back through the endpoint, and a kind nobody knows is refused rather than answered with
    /// an empty list. This is the only thing that proves the DI registration and the route
    /// exist — every other A3 test calls the reader or the mapping directly.
    /// </summary>
    private static void AssertReasonCodes(Uri address)
    {
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };

        using var listed = client.GetAsync(new Uri("/api/reason-codes?applies_to=discount", UriKind.Relative)).GetAwaiter().GetResult();
        var body = listed.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.True(listed.StatusCode == HttpStatusCode.OK, $"The reason codes failed ({listed.StatusCode}):" + body);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("discount", json.RootElement.GetProperty("applies_to").GetString());

        // The generator seeds discount reasons from its config, so an empty list here means
        // the endpoint is reading something other than the store that was just imported.
        var codes = json.RootElement.GetProperty("reason_codes");
        Assert.True(codes.GetArrayLength() > 0, "The generated store offered no discount reasons: " + body);

        foreach (var option in codes.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(option.GetProperty("code").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(option.GetProperty("label_fr").GetString()));
        }

        using var nonsense = client.GetAsync(new Uri("/api/reason-codes?applies_to=nonsense", UriKind.Relative)).GetAwaiter().GetResult();
        Assert.Equal(HttpStatusCode.BadRequest, nonsense.StatusCode);
    }

    /// <summary>
    /// Hop 1 end to end on the real process (D-066): a generated product is found at a price, in
    /// today's store date, and a barcode nobody carries is an answer rather than an error.
    /// </summary>
    private static void AssertLookup(Uri address, string barcode)
    {
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };

        using var found = client.GetAsync(new Uri($"/api/products/lookup?barcode={barcode}", UriKind.Relative)).GetAwaiter().GetResult();
        var body = found.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.True(found.StatusCode == HttpStatusCode.OK, $"The lookup failed ({found.StatusCode}):\n{body}");

        using var json = JsonDocument.Parse(body);
        Assert.Equal("found", json.RootElement.GetProperty("outcome").GetString());
        var price = decimal.Parse(
            json.RootElement.GetProperty("product").GetProperty("price_ttc").GetString()!,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(price > 0, $"A generated product was served at {price}.");

        using var unknown = client.GetAsync(new Uri("/api/products/lookup?barcode=0000000000000", UriKind.Relative)).GetAwaiter().GetResult();
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Contains("\"unknown_barcode\"", unknown.Content.ReadAsStringAsync().GetAwaiter().GetResult(), StringComparison.Ordinal);

        // Found in the step 7 run (18/09): as a path segment, "12/34" reached the lookup as
        // "12%2F34". As a query parameter the server must see exactly the code that was sent.
        using var slashed = client.GetAsync(new Uri("/api/products/lookup?barcode=12%2F34", UriKind.Relative)).GetAwaiter().GetResult();
        using var echoed = JsonDocument.Parse(slashed.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        Assert.Equal("12/34", echoed.RootElement.GetProperty("barcode").GetString());
    }

    /// <summary>The PIN the end-to-end run sets for the generated cashier. A test store's, nobody's.</summary>
    private const string DemoPin = "4821";

    /// <summary>
    /// A5 before any PIN exists (D-083): the generated cashier is listed without one, cannot sign
    /// in, and a sale that names them in the body is not a sale: nobody is signed in.
    /// </summary>
    private static void AssertNobodyCanSellYet(Uri address, string barcode, string terminal, string staff)
    {
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };

        using var listed = JsonDocument.Parse(Get(client, "/api/till/staff"));
        var cashier = listed.RootElement.GetProperty("staff").EnumerateArray()
            .Single(person => person.GetProperty("staff_id").GetString() == staff);
        Assert.False(cashier.GetProperty("has_pin").GetBoolean(), "A generated cashier has a usable PIN.");
        Assert.DoesNotContain("synthetic", listed.RootElement.GetRawText(), StringComparison.Ordinal);

        Assert.Equal("no_pin", SignIn(client, terminal, staff, DemoPin).GetProperty("outcome").GetString());

        using var claimed = client.PostAsync(
            new Uri("/api/sales", UriKind.Relative),
            JsonBody($$"""{"terminal_id":"{{terminal}}","staff_id":"{{staff}}","lines":[{"barcode":"{{barcode}}","count":1}]}""")).GetAwaiter().GetResult();
        Assert.Contains("\"not_signed_in\"", claimed.Content.ReadAsStringAsync().GetAwaiter().GetResult(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A5 on the real process (D-083), after --set-pin: a wrong PIN is counted, the right one opens
    /// a session, the session's token sells, and nothing else does — not another till's id, not a
    /// token after sign-out.
    /// </summary>
    private static void AssertSignInAndSale(Uri address, string barcode, string terminal, string staff)
    {
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };

        using (var listed = JsonDocument.Parse(Get(client, "/api/till/staff")))
        {
            Assert.True(listed.RootElement.GetProperty("staff").EnumerateArray()
                .Single(person => person.GetProperty("staff_id").GetString() == staff)
                .GetProperty("has_pin").GetBoolean());
        }

        var wrong = SignIn(client, terminal, staff, "0000");
        Assert.Equal("wrong_pin", wrong.GetProperty("outcome").GetString());
        Assert.Equal(4, wrong.GetProperty("attempts_left").GetInt32());

        var right = SignIn(client, terminal, staff, DemoPin);
        Assert.Equal("signed_in", right.GetProperty("outcome").GetString());
        var token = right.GetProperty("session_token").GetString()!;

        AssertSale(client, barcode, terminal, token);

        // The token is this till's: at another till's id it sells nothing.
        Assert.Equal("not_signed_in", Sell(client, "no-such-till", barcode, token).GetProperty("outcome").GetString());

        using var signOut = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/till/sign-out", UriKind.Relative));
        signOut.Headers.Add("X-Waymark-Session", token);
        using (var ended = client.SendAsync(signOut).GetAwaiter().GetResult())
        {
            Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        }

        Assert.Equal("not_signed_in", Sell(client, terminal, barcode, token).GetProperty("outcome").GetString());
    }

    private static string Get(HttpClient client, string path)
    {
        using var response = client.GetAsync(new Uri(path, UriKind.Relative)).GetAwaiter().GetResult();
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} failed ({response.StatusCode}): {body}");
        return body;
    }

    private static JsonElement SignIn(HttpClient client, string terminal, string staff, string pin)
    {
        using var response = client.PostAsync(
            new Uri("/api/till/sign-in", UriKind.Relative),
            JsonBody($$"""{"terminal_id":"{{terminal}}","staff_id":"{{staff}}","pin":"{{pin}}"}""")).GetAwaiter().GetResult();
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"The sign-in failed ({response.StatusCode}): {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static JsonElement Sell(HttpClient client, string terminal, string barcode, string token, int count = 1)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/sales", UriKind.Relative))
        {
            Content = JsonBody($$"""{"terminal_id":"{{terminal}}","lines":[{"barcode":"{{barcode}}","count":{{count}}}]}"""),
        };
        request.Headers.Add("X-Waymark-Session", token);
        using var response = client.SendAsync(request).GetAwaiter().GetResult();
        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"The sale failed ({response.StatusCode}):\n{body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>
    /// Hop 2 on the real process (D-070): the wiring (unit of work, executor, handler, ledger) is
    /// only proven by a sale that goes through it and comes back completed, then an unknown code
    /// that comes back refused rather than as an error. Sold by the session's person (A5).
    /// </summary>
    private static void AssertSale(HttpClient client, string barcode, string terminal, string token)
    {
        var sold = Sell(client, terminal, barcode, token, count: 2);
        Assert.Equal("completed", sold.GetProperty("outcome").GetString());
        // The generated store's invoices are all from an earlier year: this year starts at 1.
        Assert.EndsWith("-000001", sold.GetProperty("invoice_number").GetString(), StringComparison.Ordinal);

        Assert.Equal("refused", Sell(client, terminal, "0000000000000", token).GetProperty("outcome").GetString());
    }

    /// <summary>
    /// Hop 6 on the real process (D-073): the evaluator's wiring, and the cold-start window
    /// installed at start. A year of synthetic trading leaves batches on the shelf that are
    /// past their date, so a run over real data has something to say — and the second run has
    /// to say the same thing without doubling the board.
    /// </summary>
    private static void AssertExpiryEvaluation(Uri address)
    {
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };

        var first = EvaluateExpiry(client);
        Assert.Equal(7, first.GetProperty("windowDays").GetInt64());
        Assert.Equal(1, first.GetProperty("parameterVersion").GetInt64());
        Assert.True(first.GetProperty("batchesRead").GetInt32() > 0, "The generated store has stock on its shelves.");
        var flagged = first.GetProperty("flagged").GetInt32();
        Assert.True(flagged > 0, "A year of trading leaves batches inside a seven-day window.");
        Assert.Equal(0, first.GetProperty("superseded").GetInt32());

        var again = EvaluateExpiry(client);
        Assert.Equal(flagged, again.GetProperty("flagged").GetInt32());
        Assert.Equal(flagged, again.GetProperty("superseded").GetInt32());
    }

    private static JsonElement EvaluateExpiry(HttpClient client)
    {
        using var answer = client.PostAsync(new Uri("/api/engine/expiry", UriKind.Relative), JsonBody("{}"))
            .GetAwaiter().GetResult();
        var body = answer.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Assert.True(answer.StatusCode == HttpStatusCode.OK, $"The evaluator failed ({answer.StatusCode}): {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>The generated store's till and one of its cashiers.</summary>
    private static (string Terminal, string Staff) TillOf(string path)
    {
        using var context = new WaymarkDbContext(
            new DbContextOptionsBuilder<WaymarkDbContext>().UseWaymarkSqlite(path, keyProvider: null).Options,
            new FixedCurrentStore(null),
            new FixedLedgerCurrency(Currency.Dzd));
        return (
            context.Terminals.IgnoreQueryFilters().OrderBy(t => t.TerminalId).Select(t => t.TerminalId).First(),
            context.Staff.IgnoreQueryFilters().Where(s => s.Role == "cashier").OrderBy(s => s.StaffId).Select(s => s.StaffId).First());
    }

    /// <summary>A barcode of an active, piece-sold variant in a generated (plaintext) store.</summary>
    private static string SellableBarcodeIn(string path)
    {
        using var context = new WaymarkDbContext(
            new DbContextOptionsBuilder<WaymarkDbContext>().UseWaymarkSqlite(path, keyProvider: null).Options,
            new FixedCurrentStore(null),
            new FixedLedgerCurrency(Currency.Dzd));
        return context.Variants
            .Where(v => v.Barcode != null && !v.IsWeighted && v.Status == VariantStatus.Active)
            .OrderBy(v => v.VariantId)
            .Select(v => v.Barcode!)
            .First();
    }

    private static void CreatePlaintextStore(string path)
    {
        using var context = new WaymarkDbContext(
            new DbContextOptionsBuilder<WaymarkDbContext>().UseWaymarkSqlite(path, keyProvider: null).Options,
            new FixedCurrentStore(null),
            new FixedLedgerCurrency(Currency.Dzd));
        context.MigrateAndApplyTriggers();
        context.ReasonCodes.Add(new ReasonCode
        {
            ReasonCodeValue = "IMPORTED",
            AppliesTo = ReasonCodeAppliesTo.Discount,
            LabelAr = "imported",
            LabelFr = "imported",
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        context.SaveChanges();
    }

    /// <summary>A short mini store, made by the generator's own executable.</summary>
    private string Generate()
    {
        var output = Path.Combine(_root, "generated-" + Guid.NewGuid().ToString("N")[..8]);
        var generator = BuiltAssembly("Waymark.Generator");
        var config = Path.Combine(SourceRoot(), "tests", "Waymark.Generator.Tests", "Fixtures", "mini", "configs", "mini.json");

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(generator)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { generator, "--config", config, "--out", output, "--days", "20" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var log = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(Patience), "The generator did not finish.");
        Assert.True(process.ExitCode == 0, $"The generator failed:\n{log.Result}\n{errors.Result}");
        return output;
    }

    private static string StoreIdOf(string generated) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(generated, "manifest.json")))
            .RootElement.GetProperty("store_id").GetString()!;

    private static long CountOutbox(string path, IDatabaseKeyProvider? key)
    {
        using var context = new WaymarkDbContext(
            new DbContextOptionsBuilder<WaymarkDbContext>().UseWaymarkSqlite(path, key).Options,
            new FixedCurrentStore(null),
            new FixedLedgerCurrency(Currency.Dzd));
        return context.Outbox.LongCount();
    }

    private static long CountTransactions(string path, IDatabaseKeyProvider? key)
    {
        using var context = new WaymarkDbContext(
            new DbContextOptionsBuilder<WaymarkDbContext>().UseWaymarkSqlite(path, key).Options,
            new FixedCurrentStore(null),
            new FixedLedgerCurrency(Currency.Dzd));
        return context.Transactions.IgnoreQueryFilters().LongCount();
    }

    private (bool Started, string Output) Run(params string[] extra) => Run(null, extra);

    /// <summary>Runs StoreServer with <paramref name="input"/> on its console until it exits: a maintenance switch.</summary>
    private (int ExitCode, string Output) RunToExit(string input, params string[] extra)
    {
        var server = BuiltAssembly("Waymark.StoreServer");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(server)!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(server);
        start.ArgumentList.Add($"--{WaymarkStoragePaths.DataDirectorySetting}={Data}");
        start.ArgumentList.Add($"--{WaymarkStoragePaths.KeysDirectorySetting}={Keys}");
        start.ArgumentList.Add("--urls=http://127.0.0.1:0");
        foreach (var argument in extra)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["Logging__Console__FormatterName"] = "simple";

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(input);
        process.StandardInput.Close();

        if (!process.WaitForExit(Patience))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return (-1, "Did not exit.\n" + output.Result + errors.Result);
        }

        return (process.ExitCode, output.Result + errors.Result);
    }

    /// <summary>
    /// Runs StoreServer until it reports that it started, or exits; if it started, calls
    /// <paramref name="whileRunning"/> with its address. The process is stopped either way.
    /// </summary>
    private (bool Started, string Output) Run(Action<Uri>? whileRunning, params string[] extra)
    {
        var server = BuiltAssembly("Waymark.StoreServer");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(server)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(server);
        start.ArgumentList.Add($"--{WaymarkStoragePaths.DataDirectorySetting}={Data}");
        start.ArgumentList.Add($"--{WaymarkStoragePaths.KeysDirectorySetting}={Keys}");
        start.ArgumentList.Add("--urls=http://127.0.0.1:0");
        foreach (var argument in extra)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["Logging__Console__FormatterName"] = "simple";

        var output = new StringBuilder();
        Uri? address = null;
        using var started = new ManualResetEventSlim();
        using var process = new Process { StartInfo = start };

        void Collect(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (output)
            {
                output.AppendLine(line);
            }

            var listening = line.IndexOf("Now listening on: ", StringComparison.Ordinal);
            if (listening >= 0)
            {
                address = new Uri(line[(listening + "Now listening on: ".Length)..].Trim());
            }

            if (line.Contains("Application started", StringComparison.Ordinal))
            {
                started.Set();
            }
        }

        process.OutputDataReceived += (_, e) => Collect(e.Data);
        process.ErrorDataReceived += (_, e) => Collect(e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = Task.Run(() => process.WaitForExit(Patience));
        var index = WaitHandle.WaitAny([started.WaitHandle, ((IAsyncResult)exited).AsyncWaitHandle], Patience);
        var didStart = index == 0 && started.IsSet;

        try
        {
            if (didStart && whileRunning is not null)
            {
                Assert.NotNull(address);
                whileRunning(address);
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit();
        }

        lock (output)
        {
            return (didStart, output.ToString());
        }
    }

    /// <summary>A project's own build output, in the same configuration as this test run.</summary>
    private static string BuiltAssembly(string project)
    {
        var testBin = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var path = Path.Combine(SourceRoot(), project, "bin", testBin.Parent!.Name, testBin.Name, project + ".dll");
        Assert.True(File.Exists(path), $"{project} is not built at {path}.");
        return path;
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Waymark.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
