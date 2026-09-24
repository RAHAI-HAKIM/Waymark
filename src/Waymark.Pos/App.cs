using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;
using Waymark.Pos.Server;
using Waymark.Pos.Ui;

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
            var session = new TillSession(server, server, till, TimeProvider.System);

            // French unless told otherwise (G1, 23/09). Whether the language follows the person
            // signed in or the till is A5's to settle; until then a switch sets it.
            var language = Setting(args, "--lang=", "WAYMARK_LANG") is "ar" ? TillLanguage.Arabic : TillLanguage.French;
            var theme = ThemeOf(Setting(args, "--theme=", "WAYMARK_THEME"), PlatformSettings);

            // Fluent draws the parts this shell does not (scroll bars, the caret): keep it in step.
            RequestedThemeVariant = theme == TillThemeKind.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

            var window = new TillWindow(session, server, till, language, theme, TimeProvider.System);
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => http.Dispose();

#if DEBUG
            // Renders the shell to a PNG and exits, for reviewing it against the G1 boards:
            // --snapshot=out.png [--scan=code,code] [--pay] [--select]
            if (Setting(args, "--snapshot=", "WAYMARK_SNAPSHOT") is { } snapshot)
            {
                var codes = (Setting(args, "--scan=", "WAYMARK_SCAN") ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var pay = args.Contains("--pay");
                var select = args.Contains("--select");
                window.Opened += async (_, _) =>
                {
                    try
                    {
                        await window.SnapshotAsync(snapshot, codes, pay, select);
                    }
                    finally
                    {
                        desktop.Shutdown();
                    }
                };
            }
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// <c>--theme=dark</c> or <c>--theme=light</c>; otherwise whatever Windows is set to. The
    /// palette is chosen once, at start: a till does not change its colours mid-sale.
    /// </summary>
    public static TillThemeKind ThemeOf(string? requested, IPlatformSettings? platform) => requested switch
    {
        "dark" => TillThemeKind.Dark,
        "light" => TillThemeKind.Light,
        _ => platform?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark ? TillThemeKind.Dark : TillThemeKind.Light,
    };

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
