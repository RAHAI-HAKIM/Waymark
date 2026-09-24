using Avalonia;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using OutlinePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Ui;

/// <summary>The G1 kit's sizes (§4), in one place. The grid is 4 px.</summary>
public static class TillSizes
{
    public const double TopBar = 60;
    public const double BottomBar = 100;

    /// <summary>Every key and button: <c>control-lg</c>, because the till is touched (design system).</summary>
    public const double Key = 48;

    /// <summary>The bottom bar's keys.</summary>
    public const double BarKey = 72;

    /// <summary>A cart line: 40, so a big basket fits.</summary>
    public const double CartRow = 40; // this needs 2b revised

    public const double Rail = 472;
    public const double Margin = 16;
    public const double Gap = 16;

    public const double KeyRadius = 10;
    public const double CardRadius = 14;
    public const double FieldRadius = 6;

    /// <summary><c>border-signal</c>: the one 3 px rule that names a weight. Nothing else is 3 px.</summary>
    public const double SignalRule = 3;
}

/// <summary>
/// The palette as brushes, the bundled faces, and the kit's type styles (G1 kit §1–2). Views draw
/// with these and name no colour and no font of their own.
///
/// <para>
/// <b>The face follows the language.</b> Archivo carries French and IBM Plex Sans Arabic carries
/// Arabic; figures, codes and quantities are IBM Plex Mono in both, with Latin digits (kit §2).
/// Each weight is addressed by its own file: the static cuts name themselves "Archivo SemiBold"
/// rather than "Archivo" (D-080), so asking Avalonia to match a weight within one family is a
/// guess this class does not make.
/// </para>
/// </summary>
public sealed partial class TillTheme
{
    private const string Fonts = "avares://Waymark.Pos/Assets/Fonts/";

    public TillTheme(TillPalette palette, TillLanguage language)
    {
        ArgumentNullException.ThrowIfNull(palette);
        Palette = palette;
        Language = language;

        Page = Brush(palette.Page);
        Card = Brush(palette.Card);
        Tile = Brush(palette.Tile);
        Border = Brush(palette.Border);
        BorderSubtle = Brush(palette.BorderSubtle);
        Bar = Brush(palette.Bar);
        BarText = Brush(palette.BarText);
        BarLabel = Brush(palette.BarLabel);
        BarKeyBorder = Brush(palette.BarKeyBorder);
        Text = Brush(palette.Text);
        TextSecondary = Brush(palette.TextSecondary);
        TextMuted = Brush(palette.TextMuted);
        Collect = Brush(palette.Collect);
        CollectLabel = Brush(palette.CollectLabel);
        Action = Brush(palette.Action);
        ActionLabel = Brush(palette.ActionLabel);
        FocusRing = Brush(palette.FocusRing);
        DisabledFill = Brush(palette.DisabledFill);
        DisabledLabel = Brush(palette.DisabledLabel);
        BarDisabledFill = Brush(palette.BarDisabledFill);
        BarDisabledLabel = Brush(palette.BarDisabledLabel);
        AlmanacMark = Brush(palette.Almanac.Mark);
        AlmanacText = Brush(palette.Almanac.Text);
    }

    public TillPalette Palette { get; }

    public TillLanguage Language { get; }

    public IBrush Page { get; }
    public IBrush Card { get; }
    public IBrush Tile { get; }
    public IBrush Border { get; }
    public IBrush BorderSubtle { get; }
    public IBrush Bar { get; }
    public IBrush BarText { get; }
    public IBrush BarLabel { get; }
    public IBrush BarKeyBorder { get; }
    public IBrush Text { get; }
    public IBrush TextSecondary { get; }
    public IBrush TextMuted { get; }
    public IBrush Collect { get; }
    public IBrush CollectLabel { get; }
    public IBrush Action { get; }
    public IBrush ActionLabel { get; }
    public IBrush FocusRing { get; }
    public IBrush DisabledFill { get; }
    public IBrush DisabledLabel { get; }
    public IBrush BarDisabledFill { get; }
    public IBrush BarDisabledLabel { get; }
    public IBrush AlmanacMark { get; }
    public IBrush AlmanacText { get; }

    /// <summary>A tone's edge, label and fill on a surface. Neutral has no edge and no fill.</summary>
    public (IBrush? Mark, IBrush Text, IBrush? Fill) ToneOnSurface(Tone tone) => tone switch
    {
        Tone.Warning => (Brush(Palette.Warning.Mark), Brush(Palette.Warning.Text), Palette.Warning.Fill is { } w ? Brush(w) : null),
        Tone.Critical => (Brush(Palette.Critical.Mark), Brush(Palette.Critical.Text), Palette.Critical.Fill is { } c ? Brush(c) : null),
        _ => (null, TextSecondary, null),
    };

    /// <summary>A tone on the bars, which are dark in both themes.</summary>
    public (IBrush? Mark, IBrush Text) ToneOnBar(Tone tone) => tone == Tone.Critical
        ? (Brush(Palette.BarCritical.Mark), Brush(Palette.BarCritical.Text))
        : (null, BarLabel);

    // ================================================================ faces

    /// <summary>The reading face: Archivo in French, with Plex Sans Arabic behind it for an Arabic product name; Plex Sans Arabic in Arabic.</summary>
    public FontFamily Sans(FontWeight weight) => Language == TillLanguage.Arabic
        ? new FontFamily(Face("IBMPlexSansArabic", "IBM Plex Sans Arabic", weight))
        : new FontFamily($"{Face("Archivo", "Archivo", weight)}, {Face("IBMPlexSansArabic", "IBM Plex Sans Arabic", weight)}");

    /// <summary>Figures, codes, quantities and French labels.</summary>
    public static FontFamily Mono(FontWeight weight) =>
        new(Face("IBMPlexMono", "IBM Plex Mono", weight == FontWeight.Bold ? FontWeight.SemiBold : weight));

    private static string Face(string file, string family, FontWeight weight)
    {
        var cut = weight switch
        {
            FontWeight.Bold => "Bold",
            FontWeight.SemiBold => "SemiBold",
            FontWeight.Medium => "Medium",
            _ => "Regular",
        };

        // IBM Plex Mono ships no Bold here (D-080); SemiBold stands in.
        var name = cut == "Regular" || (cut == "Bold" && file != "IBMPlexMono") ? family : $"{family} {cut}";
        return $"{Fonts}{file}-{cut}.ttf#{name}";
    }

    // ========================================================== type styles

    /// <summary><c>body</c>: Archivo 16/24.</summary>
    public TextBlock Body(string text, IBrush? brush = null, FontWeight weight = FontWeight.Normal) =>
        Words(text, 16, weight, brush ?? Text);

    /// <summary><c>body-sm</c>: Archivo 14/20.</summary>
    public TextBlock BodySmall(string text, IBrush? brush = null, FontWeight weight = FontWeight.Normal) =>
        Words(text, 14, weight, brush ?? TextSecondary);

    /// <summary>
    /// <c>label</c>: Plex Mono 12, capitals, tracked 0.08 em, in French; Plex Sans Arabic 12
    /// SemiBold, untracked, in Arabic, where capitals do not exist and tracking breaks the joins.
    /// </summary>
    public TextBlock Label(string text, IBrush? brush = null) => Language == TillLanguage.Arabic
        ? new TextBlock
        {
            Text = text,
            FontFamily = Sans(FontWeight.SemiBold),
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Foreground = brush ?? TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        }
        : new TextBlock
        {
            Text = text,
            FontFamily = Mono(FontWeight.Medium),
            FontWeight = FontWeight.Medium,
            FontSize = 12,
            LetterSpacing = 0.96,
            Foreground = brush ?? TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };

    /// <summary>
    /// A figure: Plex Mono, Latin digits, <b>always left to right</b> even inside an Arabic line
    /// (kit §2), so "3 320,80" is never read backwards.
    /// </summary>
    public static TextBlock Figure(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal) => new()
    {
        Text = text,
        FontFamily = Mono(weight),
        FontWeight = weight,
        FontSize = size,
        LineHeight = size <= 13 ? 20 : size <= 16 ? 24 : size <= 22 ? 28 : 48,
        Foreground = brush,
        FlowDirection = FlowDirection.LeftToRight,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// A sentence with figures in it (<c>data-inline</c>, kit §2): the words in the language's
    /// face and direction, each figure its own Plex Mono run, so the face changes mid-line.
    ///
    /// <para>
    /// <b>Why each figure is its own run, not just a different face.</b> Digits share Unicode's
    /// "common" script and take on the script of the text around them. Beside Arabic letters they
    /// are shaped as Arabic, with the Arabic face, and the narrow no-break space between their
    /// thousands disappears: "2 377,00 د.ج" came out as "2377.00". In a run of their own they stay
    /// Latin, and the bidirectional algorithm still orders the whole sentence right to left.
    /// </para>
    /// </summary>
    public TextBlock Prose(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal)
    {
        ArgumentNullException.ThrowIfNull(text);
        var block = new TextBlock
        {
            FontFamily = Sans(weight),
            FontWeight = weight,
            FontSize = size,
            Foreground = brush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var inlines = block.Inlines ??= [];
        var last = 0;
        foreach (Match figure in FigureInText().Matches(text))
        {
            if (figure.Index > last)
            {
                inlines.Add(new Run(text[last..figure.Index]));
            }

            inlines.Add(new Run(figure.Value) { FontFamily = Mono(weight) });
            last = figure.Index + figure.Length;
        }

        if (last < text.Length)
        {
            inlines.Add(new Run(text[last..]));
        }

        return block;
    }

    /// <summary>
    /// A figure as DisplayFigures writes one: digits grouped by U+202F, a comma, a true minus, a
    /// clock or a day, and a Latin currency code after a no-break space ("3 320,80 DA"). An Arabic
    /// currency stays with the words, in the words' face.
    /// </summary>
    [GeneratedRegex(@"\u2212?\d(?:[\d\u202F,:/]*\d)?(?:\u00A0[A-Z]{2,3}\b)?")]
    private static partial Regex FigureInText();

    private TextBlock Words(string text, double size, FontWeight weight, IBrush brush) => new()
    {
        Text = text,
        FontFamily = Sans(weight),
        FontWeight = weight,
        FontSize = size,
        LineHeight = size >= 16 ? 24 : 20,
        Foreground = brush,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // ================================================================ icons

    /// <summary>A Lucide outline, stroked 1.5 px on its 24 px grid and scaled to <paramref name="size"/>.</summary>
    public static Control Icon(string pathData, IBrush stroke, double size = 20) => new Viewbox
    {
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Canvas
        {
            Width = 24,
            Height = 24,
            Children =
            {
                new OutlinePath
                {
                    Data = Geometry.Parse(pathData),
                    Stroke = stroke,
                    StrokeThickness = 1.5,
                    StrokeLineCap = PenLineCap.Round,
                    StrokeJoin = PenLineJoin.Round,
                },
            },
        },
    };

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
