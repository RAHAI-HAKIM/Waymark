using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Waymark.Pos.Checkout;
using Waymark.Pos.Server;

namespace Waymark.Pos;

/// <summary>
/// Built in code rather than XAML for now (D-007): a code-only shell has no
/// markup pipeline to go wrong. Phase 1 introduces <c>.axaml</c>.
/// </summary>
public sealed class App : Application
{
    /// <summary>The command-line switch naming StoreServer's address.</summary>
    public const string ServerSwitch = "--server=";

    /// <summary>The environment variable naming it, when the switch is absent.</summary>
    public const string ServerVariable = "WAYMARK_SERVER";

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var http = StoreServerClient.CreateHttp(ServerAddress(args));
            var server = new StoreServerClient(http);
            var till = new TillIdentity(Setting(args, "--terminal=", "WAYMARK_TERMINAL"), Setting(args, "--staff=", "WAYMARK_STAFF"));
            var session = new TillSession(server, server, till);

            desktop.MainWindow = new TillWindow(session, TimeProvider.System);
            desktop.Exit += (_, _) => http.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>A <c>--switch=value</c> argument, else an environment variable, else null.</summary>
    public static string? Setting(IReadOnlyList<string> args, string @switch, string variable) =>
        args.FirstOrDefault(arg => arg.StartsWith(@switch, StringComparison.Ordinal))?[@switch.Length..]
            ?? Environment.GetEnvironmentVariable(variable);

    /// <summary>
    /// <c>--server=http://host:port/</c>, else <c>WAYMARK_SERVER</c>, else the
    /// development address. The POS talks to StoreServer over HTTP only
    /// (CLAUDE.md §2.2), so this address is the whole of its configuration.
    /// </summary>
    public static Uri ServerAddress(IReadOnlyList<string> args)
    {
        var text = Setting(args, ServerSwitch, ServerVariable);

        if (string.IsNullOrWhiteSpace(text))
        {
            return StoreServerClient.DefaultAddress;
        }

        if (!Uri.TryCreate(text.EndsWith('/') ? text : text + "/", UriKind.Absolute, out var address))
        {
            throw new ArgumentException($"'{text}' is not a StoreServer address, such as http://localhost:5290/.");
        }

        return address;
    }
}
