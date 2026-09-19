using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Waymark.Hardware;
using Waymark.Pos.Checkout;

namespace Waymark.Pos;

/// <summary>
/// The till, hop 1 (D-068): a code box, the cart, the total. Deliberately plain.
/// It draws <see cref="TillSession"/> and feeds it codes; every rule lives in the
/// session and the cart, where it is tested.
///
/// <para>
/// <b>The scanner sits in front of every keystroke</b> (D-063). Text input is
/// caught on its way down (tunnel) and fed to the scanner, which holds it until
/// it knows whether a person typed it; typing comes back through
/// <see cref="KeyboardWedgeScanner.Typed"/> and is inserted where the caret is.
/// A scanner's suffix arrives as a key (Enter, Tab), not as text, so those keys
/// go to the scanner first as well.
/// </para>
/// <para>
/// Colours: violet for actions only; notices are neutral and say what they are
/// in words. The semantic colours are Almanac's until a POS decision says
/// otherwise (Hakim, 18/09). Styling is not final.
/// </para>
/// </summary>
public sealed class TillWindow : Window, IDisposable
{
    private static readonly IBrush Violet = new SolidColorBrush(Color.Parse("#5A3AA8"));
    private static readonly IBrush Paper = new SolidColorBrush(Color.Parse("#FBFAFC"));
    private static readonly IBrush Line = new SolidColorBrush(Color.Parse("#E4E1E9"));
    private static readonly IBrush Slate = new SolidColorBrush(Color.Parse("#6B6478"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#14101F"));
    private static readonly FontFamily Mono = new("IBM Plex Mono, Consolas, Courier New");

    private readonly TillSession _session;
    private readonly KeyboardWedgeScanner _scanner;
    private readonly TextBox _input;
    private readonly Border _notice;
    private readonly TextBlock _noticeLabel;
    private readonly TextBlock _noticeText;
    private readonly StackPanel _lines;
    private readonly TextBlock _total;

    public TillWindow(TillSession session, TimeProvider clock)
    {
        _session = session;

        // Built here, on the UI thread, so its silence timer posts back to it (D-063).
        _scanner = new KeyboardWedgeScanner(clock);
        _scanner.Scanned += (_, scan) => Submit(scan.Code);
        _scanner.Typed += (_, text) => InsertTyped(text);

        Title = "Waymark POS";
        Width = 1024;
        Height = 768;
        Background = Paper;

        _input = new TextBox
        {
            Watermark = "Scan, or type a code and press Enter",
            FontFamily = Mono,
            FontSize = 20,
        };

        _noticeLabel = new TextBlock { FontFamily = Mono, FontWeight = FontWeight.SemiBold, Foreground = Ink };
        _noticeText = new TextBlock { Foreground = Ink, TextWrapping = TextWrapping.Wrap };
        var dismiss = ActionButton("OK");
        dismiss.Click += (_, _) => _session.Dismiss();
        _notice = new Border
        {
            IsVisible = false,
            Background = Brushes.White,
            BorderBrush = Ink,
            BorderThickness = new Thickness(0, 3, 0, 0),
            Padding = new Thickness(12),
            Child = new DockPanel
            {
                Children =
                {
                    Docked(dismiss, Dock.Right),
                    new StackPanel { Spacing = 4, Children = { _noticeLabel, _noticeText } },
                },
            },
        };

        _lines = new StackPanel { Spacing = 2 };
        _total = new TextBlock { FontFamily = Mono, FontSize = 24, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Right };

        var pay = ActionButton("Pay cash");
        pay.IsEnabled = false;
        ToolTip.SetTip(pay, "Completing a sale is hop 2.");

        var footer = new DockPanel { Children = { Docked(pay, Dock.Right), _total } };

        Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                Docked(new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12), Children = { _input, _notice } }, Dock.Top),
                Docked(footer, Dock.Bottom),
                new Border
                {
                    BorderBrush = Line,
                    BorderThickness = new Thickness(1),
                    Background = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 12),
                    Child = new ScrollViewer { Content = _lines },
                },
            },
        };

#if DEBUG
        // No scanner at hand: this feeds a code through the real scanner, as a scanner would.
        var simulated = new TextBox { Watermark = "debug: code to scan", FontFamily = Mono, Width = 240 };
        var simulate = ActionButton("Simulate scan");
        simulate.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(simulated.Text))
            {
                _scanner.Scan(simulated.Text.Trim());
                simulated.Text = string.Empty;
            }
        };
        footer.Children.Insert(0, Docked(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { simulated, simulate } }, Dock.Left));
#endif

        AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, _) => _scanner.Reset(), RoutingStrategies.Bubble);

        _session.Changed += (_, _) => Render();
        Opened += (_, _) => _input.Focus();
        Closed += (_, _) => Dispose();

        Render();
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        // Everything goes through the scanner; what was typed comes back via Typed.
        foreach (var character in e.Text)
        {
            _scanner.Accept(character);
        }

        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter or Key.Tab:
                // A scanner's suffix: the scanner decides whether this key ends a scan.
                if (_scanner.Accept(e.Key == Key.Tab ? '\t' : '\r'))
                {
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && ReferenceEquals(Focused(), _input))
                {
                    // A code typed by hand (D-063's open point, hop 1).
                    Submit(_input.Text ?? string.Empty);
                    _input.Text = string.Empty;
                    e.Handled = true;
                }

                break;

            case Key.Escape:
                _session.Dismiss();
                e.Handled = true;
                break;

            case Key.Back or Key.Delete or Key.Left or Key.Right or Key.Up or Key.Down
                or Key.Home or Key.End or Key.PageUp or Key.PageDown:
                // Not text, so the scanner never sees it: release what it holds
                // first, so a held digit lands before the key pressed after it.
                _scanner.Flush();
                break;
        }
    }

    /// <summary>Puts released keystrokes where the caret is, replacing any selection.</summary>
    private void InsertTyped(string text)
    {
        var box = Focused() as TextBox ?? _input;
        var current = box.Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, current.Length);
        var end = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), 0, current.Length);

        box.Text = string.Concat(current.AsSpan(0, start), text, current.AsSpan(end));
        box.SelectionStart = box.SelectionEnd = box.CaretIndex = start + text.Length;
    }

    private async void Submit(string code)
    {
        // async void, as an event handler must be: so nothing may escape it.
        try
        {
            await _session.SubmitAsync(code);
        }
        catch (Exception exception)
        {
            ShowNotice("ERROR", $"{code}: {exception.Message}");
        }

        _input.Focus();
    }

    private void Render()
    {
        if (_session.Notice is { } notice)
        {
            var label = notice.Kind switch
            {
                TillNoticeKind.UnknownCode => "UNKNOWN CODE",
                TillNoticeKind.NotSellable => "NOT SELLABLE",
                _ => "STORESERVER UNAVAILABLE",
            };
            ShowNotice($"{label} · {notice.Code}", notice.Detail);
        }
        else
        {
            _notice.IsVisible = false;
        }

        _lines.Children.Clear();
        foreach (var line in _session.Cart.Lines)
        {
            _lines.Children.Add(LineRow(line));
        }

        _total.Text = _session.Cart.Total is { } total ? $"Total (preview)  {total}" : "Total  —";
    }

    private void ShowNotice(string label, string detail)
    {
        _noticeLabel.Text = label;
        _noticeText.Text = detail;
        _notice.IsVisible = true;
    }

    private Border LineRow(CartLine line)
    {
        var remove = ActionButton("Remove");
        remove.Click += (_, _) => _session.Remove(line.VariantId);

        var name = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = $"{line.ProductName} — {line.VariantName}", Foreground = Ink },
                new TextBlock
                {
                    // Said in words, never by colour alone (Hakim, 18/09).
                    Text = $"MORE THAN RECORDED STOCK · {line.StockOnHand} on hand",
                    FontFamily = Mono,
                    FontSize = 11,
                    Foreground = Slate,
                    IsVisible = line.ExceedsStockOnHand,
                },
            },
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"),
            Margin = new Thickness(12, 8),
            ColumnSpacing = 24,
        };
        grid.Children.Add(Cell(name, 0));
        grid.Children.Add(Cell(Figure($"× {line.Count}"), 1));
        grid.Children.Add(Cell(Figure(line.UnitPrice.ToString()), 2));
        grid.Children.Add(Cell(Figure(line.LineTotal.ToString()), 3));
        grid.Children.Add(Cell(remove, 4));

        return new Border { BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }

    private static TextBlock Figure(string text) => new()
    {
        Text = text,
        FontFamily = Mono,
        Foreground = Ink,
        VerticalAlignment = VerticalAlignment.Center,
    };

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

    private static Button ActionButton(string text) => new()
    {
        Content = text,
        Background = Violet,
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private IInputElement? Focused() => FocusManager?.GetFocusedElement();

    /// <summary>Stops the scanner's silence timer. Called when the window closes.</summary>
    public void Dispose() => _scanner.Dispose();
}
