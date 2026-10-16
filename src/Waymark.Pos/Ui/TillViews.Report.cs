using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Ui;

/// <summary>
/// What the report's keys do: a row of the list touched (null for the X), the way out, a tab touched
/// (true for "Par caissier"), and the key that asks for the owner's PIN to open the other columns (C3).
/// </summary>
public sealed record ReportActions(Action<long?> Open, Action Close, Action<bool> Tab, Action Unlock);

/// <summary>
/// The X and Z reports (C2, D-112), in the ticket's place as the carnet is: it does not float, it is
/// too long for that. The list of closes on one side, the report beside it under its facts, its total
/// and its two keys at the foot. Built from the kit's pieces; no colour is named here (D-082).
/// </summary>
public static partial class TillViews
{
    public const string ReportCloseTag = "report-close";

    public const string ReportPrintTag = "report-print";

    public const string CashierUnlockTag = "report-cashiers-unlock";

    /// <summary>The list's width beside the report.</summary>
    private const double ReportList = 200;

    /// <summary>How many people's columns fit without scrolling sideways (the board, 2A): past it, they scroll between the labels and the session.</summary>
    private const int CashiersAbreast = 6;

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

        if (theme.Narrow)
        {
            // No room for a column of reports beside six people (the board, 2A): the list is a strip
            // above the report, which scrolls sideways when the closes are many.
            list.Orientation = Orientation.Horizontal;
            list.Width = double.NaN;
            list.Margin = new Thickness(0, 0, 0, 8);
            foreach (var row in list.Children.OfType<TillKey>())
            {
                row.Width = 150;
            }

            foreach (var label in list.Children.OfType<TextBlock>())
            {
                label.VerticalAlignment = VerticalAlignment.Center;
                label.Margin = new Thickness(0, 0, 4, 0);
            }

            body.Children.Add(Docked(
                new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = list },
                Dock.Top));
        }
        else
        {
            list.Children.Add(Wrapped(theme.BodySmall(view.ListNote, theme.TextSecondary)));
            body.Children.Add(Docked(
                new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list }, Dock.Left));
        }

        // The tabs (C3): the synthesis, and the same report by cashier, with a padlock when not all of it is the reader's.
        if (view.Tabs.Count > 0)
        {
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var tab in view.Tabs)
            {
                var face = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Margin = new Thickness(12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { Words(tab.Label, 14, tab.Selected ? FontWeight.SemiBold : FontWeight.Normal, tab.Selected ? theme.Text : theme.TextSecondary, theme) },
                };
                if (tab.Locked)
                {
                    face.Children.Add(TillTheme.Icon(LucideIcons.Lock, theme.TextSecondary, 14));
                }

                var which = tab.ByCashier;
                tabs.Children.Add(new Border
                {
                    // The open tab is underlined in the operator's colour, and says so by its weight first.
                    BorderBrush = tab.Selected ? theme.Action : null,
                    BorderThickness = new Thickness(0, 0, 0, 2),
                    Child = new TillKey(theme, KeyLook.Ghost, face, () => actions.Tab(which), true, 40) { Tag = tab },
                });
            }

            body.Children.Add(Docked(
                new Border { BorderBrush = theme.BorderSubtle, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 10), Child = tabs },
                Dock.Top));
        }

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

        if (view.Cashiers is { } table)
        {
            sections.Children.Add(Cashiers(theme, table, actions));
        }
        else if (second.Children.Count > 0)
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

    /// <summary>
    /// The report by cashier (C3, D-118): the labels, a column per person, the session's last. Three
    /// stacks of cells of one height, so the lines stay level when the people's columns scroll
    /// sideways between the other two. Without the right, the reader's own column and one closed frame.
    /// </summary>
    private static Border Cashiers(TillTheme theme, CashierTable table, ReportActions actions)
    {
        var labelWidth = theme.Narrow ? 172.0 : 200.0;
        var cellWidth = theme.Narrow ? 96.0 : 112.0;
        var sessionWidth = cellWidth + 12;
        const double head = 46;
        const double title = 34;
        const double line = 31;

        Border Cell(Control? child, double width, double height, bool ruled, bool session) => new()
        {
            Width = width,
            Height = height,
            Background = session ? theme.Tile : null,
            BorderBrush = theme.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, ruled ? 1 : 0),
            Padding = new Thickness(session ? 8 : 0, 0, 8, 0),
            Child = child,
        };

        // One column of the table: its head, then a blank under each group's title and a figure on each line.
        StackPanel Column(int place)
        {
            var column = new StackPanel();
            var top = table.Heads[place];
            var who = Words(top.Name, 13, FontWeight.SemiBold, theme.Text, theme);
            var role = theme.BodySmall(top.Detail, theme.TextSecondary);
            foreach (var words in new[] { who, role })
            {
                words.HorizontalAlignment = HorizontalAlignment.Right;
                words.TextTrimming = TextTrimming.CharacterEllipsis;
            }

            column.Children.Add(Cell(
                new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { who, role } },
                top.Session ? sessionWidth : cellWidth, head, true, top.Session));
            foreach (var group in table.Groups)
            {
                column.Children.Add(Cell(null, top.Session ? sessionWidth : cellWidth, title, true, false));
                foreach (var row in group.Rows)
                {
                    var figure = TillTheme.Figure(row.Cells[place], 13, theme.Text, row.Strong || top.Session ? FontWeight.SemiBold : FontWeight.Normal);
                    figure.HorizontalAlignment = HorizontalAlignment.Right;
                    figure.VerticalAlignment = VerticalAlignment.Center;
                    column.Children.Add(Cell(figure, top.Session ? sessionWidth : cellWidth, line, true, top.Session));
                }
            }

            return column;
        }

        var labels = new StackPanel();
        labels.Children.Add(Cell(null, labelWidth, head, true, false));
        foreach (var group in table.Groups)
        {
            // A group's title is longer than the labels' column and runs over the figures' blanks
            // beside it: on a canvas, which neither clips it nor gives it the column's width.
            var name = theme.Label(group.Title, theme.TextSecondary);
            Canvas.SetLeft(name, 0);
            Canvas.SetBottom(name, 6);
            labels.Children.Add(Cell(new Canvas { ClipToBounds = false, Children = { name } }, labelWidth, title, true, false));
            foreach (var row in group.Rows)
            {
                var words = Words(row.Label, 13, row.Strong ? FontWeight.SemiBold : FontWeight.Normal, theme.Text, theme);
                words.VerticalAlignment = VerticalAlignment.Center;
                words.TextTrimming = TextTrimming.CharacterEllipsis;
                var cell = Cell(words, labelWidth, line, true, false);
                cell.Tag = row;
                labels.Children.Add(cell);
            }
        }

        var people = new StackPanel { Orientation = Orientation.Horizontal };
        StackPanel? session = null;
        for (var place = 0; place < table.Heads.Count; place++)
        {
            if (table.Heads[place].Session)
            {
                session = Column(place);
            }
            else
            {
                people.Children.Add(Column(place));
            }
        }

        var body = new DockPanel { LastChildFill = table.Lock is not null || people.Children.Count > CashiersAbreast, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (table.Groups.Count > 0)
        {
            body.Children.Add(Docked(labels, Dock.Left));
        }

        if (table.Lock is { } closed)
        {
            // The reader's own column, then one closed frame for everybody else's and the session's (the board, 2B).
            if (people.Children.Count > 0)
            {
                body.Children.Add(Docked(people, Dock.Left));
            }

            var key = new TillKey(
                theme, KeyLook.Primary,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { Words(closed.Key, 15, FontWeight.SemiBold, theme.ActionLabel, theme), TillTheme.Icon(LucideIcons.Lock, theme.ActionLabel, 16) },
                },
                actions.Unlock, true, 48)
            { Tag = CashierUnlockTag, Padding = new Thickness(20, 0), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            var title1 = Wrapped(Words(closed.Title, 15, FontWeight.SemiBold, theme.Text, theme));
            title1.TextAlignment = TextAlignment.Center;
            var detail = Wrapped(theme.BodySmall(closed.Detail, theme.TextSecondary));
            detail.TextAlignment = TextAlignment.Center;
            body.Children.Add(new Border
            {
                BorderBrush = theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(TillSizes.KeyRadius),
                Margin = new Thickness(people.Children.Count > 0 || table.Groups.Count > 0 ? 12 : 0, 0, 0, 0),
                Padding = new Thickness(24, 48),
                MinHeight = 240,
                Tag = closed,
                Child = new StackPanel
                {
                    // At the top of its frame: the frame is as tall as the column beside it, and its
                    // key must be on the screen without scrolling.
                    Spacing = 8,
                    MaxWidth = 380,
                    VerticalAlignment = VerticalAlignment.Top,
                    Children = { Centred(TillTheme.Icon(LucideIcons.Lock, theme.TextSecondary, 26)), title1, detail, key },
                },
            });
        }
        else if (people.Children.Count > CashiersAbreast)
        {
            // Seven people and more: theirs scroll sideways; the labels and the session stay in place.
            if (session is not null)
            {
                body.Children.Add(Docked(session, Dock.Right));
            }

            body.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = people,
            });
        }
        else
        {
            body.Children.Add(Docked(people, Dock.Left));
            if (session is not null)
            {
                body.Children.Add(Docked(session, Dock.Left));
            }
        }

        var stack = new StackPanel { Spacing = 10, Children = { body } };
        if (table.Lock is null)
        {
            stack.Children.Add(Wrapped(theme.BodySmall(table.Note, theme.TextSecondary)));
        }

        return new Border { Tag = table, Child = stack };
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

            if (line.Stacked)
            {
                stack.Children.Add(new Border
                {
                    BorderBrush = theme.BorderSubtle,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(0, 5),
                    Tag = line,
                    Child = new StackPanel
                    {
                        Spacing = 2,
                        Children = { Words(line.Label, 14, FontWeight.SemiBold, theme.Text, theme), Wrapped(theme.BodySmall(line.Value, theme.TextSecondary)) },
                    },
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
