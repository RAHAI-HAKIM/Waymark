using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace Waymark.Pos;

/// <summary>
/// Built in code rather than XAML for now, deliberately: there is no UI to
/// design yet and a code-only shell has no markup pipeline to go wrong.
/// Phase 1 replaces this wholesale.
/// </summary>
public sealed class App : Application
{
    // Violet #5A3AA8 is the operator colour — every button, action and active
    // state (CLAUDE.md §6). Cyan belongs to Almanac and never appears here.
    private static readonly Color Violet = Color.FromRgb(0x5A, 0x3A, 0xA8);

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "Waymark POS",
                Width = 1024,
                Height = 768,
                Content = new TextBlock
                {
                    Text = "Waymark POS — Phase 0 shell.\nThe till arrives in Phase 1.",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(Violet),
                    FontSize = 20
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
