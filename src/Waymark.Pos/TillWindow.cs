using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Waymark.Contracts.Pos;
using Waymark.Contracts.Recommendations;
using Waymark.Hardware;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;
using Waymark.Pos.Server;
using Waymark.Pos.Ui;

namespace Waymark.Pos;

/// <summary>
/// The till (session A4, G1): the shell every later session adds to. It feeds codes to
/// <see cref="TillSession"/>, gathers what the till knows into a <see cref="ScreenState"/>, and
/// draws <see cref="TillScreen.Build"/>'s answer. <b>It decides nothing</b>: every label, tone
/// and availability arrives in the model, where it is tested without a window.
///
/// <para>
/// <b>The scanner sits in front of every keystroke</b> (D-063). Text input is caught on its way
/// down (tunnel) and fed to the scanner, which holds it until it knows whether a person typed it;
/// typing comes back through <see cref="KeyboardWedgeScanner.Typed"/> and is inserted where the
/// caret is. A scanner's suffix arrives as a key (Enter, Tab), not as text, so those keys go to
/// the scanner first as well. The search field keeps the scanner's focus (G1 kit §9).
/// </para>
/// </summary>
public sealed class TillWindow : Window, IDisposable
{
    private static readonly TimeSpan HealthEvery = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BoardEvery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ClockEvery = TimeSpan.FromSeconds(15);

    private readonly TillSession _session;
    private readonly ITillServer _server;
    private readonly TillIdentity _till;
    private readonly TillText _text;
    private readonly TillTheme _theme;
    private readonly TimeProvider _clock;
    private readonly KeyboardWedgeScanner _scanner;
    private readonly TextBox _input;
    private readonly Border _topHost = new();
    private readonly Border _noticeHost = new();
    private readonly Border _cartHost = new();
    private readonly Border _railHost = new();
    private readonly Border _bottomHost = new();
    private readonly List<DispatcherTimer> _timers = [];
    private readonly TillActions _actions;

    private TillContext? _context;
    private BoardAnswer? _board;
    private string? _selected;
    private int _cardIndex;
    private TillScreen? _screen;
#if DEBUG
    private Control? _debug;
#endif

    public TillWindow(TillSession session, ITillServer server, TillIdentity till, TillLanguage language, TillThemeKind themeKind, TimeProvider clock)
    {
        _session = session;
        _server = server;
        _till = till;
        _clock = clock;
        _text = TillText.For(language);
        _theme = new TillTheme(TillPalette.For(themeKind), language);

        // Built here, on the UI thread, so its silence timer posts back to it (D-063).
        _scanner = new KeyboardWedgeScanner(clock);
        _scanner.Scanned += (_, scan) => Submit(scan.Code);
        _scanner.Typed += (_, text) => InsertTyped(text);

        Title = "Waymark";
        Width = 1366;
        Height = 768;
        MinWidth = 1024;
        MinHeight = 768;
        Background = _theme.Page;
        FontFamily = _theme.Sans(FontWeight.Normal);
        FlowDirection = _text.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        _actions = new TillActions(
            SelectLine: id =>
            {
                _selected = _selected == id ? null : id;
                Render();
            },
            RemoveSelected: RemoveSelected,
            Collect: Pay,
            NewSale: () => _session.StartNewSale(),
            Accept: (card, option) => Decide(card, "accept", option),
            Dismiss: card => Decide(card, "dismiss", null),
            NextCard: () =>
            {
                _cardIndex++;
                Render();
            });

        _input = SearchField();
        Content = Layout();

        AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, _) => _scanner.Reset(), RoutingStrategies.Bubble);

        _session.Changed += (_, _) => Render();
        Opened += (_, _) =>
        {
            _input.Focus();
            Start(ClockEvery, Render);
            Start(HealthEvery, () => _ = CheckHealthAsync());
            Start(BoardEvery, () => _ = RefreshBoardAsync());
            _ = LoadAsync();
        };
        Closed += (_, _) => Dispose();

        Render();
    }

    // =================================================================== layout

    private DockPanel Layout()
    {
        // The search strip: the field, then the notice slot under it (G1, "Avis").
        var searchIcon = TillTheme.Icon(LucideIcons.Search, _theme.TextMuted, 20);
        searchIcon.Margin = new Thickness(12, 0, 8, 0);
        var field = new Border
        {
            Background = _theme.Card,
            BorderBrush = _theme.FocusRing,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(TillSizes.FieldRadius + 2),
            Height = TillSizes.Key,
            Child = new DockPanel { Children = { Docked(searchIcon, Dock.Left), _input } },
        };
        _input.GotFocus += (_, _) => field.BorderBrush = _theme.FocusRing;
        _input.LostFocus += (_, _) => field.BorderBrush = _theme.Border;

        var strip = new Border
        {
            Background = _theme.Tile,
            CornerRadius = new CornerRadius(TillSizes.CardRadius, TillSizes.CardRadius, 0, 0),
            Padding = new Thickness(TillSizes.Margin, 12, TillSizes.Margin, 8),
            Child = new StackPanel { Children = { field, _noticeHost } },
        };

        var cartCard = new Border
        {
            Background = _theme.Card,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(TillSizes.CardRadius),
            ClipToBounds = true,
            Child = new DockPanel { Children = { Docked(strip, Dock.Top), _cartHost } },
        };

        var middle = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"*,{TillSizes.Gap},{TillSizes.Rail}"),
            Margin = new Thickness(TillSizes.Margin),
        };
        middle.Children.Add(cartCard);
        middle.Children.Add(RailWithDebug());

        return new DockPanel
        {
            Children =
            {
                Docked(_topHost, Dock.Top),
                Docked(_bottomHost, Dock.Bottom),
                middle,
            },
        };
    }

    private DockPanel RailWithDebug()
    {
        var host = new DockPanel();
        Grid.SetColumn(host, 2);

#if DEBUG
        // No scanner at hand: this feeds a code through the real scanner, as a scanner would. It
        // sits in the rail's reserved space, which the B-block parts will take.
        var simulated = new TextBox { PlaceholderText = "debug: code", Width = 220, FontFamily = TillTheme.Mono(FontWeight.Normal) };
        var simulate = new TillKey(_theme, KeyLook.Secondary, _theme.BodySmall("Simulate scan", _theme.Text), () =>
        {
            if (!string.IsNullOrWhiteSpace(simulated.Text))
            {
                _scanner.Scan(simulated.Text.Trim());
                simulated.Text = string.Empty;
            }
        });
        // Docked above the rail, so the rail's own content (the paid ticket, the Almanac card) starts
        // below it instead of being drawn underneath it.
        var debug = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 12),
            Children = { _theme.Label("DEBUG"), simulated, simulate },
        };
        DockPanel.SetDock(debug, Dock.Top);
        _debug = debug;
        host.Children.Add(debug);
#endif

        host.Children.Add(_railHost);
        return host;
    }

    private TextBox SearchField()
    {
        var input = new TextBox
        {
            PlaceholderText = _text.SearchPlaceholder,
            FontFamily = _theme.Sans(FontWeight.Normal),
            FontSize = 16,
            Foreground = _theme.Text,
            CaretBrush = _theme.Text,
            Background = Brushes.Transparent,
            BorderThickness = default,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
        };

        // The field is drawn by its own border above; Fluent's focus and hover grounds would put
        // a colour outside the palette inside it.
        foreach (var key in new[]
        {
            "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
        })
        {
            input.Resources[key] = Brushes.Transparent;
        }

        input.Resources["TextControlPlaceholderForeground"] = _theme.TextMuted;
        input.Resources["TextControlPlaceholderForegroundPointerOver"] = _theme.TextMuted;
        input.Resources["TextControlPlaceholderForegroundFocused"] = _theme.TextMuted;
        return input;
    }

    // =================================================================== render

    private void Render()
    {
        if (_selected is not null && !_session.Cart.ActiveLines.Any(line => line.VariantId == _selected))
        {
            _selected = null;
        }

        var screen = TillScreen.Build(new ScreenState(
            _text,
            TimeZoneInfo.Local,
            _clock.GetUtcNow(),
            _session.Cart,
            _session.Notice,
            _session.Paid,
            _session.LastSale,
            _session.LastSaleAt,
            _session.Server,
            _context,
            _selected,
            _board,
            _cardIndex));
        _screen = screen;

        _topHost.Child = TillViews.TopBar(screen.Top, _theme);
        _noticeHost.Child = TillViews.Notice(screen.Notice, _theme);
        _cartHost.Child = TillViews.CartTable(screen.Cart, _theme, _actions);
        _railHost.Child = TillViews.Rail(screen.Rail, _theme, _actions);
        _bottomHost.Child = TillViews.BottomBar(screen.Bottom, _theme, _actions, _session.Paid is not null);
    }

    // =================================================================== server

    private async Task LoadAsync()
    {
        // Asked at once, not after the first health tick: a till that has never reached its
        // server must not open saying "EN LIGNE" for ten seconds.
        await CheckHealthAsync();

        if (!string.IsNullOrWhiteSpace(_till.TerminalId))
        {
            _context = await _server.ContextAsync(_till.TerminalId, _till.StaffId);
        }

        await RefreshBoardAsync();
    }

    private async Task RefreshBoardAsync()
    {
        _board = string.IsNullOrWhiteSpace(_till.StaffId) ? null : await _server.BoardAsync(_till.StaffId);
        Render();
    }

    private async Task CheckHealthAsync() => _session.ReportHealth(await _server.HealthAsync());

    private async void Decide(string recommendationId, string decision, string? optionId)
    {
        // async void, as an event handler must be: so nothing may escape it.
        try
        {
            if (!string.IsNullOrWhiteSpace(_till.StaffId))
            {
                // The rank check is the server's (CardAudience, D-074): this sends and re-reads.
                await _server.DecideAsync(new DecisionRequest(recommendationId, _till.StaffId, decision, optionId));
            }

            await RefreshBoardAsync();
        }
        catch (Exception)
        {
            await RefreshBoardAsync();
        }
    }

    // ================================================================= keyboard

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
                    var typed = _input.Text ?? string.Empty;
                    if (typed.Trim().Length == 0 && _session.Paid is not null)
                    {
                        // Entrée on an empty field after a sale: "Nouvelle vente" (G1).
                        _session.StartNewSale();
                    }
                    else
                    {
                        // A code typed by hand (D-063's open point, hop 1).
                        Submit(typed);
                        _input.Text = string.Empty;
                    }

                    e.Handled = true;
                }

                break;

            case Key.F12:
                if (_screen?.Bottom.Primary is { Enabled: true } && _session.Paid is null)
                {
                    Pay();
                }

                e.Handled = true;
                break;

            case Key.F8:
                RemoveSelected();
                e.Handled = true;
                break;

            case Key.Escape:
                _selected = null;
                _session.Dismiss();
                Render();
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

    // ================================================================== actions

    private void RemoveSelected()
    {
        if (_selected is { } id && _session.Paid is null)
        {
            _selected = null;
            _session.Remove(id);
        }
    }

    private async void Submit(string code)
    {
        // async void, as an event handler must be: so nothing may escape it.
        try
        {
            await _session.SubmitAsync(code);
        }
        catch (Exception)
        {
            _session.ReportHealth(reachable: false);
        }

        _input.Focus();
    }

    private async void Pay()
    {
        // async void, as an event handler must be: so nothing may escape it.
        try
        {
            await _session.PayAsync();
            await RefreshBoardAsync();
        }
        catch (Exception)
        {
            _session.ReportHealth(reachable: false);
        }

        _input.Focus();
    }

    private void Start(TimeSpan every, Action tick)
    {
        var timer = new DispatcherTimer { Interval = every };
        timer.Tick += (_, _) => tick();
        timer.Start();
        _timers.Add(timer);
    }

    private IInputElement? Focused() => FocusManager?.GetFocusedElement();

    private static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

#if DEBUG
    /// <summary>
    /// Renders the window to a PNG after feeding it <paramref name="codes"/> through the real
    /// scanner path, for reviewing the shell against the G1 boards. Debug builds only.
    /// </summary>
    public async Task SnapshotAsync(string path, IReadOnlyList<string> codes, bool pay, bool select)
    {
        if (_debug is not null)
        {
            _debug.IsVisible = false;
        }

        await LoadAsync();
        foreach (var code in codes)
        {
            await _session.SubmitAsync(code);
        }

        if (pay)
        {
            await _session.PayAsync();
        }

        if (select)
        {
            var active = _session.Cart.ActiveLines;
            _selected = active.Count > 0 ? active[^1].VariantId : null;
        }

        Render();
        await Task.Delay(300);
        var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
        bitmap.Render(this);
        await using var file = File.Create(path);
        bitmap.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
#endif

    /// <summary>Stops the scanner's silence timer and the shell's timers. Called when the window closes.</summary>
    public void Dispose()
    {
        foreach (var timer in _timers)
        {
            timer.Stop();
        }

        _scanner.Dispose();
    }
}
