using Avalonia;

namespace Waymark.Pos;

/// <summary>
/// The till's entry point. <see cref="App"/> reads the switches and opens
/// <see cref="TillWindow"/>. No font is registered here: the till draws in the faces it
/// bundles (D-080), each addressed by its own file in <c>TillTheme</c>.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Referenced by name by the Avalonia designer tooling. Do not rename.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
