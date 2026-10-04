using Avalonia;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Styling;
using Avalonia.Layout;
using Avalonia.Media;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Ui;

/// <summary>What the customer panels do (B7). The window holds their state.</summary>
public sealed record FormActions(
    Action<string> Field,
    Action<string> Row,
    Action<string> Key,
    Action<char> Digit,
    Action Backspace,
    Action Clear,
    Action Primary,
    Action Close);

/// <summary>What the carnet view does (B7): its two keys and its ✕.</summary>
public sealed record CarnetActions(Action Change, Action Repay, Action Close);

/// <summary>
/// The customer screens (B7, B9a), from the G1 kit's pieces as the board draws them: the customer key
/// beside the field, the panels that float over the frozen ticket in the payment's frame (D-094), and
/// the carnet in the ticket's place, which does not float.
/// </summary>
public static partial class TillViews
{
    /// <summary>The customer key, tagged so its tests find it; its ✕ beside it once someone is attached.</summary>
    public const string ClientKeyTag = "client-key";

    public const string ClientDetachTag = "client-detach";

    public const string CarnetCloseTag = "carnet-close";

    /// <summary>"Client · par téléphone · F5", or the attached customer's name with its ✕.</summary>
    public static Control ClientKeyView(ClientKey key, TillTheme theme, Action open, Action detach)
    {
        var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        face.Children.Add(TillTheme.Icon(LucideIcons.User, theme.Text, 18));
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(Words(key.Label, 15, FontWeight.SemiBold, theme.Text, theme));
        if (key.Detail is { } detail)
        {
            words.Children.Add(theme.BodySmall(detail, theme.TextSecondary));
        }

        face.Children.Add(words);
        var main = new TillKey(theme, key.Attached ? KeyLook.Chosen : KeyLook.Secondary, TillKey.Labelled(face, key.Key, theme.TextMuted, reserve: key.Attached), open, height: TillSizes.Key)
        {
            Tag = ClientKeyTag,
            MinWidth = 150,
        };
        if (!key.Attached)
        {
            return new Border { Margin = new Thickness(8, 0, 0, 0), Child = main };
        }

        var x = new TillKey(theme, KeyLook.Secondary, Centred(TillTheme.Icon(LucideIcons.X, theme.Text, 18)), detach, height: TillSizes.Key)
        {
            Tag = ClientDetachTag,
            Width = 52,
            Margin = new Thickness(4, 0, 0, 0),
        };
        Avalonia.Automation.AutomationProperties.SetName(x, key.Label);
        var both = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        both.Children.Add(main);
        both.Children.Add(x);
        return both;
    }

    /// <summary>A customer panel floating over the frozen ticket: what is asked on the left, the pad and the key that goes on at the right.</summary>
    public static Control Form(FormPanel panel, TillTheme theme, FormActions? actions)
    {
        var left = new StackPanel { Spacing = 8 };

        if (panel.Tiles.Count > 0)
        {
            var tiles = new UniformGrid { Columns = panel.Tiles.Count, Margin = new Thickness(-4, 0) };
            foreach (var tile in panel.Tiles)
            {
                tiles.Children.Add(Tile(theme, tile.Label, tile.Value, null));
            }

            left.Children.Add(tiles);
        }

        foreach (var field in panel.Fields)
        {
            var id = field.Id;
            var content = new DockPanel();
            if (field.Suffix is { } suffix)
            {
                content.Children.Add(Docked(theme.BodySmall(suffix, theme.TextSecondary), Dock.Right));
            }

            content.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = field.Suffix is null ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 6, 0),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        field.Value.Length == 0
                            ? theme.BodySmall(field.Placeholder ?? string.Empty, theme.TextMuted)
                            : TillTheme.Figure(field.Value, 20, theme.Text),

                        // The field that has the keys says so, as a text box does: its caret blinks.
                        field.Active ? Caret(theme) : new Border(),
                    },
                },
            });
            var box = Field(theme, field.Active, content);
            if (field.Invalid)
            {
                var (mark, _, _) = theme.ToneOnSurface(Tone.Critical);
                box.BorderBrush = mark;
            }

            left.Children.Add(new StackPanel
            {
                Spacing = 4,
                Children = { theme.Label(field.Title, theme.TextSecondary), Touchable(box, () => actions?.Field(id), $"field-{id}") },
            });
        }

        if (panel.Note is { } note)
        {
            left.Children.Add(Wrapped(theme.BodySmall(note, theme.TextSecondary)));
        }

        if (panel.Notice is { } notice)
        {
            left.Children.Add(new Border
            {
                Background = theme.Tile,
                CornerRadius = new CornerRadius(TillSizes.KeyRadius),
                Padding = new Thickness(12, 8),
                Child = Wrapped(theme.BodySmall(notice, theme.Text)),
            });
        }

        if (panel.Message is { } message)
        {
            left.Children.Add(Refusal(theme, message));
        }

        if (panel.BigLabel is { } bigLabel)
        {
            left.Children.Add(theme.Label(bigLabel, theme.TextSecondary));
            left.Children.Add(TillTheme.Figure(panel.Big ?? string.Empty, 36, theme.Text));
            foreach (var figure in panel.Figures)
            {
                left.Children.Add(new Border
                {
                    BorderBrush = theme.BorderSubtle,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(0, 6),
                    Child = Row(theme.BodySmall(figure.Label, theme.Text), TillTheme.Figure(figure.Value, 15, theme.Text)),
                });
            }
        }

        Control? rowsLabel = null;
        Control? rowsBlock = null;
        Control? keysBlock = null;
        if (panel.RowsTitle is { } rowsTitle)
        {
            rowsLabel = theme.Label(rowsTitle, theme.TextSecondary);
            left.Children.Add(rowsLabel);
            var rows = new StackPanel { Spacing = 4 };
            rowsBlock = rows;
            if (panel.NoRows is { } none)
            {
                rows.Children.Add(new Border
                {
                    BorderBrush = theme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(TillSizes.KeyRadius),
                    Padding = new Thickness(12, 10),
                    Child = theme.BodySmall(none, theme.TextSecondary),
                });
            }

            foreach (var row in panel.Rows)
            {
                var id = row.Id;
                var face = new DockPanel { Margin = new Thickness(12, 0) };
                if (row.Detail is { } detail)
                {
                    face.Children.Add(Docked(new Border { VerticalAlignment = VerticalAlignment.Center, Child = TillTheme.Figure(detail, 13, theme.TextSecondary) }, Dock.Right));
                }

                face.Children.Add(new Border { VerticalAlignment = VerticalAlignment.Center, Child = Words(row.Label, 14, FontWeight.SemiBold, theme.Text, theme) });
                rows.Children.Add(new TillKey(theme, row.Selected ? KeyLook.Chosen : KeyLook.Secondary, face, () => actions?.Row(id), true, 44) { Tag = row });
            }

            left.Children.Add(rows);
        }

        if (panel.Keys.Count > 0)
        {
            var keys = new WrapPanel();
            foreach (var key in panel.Keys)
            {
                var id = key.Id;
                var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                face.Children.Add(Words(key.Label, 13, FontWeight.SemiBold, key.Enabled ? theme.Text : theme.DisabledLabel, theme));
                if (key.Locked)
                {
                    face.Children.Add(TillTheme.Icon(LucideIcons.Lock, theme.TextSecondary, 14));
                }

                keys.Children.Add(new TillKey(theme, key.Chosen ? KeyLook.Chosen : KeyLook.Secondary, face, () => actions?.Key(id), key.Enabled, 44)
                {
                    Tag = key,
                    Margin = new Thickness(0, 0, 6, 4),
                    Padding = new Thickness(12, 0),
                });
            }

            left.Children.Add(keys);
            keysBlock = keys;
        }

        // A panel's first choice is drawn first, under the tiles: cash in or out before the amount,
        // who clocks before their PIN (block B review). The model says which; the order here is its.
        var first = panel.Tiles.Count > 0 ? 1 : 0;
        if (panel.KeysFirst && keysBlock is not null)
        {
            left.Children.Remove(keysBlock);
            left.Children.Insert(first++, keysBlock);
        }

        if (panel.RowsFirst && rowsLabel is not null && rowsBlock is not null)
        {
            left.Children.Remove(rowsLabel);
            left.Children.Remove(rowsBlock);
            left.Children.Insert(first++, rowsLabel);
            left.Children.Insert(first, rowsBlock);
        }

        if (panel.Footer is { } footer)
        {
            left.Children.Add(Wrapped(theme.BodySmall(footer, theme.TextSecondary)));
        }

        var right = new DockPanel();
        right.Children.Add(Docked(LockedPrimary(theme, panel.Primary, panel.PrimaryKey, panel.MayPrimary, panel.Locked, () => actions?.Primary()), Dock.Bottom));
        if (panel.Pad)
        {
            // A number or a PIN reads 1 2 3 on top, as a phone and the sign-in pad do; money reads 7 8 9,
            // as the payment pad does. Left to right in Arabic too: the digits are not text.
            var pad = new UniformGrid { Columns = 3, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(-2, 0) };
            foreach (var key in panel.PhonePad ? "123456789" : "789456123")
            {
                var pressed = key;
                pad.Children.Add(FloatingPadKey(TillTheme.Figure(key.ToString(), 20, theme.Text), () => actions?.Digit(pressed), true, theme));
            }

            pad.Children.Add(FloatingPadKey(TillTheme.Icon(LucideIcons.RotateCw, theme.Text, 18), () => actions?.Clear(), true, theme));
            pad.Children.Add(FloatingPadKey(TillTheme.Figure("0", 20, theme.Text), () => actions?.Digit('0'), true, theme));
            pad.Children.Add(FloatingPadKey(TillTheme.Icon(LucideIcons.Delete, theme.Text, 22), () => actions?.Backspace(), true, theme));
            right.Children.Add(new Border { VerticalAlignment = VerticalAlignment.Top, Child = pad });
        }
        else
        {
            right.Children.Add(new Border());
        }

        return Floating(theme, panel.Label, panel.Title, panel.CloseKey, panel.Title, PaymentCloseTag, () => actions?.Close(), left, right, panel, panel.Subtitle);
    }

    /// <summary>
    /// The carnet in the ticket's place (B7): it does not float, it is a view of its own, on the card
    /// the ticket was on. The figures, the standing by its label, the statement, and its two keys.
    /// </summary>
    public static Control Carnet(CarnetView view, TillTheme theme, CarnetActions actions)
    {
        var body = new DockPanel();

        var heading = new StackPanel { Spacing = 2 };
        heading.Children.Add(theme.Label(view.Label, theme.TextSecondary));
        heading.Children.Add(Words(view.Title, 22, FontWeight.SemiBold, theme.Text, theme));
        heading.Children.Add(theme.BodySmall(view.Subtitle, theme.TextSecondary));
        var x = new TillKey(theme, KeyLook.Secondary, TillKey.Labelled(Centred(TillTheme.Icon(LucideIcons.X, theme.Text, 20)), view.CloseKey, theme.TextMuted), actions.Close, height: FloatingKey)
        {
            Tag = CarnetCloseTag,
            Width = 72,
        };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(Docked(x, Dock.Right));
        head.Children.Add(heading);
        body.Children.Add(Docked(head, Dock.Top));

        var top = new StackPanel { Spacing = 10 };
        if (view.State is { Tone: Tone.Neutral } neutral)
        {
            // "SANS CARNET" is a fact, not a warning: labelled, on the plain ground.
            top.Children.Add(new Border
            {
                BorderBrush = theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(TillSizes.FieldRadius),
                Padding = new Thickness(10, 6),
                Child = new StackPanel { Spacing = 2, Children = { theme.Label(neutral.Label, theme.TextSecondary), Wrapped(theme.BodySmall(neutral.Text, theme.Text)) } },
            });
        }
        else if (view.State is { } state)
        {
            top.Children.Add(Refusal(theme, new PanelMessage(state.Label, state.Text, state.Tone)));
        }

        if (view.Message is { } message)
        {
            top.Children.Add(Refusal(theme, new PanelMessage(view.Label, message)));
        }

        if (view.Figures.Count > 0)
        {
            // Four tiles abreast on a narrow ticket cut the fourth and its figures (F-29): two by two there.
            var tiles = new UniformGrid
            {
                Columns = theme.Narrow && view.Figures.Count > 2 ? 2 : view.Figures.Count,
                Margin = new Thickness(-4, 0),
            };
            foreach (var figure in view.Figures)
            {
                var tile = Tile(theme, figure.Label, figure.Value, figure.Detail);
                tile.Margin = new Thickness(4, 0, 4, theme.Narrow ? 8 : 0);
                tiles.Children.Add(tile);
            }

            top.Children.Add(tiles);
            top.Children.Add(theme.Label(view.StatementTitle, theme.TextSecondary));
        }

        body.Children.Add(Docked(top, Dock.Top));

        // The keys at the foot: the footer that says the opening was logged, then the two keys.
        var foot = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var repay = new TillKey(
            theme,
            KeyLook.Primary,
            TillKey.Labelled(Centred(Words(view.Repay, 15, FontWeight.SemiBold, view.MayRepay ? theme.ActionLabel : theme.DisabledLabel, theme)), view.RepayKey, theme.ActionLabel),
            actions.Repay,
            view.MayRepay,
            52)
        { Tag = view.Repay, Padding = new Thickness(16, 0) };
        var changeFace = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        changeFace.Children.Add(Words(view.Change.Label, 14, FontWeight.SemiBold, view.Change.Enabled ? theme.Text : theme.DisabledLabel, theme));
        changeFace.Children.Add(TillTheme.Icon(LucideIcons.Lock, theme.TextSecondary, 14));
        var change = new TillKey(theme, KeyLook.Secondary, changeFace, actions.Change, view.Change.Enabled, 52)
        {
            Tag = view.Change,
            Padding = new Thickness(16, 0),
            Margin = new Thickness(0, 0, 8, 0),
        };
        foot.Children.Add(Docked(repay, Dock.Right));
        foot.Children.Add(Docked(change, Dock.Right));
        var logged = Wrapped(theme.BodySmall(view.Footer, theme.TextSecondary));
        if (theme.Narrow)
        {
            // Beside the two keys the sentence had forty pixels and broke at every word (F-29): above them.
            body.Children.Add(Docked(foot, Dock.Bottom));
            body.Children.Add(Docked(new Border { Margin = new Thickness(0, 12, 0, 0), Child = logged }, Dock.Bottom));
        }
        else
        {
            foot.Children.Add(new Border { VerticalAlignment = VerticalAlignment.Center, Child = logged });
            body.Children.Add(Docked(foot, Dock.Bottom));
        }

        // The statement: date, movement, amount, balance; oldest first.
        var table = new StackPanel();
        if (view.Columns.Count == 4)
        {
            table.Children.Add(StatementLine(theme, view.Columns[0], view.Columns[1], view.Columns[2], view.Columns[3], heading: true));
        }

        foreach (var row in view.Rows)
        {
            table.Children.Add(StatementLine(theme, row.Date, row.Movement, row.Amount, row.Balance, heading: false));
        }

        if (view.Empty is { } empty)
        {
            table.Children.Add(new Border { Padding = new Thickness(12, 10), Child = theme.BodySmall(empty, theme.TextSecondary) });
        }

        body.Children.Add(new ScrollViewer { Content = table, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });

        var card = Card(theme, null, body);
        card.Tag = view;
        return card;
    }

    /// <summary>A text caret, blinking once a second while it is on screen, and stopped when it leaves it.</summary>
    private static Rectangle Caret(TillTheme theme)
    {
        var caret = new Rectangle
        {
            Width = 2,
            Height = 22,
            Fill = theme.Text,
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var blink = new Animation
        {
            Duration = TimeSpan.FromSeconds(1),
            IterationCount = IterationCount.Infinite,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(0.5), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(0.51), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
            },
        };
        var stop = new CancellationTokenSource();
        caret.AttachedToVisualTree += (_, _) => _ = blink.RunAsync(caret, stop.Token);
        caret.DetachedFromVisualTree += (_, _) =>
        {
            stop.Cancel();
            stop.Dispose();
        };
        return caret;
    }

    private static Border Tile(TillTheme theme, string label, string value, string? detail)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(theme.Label(label, theme.TextSecondary));
        stack.Children.Add(TillTheme.Figure(value, 18, theme.Text));
        if (detail is not null)
        {
            stack.Children.Add(theme.BodySmall(detail, theme.TextSecondary));
        }

        return new Border
        {
            Background = theme.Card,
            BorderBrush = theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(TillSizes.KeyRadius),
            Padding = new Thickness(12, 10),
            Margin = new Thickness(4, 0),
            Child = stack,
        };
    }

    private static Border StatementLine(TillTheme theme, string date, string movement, string amount, string balance, bool heading)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*,120,120"), Height = heading ? 32 : 40 };
        Control Text(string value, bool figure) => heading
            ? theme.Label(value, theme.TextSecondary)
            : figure ? TillTheme.Figure(value, 14, theme.Text) : theme.BodySmall(value, theme.Text);

        // At the row's start, as the ticket's quantity is: a figure is a left-to-right run, and in
        // Arabic it sat at the far end of its cell, run into the movement beside it (F-33).
        grid.Children.Add(Cell(new Border { VerticalAlignment = VerticalAlignment.Center, Child = Start(Text(date, true)) }, 0));
        grid.Children.Add(Cell(new Border { VerticalAlignment = VerticalAlignment.Center, Child = Text(movement, false) }, 1));
        grid.Children.Add(Cell(new Border { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Child = Text(amount, true) }, 2));
        grid.Children.Add(Cell(new Border { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Child = Text(balance, true) }, 3));
        return new Border
        {
            BorderBrush = theme.BorderSubtle,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 0),
            Child = grid,
        };
    }

    /// <summary>The key that goes on, with its padlock when it may ask a PIN (the board's mark).</summary>
    private static TillKey LockedPrimary(TillTheme theme, string label, string key, bool available, bool locked, Action pressed)
    {
        if (!locked)
        {
            return PrimaryKey(theme, label, key, available, pressed);
        }

        var ink = available ? theme.ActionLabel : theme.DisabledLabel;
        var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        face.Children.Add(Words(label, 16, FontWeight.SemiBold, ink, theme));
        face.Children.Add(TillTheme.Icon(LucideIcons.Lock, ink, 16));
        return new TillKey(theme, KeyLook.Primary, TillKey.Labelled(face, key, ink), pressed, available, 56) { Margin = new Thickness(-2, 12, -2, 0) };
    }
}
