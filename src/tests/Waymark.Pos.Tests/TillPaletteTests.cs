using System.Globalization;
using System.Text.RegularExpressions;
using Waymark.Pos.Ui;

namespace Waymark.Pos.Tests;

/// <summary>
/// The till's colours (G1 kit §1). The kit promises that "every text holds 4.5:1 on the surface
/// it sits on, every rule and ring 3:1"; these tests hold the till to it, in both themes, for
/// every pairing the views actually use. And the palette is the only place a colour may be
/// named, so the brand rules are checked here and nowhere else.
/// </summary>
public sealed partial class TillPaletteTests
{
    private const double Text = 4.5;

    /// <summary>For marks, edges, borders that carry meaning and focus rings (WCAG 1.4.11).</summary>
    private const double Mark = 3.0;

    public static TheoryData<TillThemeKind> Themes() => new() { TillThemeKind.Light, TillThemeKind.Dark };

    /// <summary>Every foreground on every ground the views put it on.</summary>
    private static IEnumerable<(string What, string Fore, string Ground, double Floor)> Pairs(TillPalette p)
    {
        foreach (var (name, ground) in new[] { ("page", p.Page), ("card", p.Card), ("tile", p.Tile) })
        {
            yield return ($"text on {name}", p.Text, ground, Text);
            yield return ($"text-secondary on {name}", p.TextSecondary, ground, Text);
            yield return ($"focus ring on {name}", p.FocusRing, ground, Mark);
        }

        // Muted labels sit on the page and on cards; on a tile the views use text-secondary.
        yield return ("text-muted on page", p.TextMuted, p.Page, Text);
        yield return ("text-muted on card", p.TextMuted, p.Card, Text);

        yield return ("bar text", p.BarText, p.Bar, Text);
        yield return ("bar labels", p.BarLabel, p.Bar, Text);
        yield return ("bar key border", p.BarKeyBorder, p.Bar, Mark);
        yield return ("Encaisser label", p.CollectLabel, p.Collect, Text);
        yield return ("Encaisser on the bar", p.Collect, p.Bar, Mark);
        yield return ("action label", p.ActionLabel, p.Action, Text);
        yield return ("action on a card", p.Action, p.Card, Mark);
        yield return ("unavailable key label", p.DisabledLabel, p.DisabledFill, Text);
        yield return ("unavailable bar key label", p.BarDisabledLabel, p.BarDisabledFill, Text);

        foreach (var (name, signal) in new[] { ("warning", p.Warning), ("critical", p.Critical), ("almanac", p.Almanac) })
        {
            yield return ($"{name} label on card", signal.Text, p.Card, Text);
            yield return ($"{name} edge on card", signal.Mark, p.Card, Mark);
            if (signal.Fill is { } fill)
            {
                yield return ($"{name} label on its fill", signal.Text, fill, Text);
            }
        }

        // The offline label and its edge sit on the top bar.
        yield return ("critical label on the bar", p.BarCritical.Text, p.Bar, Text);
        yield return ("critical edge on the bar", p.BarCritical.Mark, p.Bar, Mark);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Every_label_clears_its_contrast_floor(TillThemeKind theme)
    {
        var failures = Pairs(TillPalette.For(theme))
            .Select(pair => (pair.What, Ratio: Contrast(pair.Fore, pair.Ground), pair.Floor))
            .Where(pair => pair.Ratio < pair.Floor)
            .Select(pair => $"{pair.What}: {pair.Ratio:0.00}:1, floor {pair.Floor}:1")
            .ToList();

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Royal_violet_on_the_bar_is_why_Encaisser_is_accent_violet()
    {
        // The rule the palette encodes: the bars are a dark surface in both themes. Royal violet
        // there is nearly invisible, so a view that "fixed" Encaisser to the light theme's action
        // colour would pass every other test and hide the till's main button.
        Assert.True(Contrast(TillPalette.Light.Action, TillPalette.Light.Bar) < Mark);
        Assert.Equal(TillPalette.Light.Collect, TillPalette.Dark.Collect);
    }

    [Fact]
    public void The_bars_are_the_darkest_surface_in_both_themes()
    {
        foreach (var p in new[] { TillPalette.Light, TillPalette.Dark })
        {
            Assert.True(Luminance(p.Bar) < Luminance(p.Page), $"{p.Kind}: the bar is lighter than the page.");
            Assert.True(Luminance(p.Bar) < Luminance(p.Card), $"{p.Kind}: the bar is lighter than a card.");
        }
    }

    [Fact]
    public void The_palette_is_the_only_file_that_names_a_colour()
    {
        // So §6 can be checked by reading one file. A colour typed into a view drifts from the
        // palette the first time someone changes one and not the other.
        var pos = Path.Combine(SourceRoot(), "Waymark.Pos");
        var offenders = Directory.EnumerateFiles(pos, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => Path.GetFileName(path) != "TillPalette.cs")
            .Where(path => ColourLiteral().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(pos, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_contrast_arithmetic_is_right()
    {
        // The two extremes the formula is defined by, so a mistake in it cannot pass everything.
        Assert.Equal(21.0, Contrast("#000000", "#FFFFFF"), 2);
        Assert.Equal(1.0, Contrast("#5A3AA8", "#5A3AA8"), 2);
    }

    // ------------------------------------------------------------ helpers

    /// <summary>WCAG 2.x relative luminance.</summary>
    private static double Luminance(string hex)
    {
        double Channel(int start)
        {
            var c = int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(1)) + (0.7152 * Channel(3)) + (0.0722 * Channel(5));
    }

    private static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static string SourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Waymark.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not find src/Waymark.sln above the test binaries.");
    }

    [GeneratedRegex("#[0-9A-Fa-f]{6}\\b")]
    private static partial Regex ColourLiteral();
}
