using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Ui;

/// <summary>What the sign-in screen's keys do. The window binds them to <c>SignInFlow</c>.</summary>
public sealed record SignInActions(
    Action<string> Select,
    Action<char> Digit,
    Action Backspace,
    Action Clear,
    Action Open);

public static partial class TillViews
{
    private const double PeopleCard = 400;
    private const double PinCard = 456;
    private const double PadKeyHeight = 56;

    /// <summary>
    /// The sign-in screen (A5, G1 "Connexion"): the list on one side, the pad on the other, on the
    /// page ground between the two bars' worth of top bar. Drawn from <see cref="SignInScreen"/> alone.
    /// </summary>
    public static Control SignIn(SignInScreen screen, TillTheme theme, SignInActions actions)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(actions);

        var cards = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { PeopleList(screen, theme, actions), PinSide(screen, theme, actions) },
        };

        return new DockPanel
        {
            Children =
            {
                Docked(TopBar(screen.Top, theme), Dock.Top),
                new ScrollViewer { Content = new Border { Padding = new Thickness(TillSizes.Margin, 32), Child = cards } },
            },
        };
    }

    private static Border PeopleList(SignInScreen screen, TillTheme theme, SignInActions actions)
    {
        var list = new StackPanel { Spacing = 8, Margin = new Thickness(0, 16, 0, 0) };
        foreach (var person in screen.People)
        {
            list.Children.Add(PersonRow(person, theme, actions));
        }

        if (screen.Empty is { } empty)
        {
            list.Children.Add(Wrapped(Words(empty.Title, 16, FontWeight.SemiBold, theme.Text, theme)));
            list.Children.Add(Wrapped(theme.BodySmall(empty.Hint, theme.TextSecondary)));
        }

        var head = new StackPanel
        {
            Spacing = 4,
            Children = { Wrapped(Words(screen.Title, 22, FontWeight.SemiBold, theme.Text, theme)) },
        };
        if (screen.Subtitle is { } subtitle)
        {
            head.Children.Add(theme.Prose(subtitle, 13, theme.TextSecondary));
        }

        var card = Card(theme, null, new StackPanel { Children = { head, list } });
        card.Width = PeopleCard;
        card.Padding = new Thickness(24);
        card.VerticalAlignment = VerticalAlignment.Stretch;
        return card;
    }

    /// <summary>
    /// One person. Selected is the violet edge, 2 px: the operator's colour for the operator's
    /// choice. Somebody with no PIN keeps their name, in the secondary text, and says why in a label.
    /// </summary>
    private static Border PersonRow(StaffRow person, TillTheme theme, SignInActions actions)
    {
        var avatar = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = theme.Tile,
            Child = Centred(TillTheme.Figure(person.Initials, 14, theme.Text, FontWeight.Medium)),
        };
        avatar.Child.VerticalAlignment = VerticalAlignment.Center;

        var nameBrush = person.Chip is null ? theme.Text : theme.TextSecondary;
        var words = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Words(person.Name, 16, FontWeight.SemiBold, nameBrush, theme),
                theme.BodySmall(person.Role, theme.TextSecondary),
            },
        };

        var line = new DockPanel { LastChildFill = true };
        line.Children.Add(Docked(avatar, Dock.Left));
        if (person.Chip is { } chip)
        {
            var label = Chip(chip, theme);
            label.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(Docked(label, Dock.Right));
        }

        words.Margin = new Thickness(14, 0, 8, 0);
        line.Children.Add(words);

        var row = new Border
        {
            Background = theme.Card,
            BorderBrush = person.Selected ? theme.Action : theme.Border,
            BorderThickness = new Thickness(person.Selected ? 2 : 1),
            CornerRadius = new CornerRadius(TillSizes.KeyRadius),
            Padding = new Thickness(person.Selected ? 14 : 15, person.Selected ? 9 : 10),
            MinHeight = 64,
            Child = line,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        // Touching somebody without a PIN still answers: it says they have none.
        row.Tapped += (_, e) =>
        {
            actions.Select(person.StaffId);
            e.Handled = true;
        };
        return row;
    }

    private static Border PinSide(SignInScreen screen, TillTheme theme, SignInActions actions)
    {
        var pad = screen.Pad;

        // The dots: one filled per digit typed, never the digit. A PIN field that grows past four
        // is a PIN of five or more, and the field says so by its dots alone.
        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FlowDirection = FlowDirection.LeftToRight,
        };
        for (var i = 0; i < pad.Slots; i++)
        {
            dots.Children.Add(new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = i < pad.Filled ? theme.Text : null,
                Stroke = i < pad.Filled ? null : theme.TextSecondary,
                StrokeThickness = 2,
            });
        }

        var field = new Border
        {
            Height = 60,
            Margin = new Thickness(0, 16, 0, 0),
            Background = theme.Card,
            BorderBrush = pad.Available ? theme.FocusRing : theme.Border,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(TillSizes.FieldRadius + 2),
            Child = dots,
        };

        // Unavailable is its own look, ground and label (TillKey), never a faded key.
        var ink = pad.Available ? theme.Text : theme.DisabledLabel;

        // A phone pad reads 1 2 3 from the left in Arabic too: the digits are not text.
        var keys = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 0), FlowDirection = FlowDirection.LeftToRight };
        foreach (var digit in "123456789")
        {
            keys.Children.Add(PadKey(TillTheme.Figure(digit.ToString(), 20, ink), () => actions.Digit(digit), pad.Available, theme));
        }

        keys.Children.Add(PadKey(Words(pad.ClearKey, 14, FontWeight.SemiBold, ink, theme), actions.Clear, pad.Available, theme));
        keys.Children.Add(PadKey(TillTheme.Figure("0", 20, ink), () => actions.Digit('0'), pad.Available, theme));
        keys.Children.Add(PadKey(TillTheme.Icon(LucideIcons.Delete, ink, 22), actions.Backspace, pad.Available, theme));

        var open = new TillKey(
            theme,
            KeyLook.Primary,
            Centred(Words(screen.Open.Title, 16, FontWeight.SemiBold, screen.Open.Enabled ? theme.ActionLabel : theme.DisabledLabel, theme)),
            actions.Open,
            screen.Open.Enabled)
        { Margin = new Thickness(-2, 16, -2, 0) };

        var body = new StackPanel { Children = { Wrapped(Words(pad.Title, 22, FontWeight.SemiBold, theme.Text, theme)), field } };
        if (screen.Message is { } message)
        {
            body.Children.Add(Notice(message, theme));
        }

        body.Children.Add(keys);
        body.Children.Add(open);

        var card = Card(theme, null, body);
        card.Width = PinCard;
        card.Padding = new Thickness(24);
        return card;
    }

    private static TillKey PadKey(Control face, Action pressed, bool available, TillTheme theme)
    {
        if (face is Layoutable layoutable)
        {
            layoutable.HorizontalAlignment = HorizontalAlignment.Center;
            layoutable.VerticalAlignment = VerticalAlignment.Center;
        }

        return new TillKey(theme, KeyLook.Pad, face, pressed, available, PadKeyHeight) { Margin = new Thickness(2) };
    }
}
