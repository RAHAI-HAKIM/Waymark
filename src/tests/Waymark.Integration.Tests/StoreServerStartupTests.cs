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
        Assert.True(before > 0);

        var first = Run(AssertHealthy, [.. store, $"--{WaymarkStoragePaths.ImportPlaintextSetting}={source}"]);
        Assert.True(first.Started, "The generated store was not imported and served:\n" + first.Output);

        // A second start: no import, an existing encrypted file, an existing key.
        var second = Run(AssertHealthy, store);
        Assert.True(second.Started, "StoreServer did not reopen its own encrypted store:\n" + second.Output);
        Assert.DoesNotContain("Importing", second.Output, StringComparison.Ordinal);

        using var key = DatabaseKeyStore.Open(Keys, new DpapiKeyProtector());
        Assert.Equal(before, CountTransactions(WaymarkStoragePaths.StoreDatabase(Data), key));
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

    private static long CountTransactions(string path, IDatabaseKeyProvider? key)
    {
        using var context = new WaymarkDbContext(
            new DbContextOptionsBuilder<WaymarkDbContext>().UseWaymarkSqlite(path, key).Options,
            new FixedCurrentStore(null),
            new FixedLedgerCurrency(Currency.Dzd));
        return context.Transactions.IgnoreQueryFilters().LongCount();
    }

    private (bool Started, string Output) Run(params string[] extra) => Run(null, extra);

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
