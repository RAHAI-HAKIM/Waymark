using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Waymark.Pos.Ui;

/// <summary>How a key looks, from the G1 kit's controls (§5).</summary>
public enum KeyLook
{
    /// <summary>The operator's action in a card: violet fill.</summary>
    Primary,

    /// <summary>A bordered key on a card.</summary>
    Secondary,

    /// <summary>A label only: "Ignorer".</summary>
    Ghost,

    /// <summary>Encaisser on the bar: accent violet, ink label.</summary>
    Collect,

    /// <summary>A bordered key on the bar.</summary>
    Bar,

    /// <summary>A PIN pad key: the tile ground, no edge (G1 "Connexion").</summary>
    Pad,

    /// <summary>The choice that is down, among several (the payment's method keys, B6): the tile ground and the action's edge.</summary>
    Chosen,
}

/// <summary>
/// A key: touched on the shop floor, pressed from the keyboard, or reached with Tab (G1 kit §5).
///
/// <para>
/// Not a Fluent <see cref="Button"/>: the Fluent theme repaints a button's ground on hover with
/// its own grey, which would put a colour outside the palette on every key. This draws only what
/// the kit draws. <b>Unavailable is its own look</b> — the <c>border</c> ground with a
/// <c>text-secondary</c> label — <b>never lowered opacity</b>, which makes a key look broken
/// rather than unavailable (design system, "States").
/// </para>
/// <para>
/// The focus ring is 2 px of <c>focus-ring</c>, 2 px outside the key, so it sits on the surface
/// around the key and never on the key's own fill.
/// </para>
/// </summary>
public sealed class TillKey : Border
{
    private readonly Action? _pressed;
    private bool _down;

    public TillKey(TillTheme theme, KeyLook look, Control content, Action? pressed, bool available = true, double height = TillSizes.Key)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _pressed = available ? pressed : null;

        var (ground, edge) = (look, available) switch
        {
            (KeyLook.Collect, true) => (theme.Collect, null),
            (KeyLook.Collect or KeyLook.Bar, false) => (theme.BarDisabledFill, null),
            (KeyLook.Bar, true) => ((IBrush?)null, theme.BarKeyBorder),
            (_, false) => (theme.DisabledFill, null),
            (KeyLook.Primary, true) => (theme.Action, null),
            (KeyLook.Secondary, true) => (theme.Card, theme.Border),
            (KeyLook.Pad, true) => (theme.Tile, null),
            (KeyLook.Chosen, true) => (theme.Tile, theme.Action),
            _ => ((IBrush?)null, (IBrush?)null),
        };

        Focusable = available && pressed is not null;
        Cursor = Focusable ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        Padding = new Thickness(2);
        CornerRadius = new CornerRadius(TillSizes.KeyRadius + 2);
        BorderThickness = new Thickness(2);
        BorderBrush = Brushes.Transparent;

        // Transparent rather than none, as on a cart line: a key with no ground of its own (the bar
        // keys, the staff chip, "Ignorer") only took a touch on its letters and its 1 px edge.
        Background = Brushes.Transparent;

        Child = new Border
        {
            Background = ground,
            BorderBrush = edge,
            BorderThickness = edge is null ? default : new Thickness(look == KeyLook.Chosen ? 2 : 1),
            CornerRadius = new CornerRadius(TillSizes.KeyRadius),
            MinHeight = height,
            Padding = new Thickness(16, 0),
            Child = content,
        };

        GotFocus += (_, _) => BorderBrush = theme.FocusRing;
        LostFocus += (_, _) => BorderBrush = Brushes.Transparent;

        // Pressed on the release over the key, read from the pointer itself, not from Tapped: a
        // second touch inside the double-tap time is a DoubleTapped and never a Tapped, so a PIN
        // typed at a cashier's pace lost every other digit (Hakim, 30/09).
        PointerPressed += (_, e) =>
        {
            if (_pressed is not null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _down = true;
                e.Pointer.Capture(this);
                e.Handled = true;
            }
        };
        PointerReleased += (_, e) =>
        {
            if (!_down)
            {
                return;
            }

            _down = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            if (new Rect(Bounds.Size).Contains(e.GetPosition(this)))
            {
                _pressed?.Invoke();
            }
        };
        PointerCaptureLost += (_, _) => _down = false;

        // Still taken here, so a touch on a key never also selects the line or row it sits on.
        Tapped += (_, e) => e.Handled = true;
        DoubleTapped += (_, e) => e.Handled = true;
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space && _pressed is not null)
            {
                _pressed();
                e.Handled = true;
            }
        };
    }

    /// <summary>A key's label with its F key in the top corner (G1 kit §9), laid out for its look.</summary>
    public static Control Labelled(Control label, string? functionKey, IBrush keyBrush)
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Stretch };
        grid.Children.Add(new Border { VerticalAlignment = VerticalAlignment.Center, Child = label });
        if (functionKey is not null)
        {
            grid.Children.Add(new TextBlock
            {
                Text = functionKey,
                FontFamily = TillTheme.Mono(FontWeight.Medium),
                FontSize = 11,
                Foreground = keyBrush,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, -8, 0),
                FlowDirection = FlowDirection.LeftToRight,
            });
        }

        return grid;
    }
}
