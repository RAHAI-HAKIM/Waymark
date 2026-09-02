using Avalonia;

namespace Waymark.Pos;

/// <summary>
/// Phase 0 placeholder. The till client exists so the solution builds; the
/// checkout screen arrives in Phase 1.
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
            .WithInterFont()
            .LogToTrace();
}
