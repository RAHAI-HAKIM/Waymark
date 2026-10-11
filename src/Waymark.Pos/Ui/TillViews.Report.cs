using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Ui;

/// <summary>What the report's keys do: a row of the list touched (null for the X), and the way out.</summary>
public sealed record ReportActions(Action<long?> Open, Action Close);

/// <summary>
/// The X and Z reports (C2, D-112), in the ticket's place as the carnet is: it does not float, it is
/// too long for that. The list of closes on one side, the report beside it under its facts, its total
/// and its two keys at the foot. Built from the kit's pieces; no colour is named here (D-082).
/// </summary>
public static partial class TillViews
{
    public const string ReportCloseTag = "report-close";

    public const string ReportPrintTag = "report-print";

    /// <summary>The list's width beside the report.</summary>
    private const double ReportList = 200;

    public static Control Report(ReportView view, TillTheme theme, ReportActions actions)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(actions);
        var body = new DockPanel();

        // The heading: what it is, and whether it can still change.
        var chip = new Border
        {
            BorderBrush = theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(TillSizes.FieldRadius),
            Padding = new Thickness(8, 2),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = theme.Label(view.Chip, theme.TextSecondary),
        };
        var name = new StackPanel { Orientation = Orientation.Horizontal, Children = { Words(view.Title, 22, FontWeight.SemiBold, theme.Text, theme), chip } };
        var x = new TillKey(theme, KeyLook.Secondary, TillKey.Labelled(Centred(TillTheme.Icon(LucideIcons.X, theme.Text, 20)), view.CloseKey, theme.TextMuted), actions.Close, height: FloatingKey)
        {
            Tag = ReportCloseTag,
            Width = 72,
        };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(Docked(x, Dock.Right));
        head.Children.Add(name);
        body.Children.Add(Docked(head, Dock.Top));

        if (view.Facts.Count > 0)
        {
            var facts = new UniformGrid { Columns = theme.Narrow ? 3 : view.Facts.Count, Margin = new Thickness(0, 0, 0, 10) };
            foreach (var fact in view.Facts)
            {
                facts.Children.Add(new StackPanel
                {
                    Spacing = 2,
                    Margin = new Thickness(0, 0, 8, theme.Narrow ? 6 : 0),
                    Children = { theme.Label(fact.Label, theme.TextSecondary), Wrapped(theme.BodySmall(fact.Value, theme.Text)) },
                });
            }

            body.Children.Add(Docked(facts, Dock.Top));
        }

        // The foot: the total, then "Imprimer", which waits for the printer, then the way out.
        var close = new TillKey(
            theme, KeyLook.Primary,
            TillKey.Labelled(Centred(Words(view.Close, 15, FontWeight.SemiBold, theme.ActionLabel, theme)), view.CloseKey, theme.ActionLabel),
            actions.Close, true, 52)
        { Tag = view.Close, Padding = new Thickness(16, 0) };
        var print = new TillKey(
            theme, KeyLook.Secondary,
            new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Words(view.Print, 14, FontWeight.SemiBold, theme.DisabledLabel, theme),
                    theme.Label(view.PrintNote, theme.DisabledLabel),
                },
            },
            () => { }, false, 52)
        { Tag = ReportPrintTag, Padding = new Thickness(14, 0), Margin = new Thickness(0, 0, 8, 0) };
        var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        foot.Children.Add(Docked(close, Dock.Right));
        foot.Children.Add(Docked(print, Dock.Right));
        foot.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { theme.Label(view.TotalLabel, theme.TextSecondary), TillTheme.Figure(view.Total, 22, theme.Text) },
        });
        body.Children.Add(Docked(foot, Dock.Bottom));

        // The list: the session's X, then the till's closes, the newest first.
        var list = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 12, 0), Width = ReportList };
        if (view.Session is { } session)
        {
            list.Children.Add(theme.Label(view.SessionTitle, theme.TextSecondary));
            list.Children.Add(ListRow(theme, session, actions));
        }

        list.Children.Add(theme.Label(view.ClosesTitle, theme.TextSecondary));
        foreach (var row in view.Closes)
        {
            list.Children.Add(ListRow(theme, row, actions));
        }

        list.Children.Add(Wrapped(theme.BodySmall(view.ListNote, theme.TextSecondary)));
        body.Children.Add(Docked(
            new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list }, Dock.Left));

        // The report, read from the top.
        var sections = new StackPanel { Spacing = 10 };
        if (view.Message is { } message)
        {
            sections.Children.Add(Refusal(theme, message));
        }

        // The flags across; then what is about tickets beside what is about cash, as the board draws
        // them. One column on a narrow screen.
        var first = new StackPanel { Spacing = 10 };
        var second = new StackPanel { Spacing = 10 };
        foreach (var section in view.Sections)
        {
            (section.Wide ? sections : section.Second && !theme.Narrow ? second : first).Children.Add(Section(theme, section));
        }

        if (second.Children.Count > 0)
        {
            var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
            columns.Children.Add(first);
            Grid.SetColumn(second, 2);
            columns.Children.Add(second);
            sections.Children.Add(columns);
        }
        else
        {
            sections.Children.Add(first);
        }

        body.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sections, Tag = view });

        var card = Card(theme, null, body);
        card.Padding = new Thickness(16);
        return card;
    }

    private static TillKey ListRow(TillTheme theme, ReportListRow row, ReportActions actions)
    {
        var face = new StackPanel
        {
            Margin = new Thickness(10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Words(row.Label, 14, FontWeight.SemiBold, theme.Text, theme), theme.BodySmall(row.Detail, theme.TextSecondary) },
        };
        var number = row.Number;
        return new TillKey(theme, row.Selected ? KeyLook.Chosen : KeyLook.Secondary, face, () => actions.Open(number), true, 48) { Tag = row };
    }

    private static Border Section(TillTheme theme, ReportSection section)
    {
        var stack = new StackPanel { Spacing = 4 };
        var title = new DockPanel();
        if (section.Aside is { } aside)
        {
            title.Children.Add(Docked(theme.BodySmall(aside, theme.TextSecondary), Dock.Right));
        }

        title.Children.Add(theme.Label(section.Title, theme.TextSecondary));
        stack.Children.Add(title);

        if (section.Columns.Count == 2 && section.Lines.Count > 0)
        {
            stack.Children.Add(ReportRow(theme, null, theme.Label(section.Columns[0], theme.TextMuted), theme.Label(section.Columns[1], theme.TextMuted), false));
        }

        foreach (var line in section.Lines)
        {
            if (line.Tone is { } tone)
            {
                // A flag: its label in its tone, then what it is about, on the plain ground.
                var (_, ink, fill) = theme.ToneOnSurface(tone);
                var label = new Border
                {
                    Background = fill,
                    CornerRadius = new CornerRadius(TillSizes.FieldRadius),
                    Padding = new Thickness(6, 1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = theme.Label(line.Label, ink),
                };
                stack.Children.Add(new Border
                {
                    BorderBrush = theme.BorderSubtle,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(0, 6),
                    Tag = line,
                    Child = new StackPanel { Spacing = 2, Children = { label, Wrapped(theme.BodySmall(line.Value, theme.Text)) } },
                });
                continue;
            }

            var weight = line.Strong ? FontWeight.SemiBold : FontWeight.Normal;
            var row = ReportRow(
                theme,
                Words(line.Label, 14, weight, theme.Text, theme),
                line.Count is { } count ? TillTheme.Figure(count, 14, theme.TextSecondary) : null,
                TillTheme.Figure(line.Value, line.Strong ? 16 : 14, theme.Text, weight),
                true);
            row.Tag = line;
            stack.Children.Add(row);
        }

        if (section.Message is { } message)
        {
            stack.Children.Add(Refusal(theme, message, section.MessageFigure));
        }

        if (section.Note is { } note)
        {
            stack.Children.Add(Wrapped(theme.BodySmall(note, theme.TextSecondary)));
        }

        return new Border
        {
            BorderBrush = theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(TillSizes.KeyRadius),
            Padding = new Thickness(12, 8),
            Tag = section,
            Child = stack,
        };
    }

    /// <summary>A line of a section: its words, then its count and its value in two columns that line up down the block.</summary>
    private static Border ReportRow(TillTheme theme, Control? label, Control? count, Control value, bool ruled)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,70,130") };
        if (label is not null)
        {
            grid.Children.Add(label);
        }

        if (count is not null)
        {
            count.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(count, 1);
            grid.Children.Add(count);
        }

        value.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return new Border
        {
            BorderBrush = theme.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, ruled ? 1 : 0),
            Padding = new Thickness(0, ruled ? 5 : 0),
            Child = grid,
        };
    }
}
