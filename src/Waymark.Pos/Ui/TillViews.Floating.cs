using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Waymark.Domain.Enums;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Ui;

/// <summary>What the payment panel does (B6). The window holds its state.</summary>
public sealed record PaymentActions(
    Action<PaymentMethod> Choose,
    Action<char> Key,
    Action Backspace,
    Action WholeRest,
    Action<int> RemovePart,
    Action<bool> TypeInReference,
    Action Primary,
    Action Close,
    Action? Action = null);

/// <summary>
/// The panels that freeze the ticket and float over it (D-094): the payment (B6) and the manager's
/// PIN (B4). One frame for both: a card 880 px wide on a scrim, its title and ✕ at the top, what is
/// asked on the left, the pad and the key that goes on at the right. The scrim takes the touches
/// meant for the ticket under it; the ✕ closes and nothing changes, as Échap does. Never in a scroll
/// viewer: every key redraws it (29/09).
/// </summary>
public static partial class TillViews
{
    /// <summary>The ✕ on the manager step, tagged so its tests find it.</summary>
    public const string ApprovalCloseTag = "approval-close";

    /// <summary>The ✕ on the payment panel.</summary>
    public const string PaymentCloseTag = "payment-close";

    /// <summary>The payment's amount field, touched to type the amount.</summary>
    public const string PaymentAmountTag = "payment-amount";

    /// <summary>The payment's reference field, touched to type the reference.</summary>
    public const string PaymentReferenceTag = "payment-reference";

    /// <summary>"Tout le reste".</summary>
    public const string PaymentRestTag = "payment-rest";

    /// <summary>Hakim, 30/09: the payment panel's width, and the manager step's with it.</summary>
    private const double FloatingWidth = 880;

    private const double FloatingRight = 300;

    private const double FloatingKey = 52;

    // ================================================================ the manager step

    /// <summary>
    /// The manager step (B4), as the board's 09-pin draws it: what is asked, who authorises it and the
    /// PIN as dots on the left; the pad and Valider on the right. The digits never reach the screen.
    /// </summary>
    public static Control Approval(Approval approval, TillTheme theme, TillActions actions)
    {
        var discounts = actions.Discounts;

        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(new Border
        {
            Background = theme.Tile,
            CornerRadius = new CornerRadius(TillSizes.KeyRadius),
            Padding = new Thickness(12, 8),
            Child = Wrapped(theme.BodySmall(approval.Summary, theme.TextSecondary)),
        });

        left.Children.Add(theme.Label(approval.WhoTitle, theme.TextSecondary));
        var who = new WrapPanel();
        foreach (var person in approval.Approvers)
        {
            var id = person.StaffId;
            who.Children.Add(new TillKey(
                theme,
                person.Selected ? KeyLook.Primary : KeyLook.Secondary,
                Words(person.Name, 14, FontWeight.SemiBold, person.Selected ? theme.ActionLabel : person.Available ? theme.Text : theme.DisabledLabel, theme),
                () => discounts?.Approver(id),
                person.Available,
                FloatingKey)
            { Margin = new Thickness(0, 0, 6, 4), Tag = person });
        }

        left.Children.Add(who);
        left.Children.Add(theme.Label(approval.PinTitle, theme.TextSecondary));

        // The dots: one filled per digit typed, never the digit (as at sign-in).
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
        for (var i = 0; i < Math.Max(4, approval.PinLength); i++)
        {
            dots.Children.Add(new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = i < approval.PinLength ? theme.Text : null,
                Stroke = i < approval.PinLength ? null : theme.TextSecondary,
                StrokeThickness = 2,
            });
        }

        left.Children.Add(Field(theme, true, new Border { VerticalAlignment = VerticalAlignment.Center, Child = dots }));

        if (approval.Message is { } message)
        {
            var (_, refusedInk, _) = theme.ToneOnSurface(Tone.Critical);
            left.Children.Add(Wrapped(theme.BodySmall(message, refusedInk)));
        }

        // A phone pad reads 1 2 3 from the top, as at sign-in; and from the left in Arabic too.
        var pad = new UniformGrid { Columns = 3, FlowDirection = FlowDirection.LeftToRight };
        foreach (var digit in "123456789")
        {
            pad.Children.Add(FloatingPadKey(TillTheme.Figure(digit.ToString(), 20, theme.Text), () => discounts?.Digit(digit), true, theme));
        }

        pad.Children.Add(FloatingPadKey(TillTheme.Icon(LucideIcons.RotateCw, theme.Text, 18), () => discounts?.Clear(), true, theme));
        pad.Children.Add(FloatingPadKey(TillTheme.Figure("0", 20, theme.Text), () => discounts?.Digit('0'), true, theme));
        pad.Children.Add(FloatingPadKey(TillTheme.Icon(LucideIcons.Delete, theme.Text, 22), () => discounts?.Backspace(), true, theme));

        var right = new DockPanel();
        right.Children.Add(Docked(PrimaryKey(theme, approval.Validate, approval.EnterKey, approval.MayValidate, () => discounts?.Validate()), Dock.Bottom));
        right.Children.Add(new Border { VerticalAlignment = VerticalAlignment.Top, Child = pad });

        return Floating(theme, approval.Label, approval.Title, approval.EscapeKey, approval.Close, ApprovalCloseTag, () => discounts?.Close(), left, right, approval);
    }

    // ================================================================ the payment

    /// <summary>
    /// "Encaisser" (B6, D-095), as the board draws it, without what the board shows of cash handed over
    /// and change, which the till does not take (D-095): the figures and the cash to collect, the parts,
    /// the three methods, and for a card or BaridiMob part its amount and optional reference; the rest
    /// after the part, "Tout le reste", the pad and the key that goes on at the right.
    /// </summary>
    public static Control Payment(PaymentPanel panel, TillTheme theme, PaymentActions? actions)
    {
        var left = new StackPanel { Spacing = 8 };

        // The figures, then the cash to collect: the big figure.
        var summary = new StackPanel { Spacing = 4 };
        foreach (var figure in panel.Figures)
        {
            summary.Children.Add(Row(theme.BodySmall(figure.Label, theme.TextSecondary), TillTheme.Figure(figure.Value, 15, theme.TextSecondary)));
        }

        summary.Children.Add(new Border { Height = 1, Background = theme.Border, Margin = new Thickness(0, 6) });
        summary.Children.Add(Row(Words(panel.DueLabel, 16, FontWeight.SemiBold, theme.Text, theme), TillTheme.Figure(panel.Due, 36, theme.Text)));
        left.Children.Add(new Border
        {
            Background = theme.Tile,
            CornerRadius = new CornerRadius(TillSizes.KeyRadius),
            Padding = new Thickness(16, 12),
            Child = summary,
        });

        // The parts added, each with its ✕.
        left.Children.Add(theme.Label(panel.PartsTitle, theme.TextSecondary));
        var parts = new StackPanel();
        if (panel.NoParts is { } none)
        {
            parts.Children.Add(new Border { Padding = new Thickness(12, 10), Child = theme.BodySmall(none, theme.TextSecondary) });
        }

        foreach (var part in panel.Parts)
        {
            var index = part.Index;
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(TillTheme.Icon(MethodIcon(part.Method), theme.TextSecondary, 16));
            name.Children.Add(Words(part.Label, 14, FontWeight.SemiBold, theme.Text, theme));
            if (part.Reference is { } reference)
            {
                name.Children.Add(TillTheme.Figure(reference, 12, theme.TextMuted));
            }

            var remove = new TillKey(theme, KeyLook.Ghost, TillTheme.Icon(LucideIcons.X, theme.TextSecondary, 16), () => actions?.RemovePart(index), height: 36) { Tag = part };
            var row = new DockPanel { Margin = new Thickness(12, 2, 4, 2) };
            row.Children.Add(Docked(remove, Dock.Right));
            row.Children.Add(Docked(new Border { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0), Child = TillTheme.Figure(part.Amount, 15, theme.Text) }, Dock.Right));
            row.Children.Add(name);
            parts.Children.Add(row);
        }

        left.Children.Add(new Border
        {
            Background = theme.Card,
            BorderBrush = theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(TillSizes.KeyRadius),
            Child = parts,
        });

        // Espèces, Carte, BaridiMob, and Carnet for a customer with a tab (B7).
        // Three abreast at most: with Carnet and Avoir beside the three, five keys in one row ran
        // their labels into each other. Four go two by two, five three and two.
        var methods = new UniformGrid { Columns = panel.Methods.Count == 4 ? 2 : 3, Margin = new Thickness(-2, 0) };
        foreach (var choice in panel.Methods)
        {
            var method = choice.Method;
            var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            face.Children.Add(TillTheme.Icon(MethodIcon(method), theme.Text, 18));
            face.Children.Add(Words(choice.Label, 14, FontWeight.SemiBold, theme.Text, theme));
            methods.Children.Add(new TillKey(theme, choice.Selected ? KeyLook.Chosen : KeyLook.Secondary, face, () => actions?.Choose(method), true, FloatingKey) { Tag = choice });
        }

        left.Children.Add(methods);

        // A card or BaridiMob part: its amount and its reference, side by side.
        if (panel.Entry is { } entry)
        {
            var amount = new DockPanel();
            amount.Children.Add(Docked(theme.BodySmall(entry.Currency, theme.TextSecondary), Dock.Right));
            amount.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Child = TillTheme.Figure(entry.Amount, 20, entry.Prefilled ? theme.TextSecondary : theme.Text),
            });
            var reference = entry.Reference.Length == 0
                ? theme.BodySmall(entry.ReferencePlaceholder, theme.TextMuted)
                : TillTheme.Figure(entry.Reference, 16, theme.Text);

            // The tab (B7): whose it is, what is available, and after this sale, above its amount.
            if (entry.AccountTitle is { } account)
            {
                var lines = new StackPanel { Spacing = 4 };
                lines.Children.Add(theme.Label(account, theme.TextSecondary));
                foreach (var figure in entry.Account ?? [])
                {
                    lines.Children.Add(Row(theme.BodySmall(figure.Label, theme.Text), TillTheme.Figure(figure.Value, 15, theme.Text)));
                }

                left.Children.Add(new Border
                {
                    Background = theme.Card,
                    BorderBrush = theme.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(TillSizes.KeyRadius),
                    Padding = new Thickness(12, 8),
                    Child = lines,
                });
            }

            var fields = new Grid { ColumnDefinitions = new ColumnDefinitions(entry.HasReference ? "*,12,*" : "*") };
            fields.Children.Add(Labelled(theme, entry.AmountTitle, Touchable(Field(theme, !entry.OnReference, amount), () => actions?.TypeInReference(false), PaymentAmountTag), 0));
            if (entry.HasReference)
            {
                fields.Children.Add(Labelled(theme, entry.ReferenceTitle, Touchable(Field(theme, entry.OnReference, new Border { VerticalAlignment = VerticalAlignment.Center, Child = reference }), () => actions?.TypeInReference(true), PaymentReferenceTag), 2));
            }

            left.Children.Add(fields);
        }

        if (panel.Hint is { } hint)
        {
            left.Children.Add(Wrapped(theme.BodySmall(hint, theme.TextSecondary)));
        }

        if (panel.Message is { } message)
        {
            left.Children.Add(Refusal(theme, message));
        }

        // A step to take first, in the panel: "Rattacher un client · F5" (B9a).
        if (panel.Action is { } step)
        {
            var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            face.Children.Add(TillTheme.Icon(LucideIcons.User, theme.Text, 16));
            face.Children.Add(Words(step.Label, 14, FontWeight.SemiBold, theme.Text, theme));
            left.Children.Add(new TillKey(theme, KeyLook.Secondary, face, () => actions?.Action?.Invoke(), true, 44) { Tag = step, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 0) });
        }

        // The right: the rest after the part and "Tout le reste" while a part is added, then the pad.
        var right = new DockPanel();
        right.Children.Add(Docked(PrimaryKey(theme, panel.Primary, panel.PrimaryKey, panel.MayPrimary, () => actions?.Primary()), Dock.Bottom));
        var top = new StackPanel { Spacing = 8 };
        if (panel.Entry is { } part2)
        {
            top.Children.Add(new Border
            {
                Background = theme.Card,
                BorderBrush = theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(TillSizes.KeyRadius),
                Padding = new Thickness(12, 8),
                Child = new StackPanel
                {
                    Spacing = 2,
                    Children = { theme.Label(part2.RestTitle, theme.TextSecondary), TillTheme.Figure(part2.Rest, 28, theme.Text) },
                },
            });
            var whole = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            whole.Children.Add(Centred(Words(part2.WholeRest, 13, FontWeight.SemiBold, theme.Text, theme)));
            whole.Children.Add(Centred(TillTheme.Figure(part2.WholeRestAmount, 11, theme.TextSecondary)));
            top.Children.Add(new TillKey(theme, KeyLook.Pad, whole, () => actions?.WholeRest(), panel.PadAvailable, 44) { Tag = PaymentRestTag, Margin = new Thickness(-2, 0) });
            top.Children.Add(Wrapped(theme.BodySmall(part2.NoChange, theme.TextSecondary)));
        }

        // A calculator's pad, 7 8 9 on top as on the board; left to right in Arabic too.
        var pad = new UniformGrid { Columns = 3, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(-2, 0) };
        foreach (var key in "789456123,0")
        {
            var pressed = key;
            pad.Children.Add(FloatingPadKey(TillTheme.Figure(key.ToString(), 20, panel.PadAvailable ? theme.Text : theme.DisabledLabel), () => actions?.Key(pressed), panel.PadAvailable, theme));
        }

        pad.Children.Add(FloatingPadKey(TillTheme.Icon(LucideIcons.Delete, panel.PadAvailable ? theme.Text : theme.DisabledLabel, 22), () => actions?.Backspace(), panel.PadAvailable, theme));
        top.Children.Add(pad);
        right.Children.Add(new Border { VerticalAlignment = VerticalAlignment.Top, Child = top });

        return Floating(theme, null, panel.Title, panel.CloseKey, panel.Title, PaymentCloseTag, () => actions?.Close(), left, right, panel, panel.Subtitle);
    }

    // ================================================================ the frame

    /// <summary>The card on its scrim: an optional label, the title and its subtitle, the ✕ with its key in the corner, then the two columns.</summary>
    private static Border Floating(
        TillTheme theme, string? label, string title, string closeKey, string closeName, string closeTag, Action close,
        Control left, Control right, object tag, string? subtitle = null)
    {
        var heading = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        if (label is not null)
        {
            heading.Children.Add(theme.Label(label, theme.TextSecondary));
        }

        heading.Children.Add(Wrapped(Words(title, 22, FontWeight.SemiBold, theme.Text, theme)));
        if (subtitle is not null)
        {
            heading.Children.Add(theme.BodySmall(subtitle, theme.TextSecondary));
        }

        var x = new TillKey(theme, KeyLook.Secondary, TillKey.Labelled(Centred(TillTheme.Icon(LucideIcons.X, theme.Text, 20)), closeKey, theme.TextMuted), close, height: FloatingKey)
        {
            Tag = closeTag,
            Width = 72,
        };
        Avalonia.Automation.AutomationProperties.SetName(x, closeName);

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        head.Children.Add(Docked(x, Dock.Right));
        head.Children.Add(heading);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions($"*,20,{FloatingRight}") };
        columns.Children.Add(left);
        Grid.SetColumn(right, 2);
        columns.Children.Add(right);

        var body = new DockPanel();
        body.Children.Add(Docked(head, Dock.Top));
        body.Children.Add(columns);

        var card = Card(theme, null, body);
        card.Width = FloatingWidth;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        card.Tag = tag;

        return new Border { Background = theme.Scrim, Child = card };
    }

    /// <summary>A field: the card ground and the focus ring's edge when it takes the keys, the border's when not.</summary>
    private static Border Field(TillTheme theme, bool active, Control child) => new()
    {
        Height = FloatingKey,
        Background = theme.Card,
        BorderBrush = active ? theme.FocusRing : theme.Border,
        BorderThickness = new Thickness(active ? 2 : 1),
        CornerRadius = new CornerRadius(TillSizes.FieldRadius + 2),
        Padding = new Thickness(12, 0),
        Child = child,
    };

    /// <summary>A field that takes a touch, so the keys go to it: not a key, so it never takes the focus.</summary>
    private static Border Touchable(Border field, Action touched, string tag)
    {
        field.Tag = tag;
        field.Tapped += (_, e) =>
        {
            touched();
            e.Handled = true;
        };
        return field;
    }

    private static StackPanel Labelled(TillTheme theme, string label, Control field, int column)
    {
        var stack = new StackPanel { Spacing = 4, Children = { theme.Label(label, theme.TextSecondary), field } };
        Grid.SetColumn(stack, column);
        return stack;
    }

    /// <summary>A label and its figure at the two ends of a line.</summary>
    private static DockPanel Row(Control label, Control figure)
    {
        var row = new DockPanel();
        row.Children.Add(Docked(new Border { VerticalAlignment = VerticalAlignment.Center, Child = figure }, Dock.Right));
        row.Children.Add(new Border { VerticalAlignment = VerticalAlignment.Center, Child = label });
        return row;
    }

    /// <summary>A refusal: labelled first, on the critical ground with its edge (label before colour).</summary>
    private static Border Refusal(TillTheme theme, PanelMessage message)
    {
        var (mark, ink, fill) = theme.ToneOnSurface(message.Tone);
        return new Border
        {
            Background = fill,
            BorderBrush = mark,
            BorderThickness = new Thickness(0, 0, 0, 0),
            CornerRadius = new CornerRadius(TillSizes.FieldRadius),
            Padding = new Thickness(0),
            Child = new Border
            {
                BorderBrush = mark,
                BorderThickness = new Thickness(TillSizes.SignalRule, 0, 0, 0),
                Padding = new Thickness(10, 6),
                Child = new StackPanel
                {
                    Spacing = 2,
                    Children = { theme.Label(message.Title, ink), Wrapped(theme.BodySmall(message.Body, theme.Text)) },
                },
            },
        };
    }

    private static TillKey PrimaryKey(TillTheme theme, string label, string key, bool available, Action pressed) => new(
        theme,
        KeyLook.Primary,
        TillKey.Labelled(Centred(Words(label, 16, FontWeight.SemiBold, available ? theme.ActionLabel : theme.DisabledLabel, theme)), key, available ? theme.ActionLabel : theme.DisabledLabel),
        pressed,
        available,
        56)
    { Margin = new Thickness(-2, 12, -2, 0) };

    private static TillKey FloatingPadKey(Control face, Action pressed, bool available, TillTheme theme)
    {
        if (face is Layoutable layoutable)
        {
            layoutable.HorizontalAlignment = HorizontalAlignment.Center;
            layoutable.VerticalAlignment = VerticalAlignment.Center;
        }

        return new TillKey(theme, KeyLook.Pad, face, pressed, available, FloatingKey) { Margin = new Thickness(2) };
    }

    private static string MethodIcon(PaymentMethod method) => method switch
    {
        PaymentMethod.Card => LucideIcons.CreditCard,
        PaymentMethod.MobileWallet => LucideIcons.Smartphone,
        PaymentMethod.StoreCredit => LucideIcons.Undo2,
        PaymentMethod.OnAccount => LucideIcons.Notebook,
        _ => LucideIcons.Banknote,
    };
}
