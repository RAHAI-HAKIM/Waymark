using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Ui;

/// <summary>What the cashier can do from the screen. The window wires these to the session.</summary>
public sealed record TillActions(
    Action<string> SelectLine,
    Action RemoveSelected,
    Action Collect,
    Action NewSale,
    Action<string, string> Accept,
    Action<string> Dismiss,
    Action NextCard,
    Action AcknowledgeUnconfirmed,
    Action<string, int> SetCount,
    Action<Operation> Operate,
    Action<string> ResumeParked,
    Action<string> ResumeDraft,
    Action CloseDrafts);

/// <summary>
/// The G1 regions, each drawn from its part of <see cref="TillScreen"/> and nothing else (kit §5).
/// Nothing here decides: a label, a tone, whether a key is available — all of it arrives in the
/// model, where it is tested.
/// </summary>
public static partial class TillViews
{
    // ================================================================ top bar

    /// <param name="switchCashier">What touching the staff chip does ("Changer de caissier", A5); null draws it inert.</param>
    /// <param name="resumeParked">What touching a ticket on hold does (B2); null draws the tabs inert.</param>
    public static Control TopBar(TopBar top, TillTheme theme, Action? switchCashier = null, Action<string>? resumeParked = null)
    {
        var start = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        start.Children.Add(Words("Waymark", 16, FontWeight.SemiBold, theme.BarText, theme));
        if (top.Place is { } place)
        {
            start.Children.Add(Words("·", 14, FontWeight.Normal, theme.BarLabel, theme));
            start.Children.Add(Words(place, 14, FontWeight.Normal, theme.BarLabel, theme));
        }

        // The open ticket is a tab joined to the page below it (kit §9, "onglets dans la barre haute").
        // The sign-in screen has no ticket, and no tab.
        var tab = top.Tab is null ? null : new Border
        {
            Background = theme.Page,
            CornerRadius = new CornerRadius(TillSizes.KeyRadius, TillSizes.KeyRadius, 0, 0),
            Padding = new Thickness(16, 0),
            Margin = new Thickness(24, 12, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Words(top.Tab.Title, 14, FontWeight.SemiBold, theme.Text, theme),
                    theme.Prose(top.Tab.Detail, 12, theme.TextMuted),
                },
            },
        };

        var end = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        end.Children.Add(ConnectionChip(top.Connection, theme));
        if (top.Staff is { } staff)
        {
            end.Children.Add(Words("·", 14, FontWeight.Normal, theme.BarLabel, theme));

            // A bar key: touching it hands the till to somebody else (A5). Whether that is allowed
            // now is the session's to say, not the chip's.
            end.Children.Add(new TillKey(theme, KeyLook.Bar, new Border
            {
                Padding = new Thickness(0, 4),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 16,
                    Children =
                    {
                        new StackPanel
                        {
                            Children =
                            {
                                Words(staff.Name, 14, FontWeight.SemiBold, theme.BarText, theme),
                                theme.Label(staff.Role, theme.BarLabel),
                            },
                        },
                        TillTheme.Icon(LucideIcons.ChevronDown, theme.BarLabel, 16),
                    },
                },
            }, switchCashier, available: true, height: 44) { VerticalAlignment = VerticalAlignment.Center });
        }

        end.Children.Add(Words("·", 14, FontWeight.Normal, theme.BarLabel, theme));
        end.Children.Add(TillTheme.Figure(top.Clock, 14, theme.BarText, FontWeight.Medium));

        // The ticket on screen, then the tickets on hold, on the bar itself (G1 board): a touch brings
        // one back, and the ticket on screen, if it has lines, goes on hold in its place.
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (tab is not null)
        {
            tabs.Children.Add(tab);
        }

        foreach (var parked in top.Parked)
        {
            var face = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Words(parked.Title, 14, FontWeight.SemiBold, theme.BarText, theme),
                    theme.Prose(parked.Detail, 12, theme.BarLabel),
                },
            };
            var id = parked.Id;
            tabs.Children.Add(new TillKey(theme, KeyLook.Ghost, face, resumeParked is null ? null : () => resumeParked(id), available: true, height: 44)
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(8, 0, 0, 2),
                Tag = parked,
            });
        }

        // The tabs take what the bar has left between the store's name and the clock, and scroll
        // sideways when there are more than fit: a ticket on hold is never cut off the bar, and
        // the clock and the staff chip are never pushed off it. No scroll bar: Fluent draws it in
        // its own grey; the tab cut at the edge says there is more, and a swipe or the wheel shows it.
        var tabStrip = new ScrollViewer
        {
            Content = tabs,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 12, 0),
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(TillSizes.Margin, 0) };
        grid.Children.Add(Cell(start, 0));
        grid.Children.Add(Cell(tabStrip, 1));
        grid.Children.Add(Cell(end, 2));

        return new Border { Background = theme.Bar, Height = TillSizes.TopBar, Child = grid };
    }

    private static Control ConnectionChip(Connection connection, TillTheme theme)
    {
        var (mark, text) = theme.ToneOnBar(connection.Tone);
        if (mark is null)
        {
            // Online is the absence of a problem: a label, no colour, no chip (CLAUDE.md §6).
            return theme.Label(connection.Label, text);
        }

        return new Border
        {
            BorderBrush = mark,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(TillSizes.FieldRadius),
            Padding = new Thickness(8, 4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { TillTheme.Icon(LucideIcons.WifiOff, text, 14), theme.Label(connection.Label, text) },
            },
        };
    }

    // ============================================================ notice slot

    /// <summary>
    /// The notice slot under the search field: fixed height, never above the cart, so a notice
    /// moves no line (G1, "Avis"). Neutral sits on the strip; a warning or critical gets its fill
    /// and its 3 px edge, and always its word first.
    /// </summary>
    public static Control Notice(NoticeLine notice, TillTheme theme)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        var (mark, text, fill) = theme.ToneOnSurface(notice.Tone);

        line.Children.Add(theme.Label(notice.Label, notice.Tone == Tone.Neutral ? theme.TextSecondary : text));
        if (notice.Text.Length > 0)
        {
            line.Children.Add(theme.Prose(notice.Text, 14, notice.Tone == Tone.Neutral ? theme.TextSecondary : theme.Text));
        }

        return new Border
        {
            Height = 36,
            Margin = new Thickness(0, 8, 0, 0),
            Background = fill ?? (mark is null ? null : theme.Card),
            BorderBrush = mark,
            BorderThickness = mark is null ? default : new Thickness(0, TillSizes.SignalRule, 0, 0),
            CornerRadius = new CornerRadius(TillSizes.FieldRadius),
            Padding = new Thickness(mark is null ? 4 : 12, 0),
            Child = line,
        };
    }

    // ================================================================= cart
    //
    // The cart is drawn in three parts because the window keeps its scroll viewer, and the panel of
    // rows inside it, from one frame to the next: replacing either puts a long ticket back at its
    // first line (Avalonia resets the offset when a scroll viewer's content is swapped). Only the
    // rows are drawn again; the window decides nothing about where the ticket stands (CartFollow).

    /// <summary>The column heads over the ticket.</summary>
    public static Control CartHeader(CartView cart, TillTheme theme)
    {
        var header = RowGrid();
        header.Height = 32;
        header.Children.Add(Cell(theme.Label(cart.Columns[0]), 0));
        header.Children.Add(Cell(theme.Label(cart.Columns[1]), 1));
        header.Children.Add(Cell(End(theme.Label(cart.Columns[2])), 2));
        header.Children.Add(Cell(End(theme.Label(cart.Columns[3])), 3));

        return new Border
        {
            BorderBrush = theme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 0),
            Child = header,
        };
    }

    /// <summary>
    /// The ticket's lines, in scan order, with the selected line's actions beneath it (kit §6). Each
    /// row carries its <see cref="Screen.LineRow"/> as its <c>Tag</c> and the action bar its
    /// <see cref="LineActions"/>, which is how the window finds the line it brings into view.
    /// </summary>
    public static IEnumerable<Control> CartRows(CartView cart, TillTheme theme, TillActions actions)
    {
        ArgumentNullException.ThrowIfNull(cart);

        foreach (var line in cart.Lines)
        {
            yield return LineRow(line, theme, actions);
            if (line.Selected && cart.Actions is { } lineActions)
            {
                yield return LineActionBar(lineActions, theme, actions);
            }
        }
    }

    /// <summary>An empty ticket: what to do, in the middle of the space the lines will take.</summary>
    public static Control CartEmpty(EmptyState empty, TillTheme theme) => new StackPanel
    {
        Spacing = 8,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new Border { HorizontalAlignment = HorizontalAlignment.Center, Child = TillTheme.Icon(LucideIcons.ScanBarcode, theme.TextMuted, 40) },
            Centred(theme.Body(empty.Title, theme.Text, FontWeight.SemiBold)),
            Centred(theme.BodySmall(empty.Hint)),
        },
    };

    private static Border LineRow(LineRow line, TillTheme theme, TillActions actions)
    {
        var ink = line.Struck ? theme.TextMuted : theme.Text;
        var strike = line.Struck ? TextDecorations.Strikethrough : null;

        // The name trims before it can reach the price column; the chips keep their width beside
        // it, word first (kit §5).
        var article = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        // Medium at rest, SemiBold when selected: the name is what the cashier reads (Hakim, 23/09).
        var name = theme.Body(line.Article, ink, line.Selected ? FontWeight.SemiBold : FontWeight.Medium);
        name.TextDecorations = strike;
        article.Children.Add(name);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 0, 0, 0) };
        foreach (var chip in line.Chips)
        {
            chips.Children.Add(Chip(chip, theme));
        }

        article.Children.Add(Cell(chips, 1));

        var grid = RowGrid();
        grid.Children.Add(Cell(Start(Struck(TillTheme.Figure(line.Quantity, 16, ink), strike)), 0));
        grid.Children.Add(Cell(article, 1));
        grid.Children.Add(Cell(End(Struck(TillTheme.Figure(line.UnitPrice, 16, ink), strike)), 2));
        grid.Children.Add(Cell(End(Struck(TillTheme.Figure(line.Total, 16, ink, FontWeight.Medium), strike)), 3));

        var row = new Border
        {
            MinHeight = TillSizes.CartRow,
            // Transparent rather than none: a border with no ground only takes a touch where it
            // has text, so a tap between the name and the price did nothing.
            Background = line.Selected ? theme.Tile : Brushes.Transparent,
            BorderBrush = theme.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 0),
            Child = grid,
            Tag = line,
        };

        if (!line.Struck)
        {
            row.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            row.Tapped += (_, _) => actions.SelectLine(line.LineId);
        }

        return row;
    }

    /// <summary>
    /// The selected line's actions, opened beneath it; the other lines stay put (kit §6). The
    /// stepper first, as the board draws it: − stops at one, and the last unit goes with
    /// "Retirer la ligne".
    /// </summary>
    private static Border LineActionBar(LineActions lineActions, TillTheme theme, TillActions actions)
    {
        var id = lineActions.LineId;
        var count = lineActions.Count;
        var minusInk = lineActions.MayDecrease ? theme.Text : theme.DisabledLabel;
        var stepper = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            // − then +, left to right in Arabic too: a count goes up to the right on every till.
            FlowDirection = FlowDirection.LeftToRight,
            Children =
            {
                new TillKey(theme, KeyLook.Secondary, Centred(TillTheme.Icon(LucideIcons.Minus, minusInk, 18)), () => actions.SetCount(id, count - 1), lineActions.MayDecrease, TillSizes.LineKey)
                { Width = 48 },
                QuantityField(lineActions, theme),
                new TillKey(theme, KeyLook.Secondary, Centred(TillTheme.Icon(LucideIcons.Plus, theme.Text, 18)), () => actions.SetCount(id, count + 1), height: TillSizes.LineKey)
                { Width = 48 },
            },
        };

        var remove = new TillKey(
            theme,
            KeyLook.Secondary,
            TillKey.Labelled(
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { TillTheme.Icon(LucideIcons.Trash2, theme.TextMuted, 18), theme.Body(lineActions.Remove, theme.Text, FontWeight.SemiBold) },
                },
                lineActions.RemoveKey,
                theme.TextMuted),
            actions.RemoveSelected,
            height: TillSizes.LineKey)
        { HorizontalAlignment = HorizontalAlignment.Right };

        var bar = new DockPanel();
        bar.Children.Add(Docked(stepper, Dock.Left));
        bar.Children.Add(remove);

        return new Border
        {
            Background = theme.Tile,
            Padding = new Thickness(12, 2, 12, 4),
            BorderBrush = theme.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar,
            Tag = lineActions,
        };
    }

    /// <summary>
    /// The count between − and +, which takes a number typed on the keyboard (Hakim, 25/09): touched,
    /// it selects its text so the typing replaces it, and Entrée confirms (the window reads it through
    /// <see cref="QuantityEntry"/>). Its <c>Tag</c> is the line's <see cref="LineActions"/>, which is
    /// how the window knows which line Entrée is for. No clipboard menu, as the search field (F-26).
    /// </summary>
    private static TextBox QuantityField(LineActions lineActions, TillTheme theme)
    {
        var field = new TextBox
        {
            Text = lineActions.Quantity,
            Width = 72,
            Height = TillSizes.LineKey,
            Margin = new Thickness(0, 2),
            MinHeight = 0,
            Padding = new Thickness(4, 0),
            FontFamily = TillTheme.Mono(FontWeight.Medium),
            FontSize = 17,
            Foreground = theme.Text,
            CaretBrush = theme.Text,
            SelectionBrush = theme.Action,
            SelectionForegroundBrush = theme.ActionLabel,
            Background = theme.Card,
            BorderBrush = theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(TillSizes.KeyRadius),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ContextFlyout = null,
            ContextMenu = null,
            Tag = lineActions,
        };

        // Fluent repaints a field's ground and edge on hover and focus with its own colours: these
        // keep the palette's, and the focused edge is the focus ring, as on every key.
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
        {
            field.Resources[key] = theme.Card;
        }

        field.Resources["TextControlBorderBrush"] = theme.Border;
        field.Resources["TextControlBorderBrushPointerOver"] = theme.Border;
        field.Resources["TextControlBorderBrushFocused"] = theme.FocusRing;
        // After the press that focused it: the press itself puts the caret where the finger landed,
        // which undid a selection made on focus, and "240" typed onto "1" gave 2401.
        field.GotFocus += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(field.SelectAll);
        return field;
    }

    private static Border Chip(Chip chip, TillTheme theme)
    {
        var (mark, text, fill) = theme.ToneOnSurface(chip.Tone);
        return new Border
        {
            Background = fill,
            BorderBrush = mark ?? theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(TillSizes.FieldRadius),
            Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = theme.Label(chip.Label, chip.Tone == Tone.Neutral ? theme.TextSecondary : text),
        };
    }

    // ================================================================= rail

    public static Control Rail(Rail rail, TillTheme theme, TillActions actions) => rail switch
    {
        Screen.Rail.Paid paid => PaidPanel(paid, theme),
        Screen.Rail.Unconfirmed unconfirmed => UnconfirmedCard(unconfirmed, theme, actions),
        Screen.Rail.Rest rest => RestRail(rest, theme, actions),
        Screen.Rail.Drafts drafts => DraftsPanel(drafts, theme, actions),
        _ => new Border(),
    };

    /// <summary>
    /// At rest the rail has its operation keys at the top, three to a row as the G1 board sets them,
    /// keeps its space for the parts B1–B10 will add, and holds the Almanac slot at its foot, the
    /// only cyan on the screen (kit §7).
    /// </summary>
    private static DockPanel RestRail(Rail.Rest rest, TillTheme theme, TillActions actions)
    {
        var dock = new DockPanel { LastChildFill = true };

        var keys = new UniformGrid { Columns = 3, Margin = new Thickness(-2, -2, -2, 12) };
        foreach (var operation in rest.Operations)
        {
            keys.Children.Add(OperationTile(operation, theme, actions));
        }

        dock.Children.Add(Docked(keys, Dock.Top));
        if (rest.Almanac is { } slot)
        {
            dock.Children.Add(Docked(Almanac(slot, theme, actions), Dock.Bottom));
        }

        dock.Children.Add(new Border());
        return dock;
    }

    /// <summary>An operation key: its icon, its word, its F key in the corner (G1 board). Unavailable is its own look.</summary>
    private static TillKey OperationTile(OperationKey operation, TillTheme theme, TillActions actions)
    {
        var ink = operation.Enabled ? theme.Text : theme.DisabledLabel;
        var icon = operation.Operation switch
        {
            Operation.Park => LucideIcons.Pause,
            Operation.CancelTicket => LucideIcons.Ban,
            _ => LucideIcons.Archive,
        };

        var face = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(0, 8),
            Children =
            {
                Start(TillTheme.Icon(icon, ink, 18)),
                Words(operation.Label, 14, FontWeight.SemiBold, ink, theme),
            },
        };

        var kind = operation.Operation;
        return new TillKey(theme, KeyLook.Pad, TillKey.Labelled(face, operation.Key, theme.TextMuted), () => actions.Operate(kind), operation.Enabled, height: 64)
        {
            Tag = operation,
        };
    }

    /// <summary>
    /// The tickets cancelled today (D-087): a card in the rail, like the paid ticket's, each ticket
    /// with who cancelled it and when, what it held, and "Reprendre". "Fermer" gives the rail back.
    /// </summary>
    private static Border DraftsPanel(Rail.Drafts drafts, TillTheme theme, TillActions actions)
    {
        var list = new StackPanel();
        foreach (var draft in drafts.Rows)
        {
            var id = draft.Id;
            var words = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Words(draft.Title, 15, FontWeight.SemiBold, theme.Text, theme),
                    theme.Prose(draft.Detail, 13, theme.TextSecondary),
                },
            };
            var row = new DockPanel { Margin = new Thickness(0, 8) };
            row.Children.Add(Docked(
                new TillKey(theme, KeyLook.Secondary, theme.Body(draft.Resume, theme.Text, FontWeight.SemiBold), () => actions.ResumeDraft(id))
                { VerticalAlignment = VerticalAlignment.Center },
                Dock.Right));
            row.Children.Add(words);
            list.Children.Add(new Border
            {
                BorderBrush = theme.BorderSubtle,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = row,
                Tag = draft,
            });
        }

        if (drafts.Empty is { } empty)
        {
            list.Children.Add(Wrapped(theme.BodySmall(empty, theme.TextSecondary)));
        }

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(Docked(new TillKey(theme, KeyLook.Ghost, theme.Body(drafts.Close, theme.TextSecondary), actions.CloseDrafts), Dock.Right));
        head.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4,
            Children = { theme.Label(drafts.Label, theme.TextSecondary), Wrapped(theme.BodySmall(drafts.Hint, theme.TextSecondary)) },
        });

        var body = new DockPanel();
        body.Children.Add(Docked(head, Dock.Top));
        body.Children.Add(new ScrollViewer { Content = list });

        var card = Card(theme, null, body);
        card.VerticalAlignment = VerticalAlignment.Stretch;
        return card;
    }

    private static Border PaidPanel(Rail.Paid paid, TillTheme theme)
    {
        var figures = new StackPanel();
        foreach (var figure in paid.Figures)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 36 };
            row.Children.Add(Cell(theme.BodySmall(figure.Label, theme.Text), 0));
            row.Children.Add(Cell(TillTheme.Figure(figure.Value, 16, theme.Text), 1));
            figures.Children.Add(new Border { BorderBrush = theme.BorderSubtle, BorderThickness = new Thickness(0, 0, 0, 1), Child = row });
        }

        return Card(theme, null, new DockPanel
        {
            Children =
            {
                Docked(new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        theme.Label(paid.Label),
                        Words(paid.Title, 24, FontWeight.SemiBold, theme.Text, theme),
                        theme.Prose(paid.Subtitle, 14, theme.TextSecondary),
                        new Border { Margin = new Thickness(0, 12, 0, 0), Child = figures },
                    },
                }, Dock.Top),
                Docked(theme.BodySmall(paid.Footer), Dock.Bottom),
                new Border(),
            },
        });
    }

    /// <summary>
    /// A sale with no answer: critical, with room to say what to do. No "Réessayer" — nothing yet
    /// makes a second request harmless, and the first may have been recorded (O-27). One key, the
    /// cashier's word that they have checked, which re-opens Encaisser (D-085).
    /// </summary>
    private static Border UnconfirmedCard(Rail.Unconfirmed unconfirmed, TillTheme theme, TillActions actions)
    {
        var (mark, text, fill) = theme.ToneOnSurface(Tone.Critical);
        return new Border
        {
            VerticalAlignment = VerticalAlignment.Top,
            Background = fill ?? theme.Card,
            BorderBrush = mark,
            BorderThickness = new Thickness(0, TillSizes.SignalRule, 0, 0),
            CornerRadius = new CornerRadius(TillSizes.CardRadius),
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    theme.Label(unconfirmed.Label, text),
                    Wrapped(theme.Body(unconfirmed.Title, theme.Text, FontWeight.SemiBold)),
                    Wrapped(theme.BodySmall(unconfirmed.Body, theme.Text)),
                    Wrapped(theme.Prose(unconfirmed.Detail, 14, theme.TextSecondary)),
                    new TillKey(theme, KeyLook.Secondary, theme.Body(unconfirmed.Acknowledge, theme.Text, FontWeight.SemiBold), actions.AcknowledgeUnconfirmed)
                    {
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(-2, 8, 0, 0),
                    },
                },
            },
        };
    }

    /// <summary>
    /// The Almanac slot (kit §7, design system InsightCard): white card, 3 px cyan rule, the
    /// label first. A card on top, the quiet weight on the left, as the design system sets them.
    /// </summary>
    private static Border Almanac(AlmanacSlot slot, TillTheme theme, TillActions actions)
    {
        switch (slot)
        {
            case AlmanacSlot.Quiet quiet:
                var quietBody = new StackPanel { Spacing = 6 };
                quietBody.Children.Add(AlmanacLabel("ALMANAC", theme));
                quietBody.Children.Add(Wrapped(theme.BodySmall(quiet.Text, theme.Text)));
                if (quiet.Awaiting is { } waiting)
                {
                    quietBody.Children.Add(Awaiting(waiting, theme));
                }

                return new Border
                {
                    Background = theme.Card,
                    BorderBrush = theme.AlmanacMark,
                    BorderThickness = new Thickness(TillSizes.SignalRule, 0, 0, 0),
                    CornerRadius = new CornerRadius(TillSizes.CardRadius),
                    Padding = new Thickness(16),
                    Child = quietBody,
                };

            case AlmanacSlot.Card card:
                var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                head.Children.Add(Cell(AlmanacLabel($"ALMANAC · {card.Kind}", theme), 0));
                var position = TillTheme.Figure(card.Position, 12, theme.TextMuted);
                position.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
                position.Tapped += (_, _) => actions.NextCard();
                head.Children.Add(Cell(position, 1));

                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
                if (card.Primary is { OptionId: { } option } primary)
                {
                    buttons.Children.Add(new TillKey(theme, KeyLook.Primary, theme.Body(primary.Label, theme.ActionLabel, FontWeight.SemiBold), () => actions.Accept(card.RecommendationId, option)));
                }

                // Adjust is on the card and unavailable: D-074 records no adjusted payload yet.
                buttons.Children.Add(new TillKey(theme, KeyLook.Secondary, theme.Body(card.Adjust.Label, theme.DisabledLabel), null, available: false));
                buttons.Children.Add(new TillKey(theme, KeyLook.Ghost, theme.Body(card.Dismiss.Label, theme.TextSecondary), () => actions.Dismiss(card.RecommendationId)));

                var body = new StackPanel { Spacing = 6 };
                body.Children.Add(head);
                body.Children.Add(Wrapped(theme.Prose(card.Claim, 16, theme.Text, FontWeight.SemiBold)));
                if (card.Detail is { } detail)
                {
                    body.Children.Add(Wrapped(theme.Prose(detail, 14, theme.TextSecondary)));
                }

                body.Children.Add(buttons);
                if (card.Awaiting is { } awaiting)
                {
                    body.Children.Add(Awaiting(awaiting, theme));
                }

                return new Border
                {
                    Background = theme.Card,
                    BorderBrush = theme.AlmanacMark,
                    BorderThickness = new Thickness(0, TillSizes.SignalRule, 0, 0),
                    CornerRadius = new CornerRadius(TillSizes.CardRadius),
                    Padding = new Thickness(16),
                    Child = body,
                };

            default:
                return new Border();
        }
    }

    /// <summary>The diamond marker and the attribution label, both Almanac's, never the operator's.</summary>
    private static StackPanel AlmanacLabel(string text, TillTheme theme) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            new Rectangle
            {
                Width = 8,
                Height = 8,
                Fill = theme.AlmanacMark,
                RenderTransform = new RotateTransform(45),
                VerticalAlignment = VerticalAlignment.Center,
            },
            theme.Label(text, theme.AlmanacText),
        },
    };

    private static StackPanel Awaiting(string text, TillTheme theme) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Margin = new Thickness(0, 4, 0, 0),
        Children = { TillTheme.Icon(LucideIcons.Users, theme.TextMuted, 16), theme.Prose(text, 14, theme.TextSecondary) },
    };

    // =========================================================== bottom bar

    public static Control BottomBar(BottomBar bottom, TillTheme theme, TillActions actions, bool paid)
    {
        var summary = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 48, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < bottom.Summary.Count; i++)
        {
            summary.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = theme.BodySmall(bottom.Summary[i].Label, theme.BarLabel);
            var figure = TillTheme.Figure(bottom.Summary[i].Value, 16, theme.BarText);
            figure.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetRow(label, i);
            Grid.SetRow(figure, i);
            Grid.SetColumn(figure, 1);
            summary.Children.Add(label);
            summary.Children.Add(figure);
        }

        var big = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 24, 0),
            Children =
            {
                End(theme.Label(bottom.BigLabel, theme.BarLabel)),
                theme.Prose(bottom.BigFigure, 44, theme.BarText),
            },
        };

        var primary = bottom.Primary;
        var labelBrush = primary.Enabled ? theme.CollectLabel : theme.BarDisabledLabel;
        var face = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        face.Children.Add(Words(primary.Title, 20, FontWeight.SemiBold, labelBrush, theme));
        if (primary.Detail is { } detail)
        {
            face.Children.Add(theme.Prose(detail, 13, labelBrush));
        }

        var key = new TillKey(
            theme,
            KeyLook.Collect,
            TillKey.Labelled(face, primary.Key, labelBrush),
            paid ? actions.NewSale : actions.Collect,
            primary.Enabled,
            TillSizes.BarKey)
        { Width = 236, VerticalAlignment = VerticalAlignment.Center };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(TillSizes.Margin, 0) };
        grid.Children.Add(Cell(summary, 0));
        grid.Children.Add(Cell(big, 1));
        grid.Children.Add(Cell(key, 2));

        return new Border { Background = theme.Bar, Height = TillSizes.BottomBar, Child = grid };
    }

    // ============================================================== helpers

    private static Grid RowGrid() => new()
    {
        // Quantity, article, unit price, total: the article takes what is left.
        ColumnDefinitions = new ColumnDefinitions("96,*,140,120"),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Border Card(TillTheme theme, IBrush? edge, Control child) => new()
    {
        Background = theme.Card,
        BorderBrush = edge ?? theme.Border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(TillSizes.CardRadius),
        Padding = new Thickness(20),
        Child = child,
    };

    private static TextBlock Words(string text, double size, FontWeight weight, IBrush brush, TillTheme theme)
    {
        var block = size >= 16 ? theme.Body(text, brush, weight) : theme.BodySmall(text, brush, weight);
        block.FontSize = size;
        block.LineHeight = double.NaN;
        return block;
    }

    private static TextBlock Struck(TextBlock block, TextDecorationCollection? strike)
    {
        block.TextDecorations = strike;
        return block;
    }

    private static TextBlock Wrapped(TextBlock block)
    {
        block.TextWrapping = TextWrapping.Wrap;
        block.TextTrimming = TextTrimming.None;
        return block;
    }

    private static Control End(Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Right;
        return control;
    }

    /// <summary>At the start of its cell: the left in French, the right in Arabic.</summary>
    private static Control Start(Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Left;
        return control;
    }

    private static Control Centred(Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Center;
        return control;
    }

    private static Control Cell(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }
}
