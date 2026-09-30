using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
/// <para>
/// <b>Nobody signed in, no till</b> (A5, D-083): until a PIN is accepted the window shows the
/// sign-in screen, typed digits go to the pad, and a scan is ignored. The ticket survives a lost
/// session; it does not survive "Changer de caissier", which the session refuses while it has lines.
/// </para>
/// <para>
/// <b>It redraws only what changed</b> (<see cref="TillScreen.Compare"/>), and it keeps the cart's
/// scroll viewer and its panel of rows from one frame to the next, so a redraw never moves the
/// ticket; <see cref="CartFollow"/> says when the ticket moves instead.
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
    private readonly Border _cartHeaderHost = new();
    private readonly StackPanel _cartRows = new();
    /// <summary>The ticket's scroll viewer, kept for the window's life (D-084). Tagged so its tests find it among the others.</summary>
    public const string CartScrollTag = "cart";

    private readonly ScrollViewer _cartScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Tag = CartScrollTag };
    private readonly Border _cartEmptyHost = new();
    private readonly Border _railHost = new();
    private readonly Border _bottomHost = new();

    // The manager step floats over everything (30/09): hidden, it takes no touch.
    private readonly Border _approvalHost = new() { IsVisible = false };
    private readonly List<DispatcherTimer> _timers = [];
    private readonly TillActions _actions;
    private readonly SignInFlow _signIn;
    private readonly SignInActions _signInActions;
    private readonly Panel _layout;

    private TillContext? _context;
    private BoardAnswer? _board;
    private string? _selected;
    private bool _draftsOpen;

    // B1 (D-088): the name search under way, its timer, and the "Tickets" list when open.
    private static readonly TimeSpan SearchAfter = TimeSpan.FromMilliseconds(250);
    private readonly Border _fieldChipHost = new();
    private readonly Border _resultsHost = new() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
    private ITimer? _searchTimer;
    private ITimer? _weighTimer;

    /// <summary>A discount being given (B4): the panel's state. The manager's PIN digits live only in <see cref="_pin"/>, never in the screen.</summary>
    private DiscountState? _discount;
    private string _pin = string.Empty;
    private SearchState? _search;
    private TicketsState? _tickets;
    private int _cardIndex;
    private TillScreen? _screen;
    private SignInScreen? _signInScreen;
    private Control? _signInView;

    // What the cart looked like when the window last followed it (CartFollow).
    private CartLine? _followedScan;
    private string? _followedSelection;
#if DEBUG
    private Control? _debug;
#endif

    public TillWindow(TillSession session, ITillServer server, TillIdentity till, TillLanguage language, TillThemeKind themeKind, TimeProvider clock)
    {
        _session = session;
        _server = server;
        _till = till;
        _signIn = new SignInFlow(server, till);
        _clock = clock;
        _text = TillText.For(language);
        _theme = new TillTheme(TillPalette.For(themeKind), language);

        // Built here, on the UI thread, so its silence timer posts back to it (D-063).
        _scanner = new KeyboardWedgeScanner(clock);
        _scanner.Scanned += (_, scan) =>
        {
            // A scan at the sign-in screen belongs to no sale, and a barcode is not a PIN.
            if (_session.SignedIn is not null)
            {
                Submit(scan.Code);
            }
        };
        _scanner.Typed += (_, text) =>
        {
            if (_session.SignedIn is null)
            {
                foreach (var character in text)
                {
                    _signIn.Press(character);
                }
            }
            else if (_discount is { Authorising: true })
            {
                // The manager's PIN (B4): typed digits go to it, never to the field, where they would show.
                foreach (var character in text.Where(c => c is >= '0' and <= '9'))
                {
                    PinDigit(character);
                }
            }
            else
            {
                InsertTyped(text);
            }
        };

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
            },
            AcknowledgeUnconfirmed: _session.AcknowledgeUnconfirmed,
            SetCount: _session.SetCount,
            Operate: Operate,
            ResumeParked: id =>
            {
                _selected = null;
                _session.ResumeParked(id);
            },
            ResumeDraft: id =>
            {
                _selected = null;
                _draftsOpen = false;
                _session.ResumeDraft(id);
            },
            CloseDrafts: () =>
            {
                _draftsOpen = false;
                Render();
            },
            PickResult: PickResult,
            OpenTicket: id => _ = OpenTicketAsync(id),
            TicketsDay: delta =>
            {
                if (_tickets is { } open)
                {
                    _tickets = open with { Day = open.Day.AddDays(delta), List = null, Offline = false };
                    _ = LoadTicketsAsync();
                }
            },
            TicketsScope: () =>
            {
                if (_tickets is { } open)
                {
                    _tickets = open with { AllTills = !open.AllTills, List = null, Offline = false };
                    _ = LoadTicketsAsync();
                }
            },
            CloseTickets: () =>
            {
                _tickets = null;
                Render();
            },
            Reweigh: Reweigh,
            Discounts: new DiscountActions(
                Open: OpenDiscount,
                Form: form =>
                {
                    if (_discount is { } open)
                    {
                        _discount = open with { Form = form, Problem = null };
                        Render();
                    }
                },
                Reason: PickReason,
                Continue: () => _ = ContinueDiscountAsync(),
                Remove: RemoveDiscount,
                Close: CloseDiscount,
                Approver: id =>
                {
                    if (_discount is { } open)
                    {
                        _discount = open with { ManagerId = id, Problem = null };
                        _pin = string.Empty;
                        Render();
                    }
                },
                Digit: PinDigit,
                Backspace: () =>
                {
                    _pin = _pin.Length > 0 ? _pin[..^1] : _pin;
                    Render();
                },
                Clear: () =>
                {
                    _pin = string.Empty;
                    Render();
                },
                Validate: () => _ = ValidatePinAsync(),
                Price: OpenPrice));

        _signInActions = new SignInActions(
            Select: _signIn.Select,
            Digit: _signIn.Press,
            Backspace: _signIn.Backspace,
            Clear: _signIn.Clear,
            Open: OpenTill);

        _input = SearchField();
        _cartScroll.Content = _cartRows;
        _layout = Layout();
        Focusable = true;

        AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(GettingFocusEvent, OnGettingFocus, RoutingStrategies.Bubble);
        AddHandler(GotFocusEvent, (_, _) => _scanner.Reset(), RoutingStrategies.Bubble);

        _session.Changed += (_, _) => Render();
        _signIn.Changed += (_, _) => Render();
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

    private Panel Layout()
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
            Child = new DockPanel { Children = { Docked(searchIcon, Dock.Left), Docked(_fieldChipHost, Dock.Right), _input } },
        };

        // B1: the field is read as the cashier types (FieldInput), and a name is searched once the
        // typing pauses, so the server is asked once per word, not once per letter.
        _input.TextChanged += (_, _) => OnFieldChanged();
        _input.GotFocus += (_, _) => field.BorderBrush = _theme.FocusRing;
        _input.LostFocus += (_, _) => field.BorderBrush = _theme.Border;

        var strip = new Border
        {
            Background = _theme.Tile,
            CornerRadius = new CornerRadius(TillSizes.CardRadius, TillSizes.CardRadius, 0, 0),
            Padding = new Thickness(TillSizes.Margin, 12, TillSizes.Margin, 8),
            Child = new StackPanel { Children = { field, _noticeHost } },
        };

        // The column heads, then the lines in their scroll viewer or, with no line, the empty state
        // in its place. The scroll viewer and its panel are the same controls for the window's life.
        var cartCard = new Border
        {
            Background = _theme.Card,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(TillSizes.CardRadius),
            ClipToBounds = true,
            Child = new DockPanel
            {
                Children =
                {
                    Docked(strip, Dock.Top),

                    // The search's results float over the ticket, under the field (D-088): drawn
                    // over the lines, never among them, so the ticket underneath does not move.
                    new Panel
                    {
                        Children =
                        {
                            new DockPanel
                            {
                                Children =
                                {
                                    Docked(_cartHeaderHost, Dock.Top),
                                    new Panel { Children = { _cartScroll, _cartEmptyHost } },
                                },
                            },
                            _resultsHost,
                        },
                    },
                },
            },
        };

        var middle = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"*,{TillSizes.Gap},{TillSizes.Rail}"),
            Margin = new Thickness(TillSizes.Margin),
        };
        middle.Children.Add(cartCard);
        middle.Children.Add(RailWithDebug());

        var screen = new DockPanel
        {
            Children =
            {
                Docked(_topHost, Dock.Top),
                Docked(_bottomHost, Dock.Bottom),
                middle,
            },
        };

        return new Panel { Children = { screen, _approvalHost } };
    }

    private DockPanel RailWithDebug()
    {
        var host = new DockPanel();
        Grid.SetColumn(host, 2);

#if DEBUG
        // No scanner at hand: this feeds a code through the real scanner, as a scanner would. It
        // sits in the rail's reserved space, which the B-block parts will take.
        var simulated = new TextBox
        {
            PlaceholderText = "debug: code",
            Width = 220,
            FontFamily = TillTheme.Mono(FontWeight.Normal),
            SelectionBrush = _theme.Action,
            SelectionForegroundBrush = _theme.ActionLabel,
        };
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
            // Selected text is the operator's active state (CLAUDE.md §6): the action colour, with
            // the action label's colour on it. Fluent would otherwise paint it in the accent colour
            // Windows is set to, which on a till set to red selected the code in red.
            SelectionBrush = _theme.Action,
            SelectionForegroundBrush = _theme.ActionLabel,
            Background = Brushes.Transparent,
            BorderThickness = default,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),

            // No clipboard menu (F-26): Fluent's is in English, and the till has no use for Cut,
            // Copy or Paste. A local null outranks the theme's setter.
            ContextFlyout = null,
            ContextMenu = null,
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
        if (_session.SignedIn is null)
        {
            RenderSignIn();
            return;
        }

        if (!ReferenceEquals(Content, _layout))
        {
            Content = _layout;
            _signInView = null;

            // Signed in, the window is not focusable: a touch on a line, the notice or the rail finds
            // nothing focusable above it and leaves the scanner's focus where it is. Focusable, the
            // window took it, and Entrée after a code typed by hand then sent nothing.
            Focusable = false;
            _input.Focus();
        }

        if (_selected is not null && !_session.Cart.ActiveLines.Any(line => line.LineId == _selected))
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
            _cardIndex,
            _session.Unconfirmed,
            _session.Parked,
            _session.Drafts,
            _draftsOpen,
            _search,
            _session.NextCount,
            _session.Viewing,
            _tickets,
            _session.Weighing,
            _discount is { } discounting ? discounting with { PinLength = _pin.Length } : null));
        var changes = TillScreen.Compare(_screen, screen);
        _screen = screen;

        if (changes.Top)
        {
            _topHost.Child = TillViews.TopBar(screen.Top, _theme, SwitchCashier, _actions.ResumeParked);
        }

        if (changes.Notice)
        {
            _noticeHost.Child = TillViews.Notice(screen.Notice, _theme);
        }

        if (changes.Cart)
        {
            DrawCart(screen.Cart);
        }

        if (changes.Rail)
        {
            _railHost.Child = TillViews.Rail(screen.Rail, _theme, _actions);
        }

        if (changes.Bottom)
        {
            // The big key: Fermer on a past ticket, Nouvelle vente after a sale, Encaisser otherwise.
            Action primary = _session.Viewing is not null ? _session.CloseView
                : _session.Paid is not null ? _actions.NewSale
                : _actions.Collect;
            _bottomHost.Child = TillViews.BottomBar(screen.Bottom, _theme, primary);
        }

        if (changes.Field)
        {
            _fieldChipHost.Child = TillViews.FieldChip(screen.Field, _theme);
        }

        if (changes.Results)
        {
            _resultsHost.Child = screen.Weigh is { } weigh ? TillViews.Weigh(weigh, _theme)
                : screen.Results is { } results ? TillViews.Results(results, _theme, _actions)
                : null;
        }

        if (changes.Approval)
        {
            _approvalHost.Child = screen.Approval is { } approval ? TillViews.Approval(approval, _theme, _actions) : null;
            _approvalHost.IsVisible = screen.Approval is not null;
        }

        FollowCart();
        KeepScannerFocus();
    }

    /// <summary>
    /// The ticket: the rows go into the same panel, inside the same scroll viewer, every time, so
    /// the offset the cashier scrolled to survives the redraw.
    /// </summary>
    private void DrawCart(CartView cart)
    {
        _cartHeaderHost.Child = TillViews.CartHeader(cart, _theme);
        _cartRows.Children.Clear();
        _cartRows.Children.AddRange(TillViews.CartRows(cart, _theme, _actions));
        _cartScroll.IsVisible = cart.Empty is null;
        _cartEmptyHost.Child = cart.Empty is { } empty ? TillViews.CartEmpty(empty, _theme) : null;
    }

    /// <summary>
    /// Brings the line just scanned, or the actions of the line just touched, into view; anything
    /// else leaves the ticket where the cashier left it (<see cref="CartFollow"/>).
    /// </summary>
    private void FollowCart()
    {
        var target = CartFollow.After(_followedScan, _session.Cart.LastAdded, _followedSelection, _selected);
        _followedScan = _session.Cart.LastAdded;
        _followedSelection = _selected;
        if (target is null)
        {
            return;
        }

        Control? row = null;
        Control? actions = null;
        foreach (var child in _cartRows.Children)
        {
            if (row is null)
            {
                row = child.Tag is LineRow { Struck: false } line && line.LineId == target.LineId ? child : null;
            }
            else
            {
                actions = child.Tag is LineActions ? child : null;
                break;
            }
        }

        if (row is null)
        {
            return;
        }

        // A row has no place until it has been laid out, and these were just drawn.
        _cartRows.UpdateLayout();
        row.BringIntoView();
        if (target.WithActions)
        {
            actions?.BringIntoView();
        }
    }

    /// <summary>
    /// The search field keeps the scanner's focus (G1 kit §9). A key keeps the focus Tab gave it
    /// until a redraw replaces the key; the field takes it back then, because a code typed by hand
    /// is sent by Entrée from the field and from nowhere else.
    /// </summary>
    private void KeepScannerFocus()
    {
        if (Focused() is not Visual focused || !this.IsVisualAncestorOf(focused))
        {
            _input.Focus();
        }
    }

    private void RenderSignIn()
    {
        var screen = SignInScreen.Build(new SignInState(
            _text,
            TimeZoneInfo.Local,
            _clock.GetUtcNow(),
            _session.Server,
            _context,
            _signIn.Staff,
            _signIn.SelectedId,
            _signIn.DigitCount,
            _signIn.Message,
            _signIn.Busy,
            _signIn.MaySubmit));

        // Drawn again only when something on it changed, so the clock does not replace the pad
        // under a finger every few seconds.
        if (ReferenceEquals(Content, _signInView) && SignInScreen.Same(_signInScreen, screen))
        {
            return;
        }

        _signInScreen = screen;
        _signInView = TillViews.SignIn(screen, _theme, _signInActions);
        Content = _signInView;

        // Keys reach the window's handlers only through something focused, and the sign-in screen
        // has no field: the window itself takes the keyboard.
        Focusable = true;
        Focus();
    }

    // =================================================================== server

    private async Task LoadAsync()
    {
        // Asked at once, not after the first health tick: a till that has never reached its
        // server must not open saying "EN LIGNE" for ten seconds. When the server answers, the
        // same check loads the store's names and the list of who may sign in.
        await CheckHealthAsync();
        await RefreshBoardAsync();
    }

    /// <summary>The store, the till and, once somebody is signed in, who they are.</summary>
    private async Task LoadContextAsync()
    {
        if (!string.IsNullOrWhiteSpace(_till.TerminalId))
        {
            _context = await _server.ContextAsync(_till.TerminalId, _session.SignedIn?.StaffId);

            // The store's rounding policy, for the discount preview (B4): the sale's own, so the
            // figure shown is the one charged.
            if (_context?.RoundingPolicy is { } policy)
            {
                _session.SetRoundingPolicy(policy == RoundingPolicies.HalfEven ? Domain.Values.Rounding.HalfEven : Domain.Values.Rounding.HalfUp);
            }
        }
    }

    private async Task RefreshBoardAsync()
    {
        _board = _session.SignedIn is { } person ? await _server.BoardAsync(person.StaffId) : null;
        Render();
    }

    /// <summary>
    /// Whether the server answers, and, when it does, whatever the till could not get from it
    /// before. Both processes start with Windows at the Basic tier, and the till is often the
    /// first up: loaded once at start and never again, the sign-in screen stayed empty and said
    /// "HORS LIGNE" after the server had come up, until the till was restarted.
    /// </summary>
    private async Task CheckHealthAsync()
    {
        var reachable = await _server.HealthAsync();
        _session.ReportHealth(reachable);
        if (!reachable)
        {
            return;
        }

        if (_context is null && !string.IsNullOrWhiteSpace(_till.TerminalId))
        {
            await LoadContextAsync();
            Render();
        }

        if (_session.SignedIn is null && _signIn.NeedsList)
        {
            await _signIn.LoadAsync();
        }
    }

    private async void Decide(string recommendationId, string decision, string? optionId)
    {
        // async void, as an event handler must be: so nothing may escape it.
        try
        {
            if (_session.SignedIn is { } person)
            {
                // The rank check is the server's (CardAudience, D-074): this sends and re-reads.
                await _server.DecideAsync(new DecisionRequest(recommendationId, person.StaffId, decision, optionId));
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
        if (_session.SignedIn is null)
        {
            OnSignInKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case Key.Enter or Key.Tab:
                // A scanner's suffix: the scanner decides whether this key ends a scan.
                if (_scanner.Accept(e.Key == Key.Tab ? '\t' : '\r'))
                {
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && Focused() is TextBox { Tag: LineActions line } quantity)
                {
                    // A count typed between − and + (B2).
                    CommitQuantity(quantity, line);
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && ReferenceEquals(Focused(), _input))
                {
                    EnterInField();
                    e.Handled = true;
                }

                break;

            case Key.F12:
                if (_screen?.Bottom.Primary is { Enabled: true } && _session.Paid is null && _session.Viewing is null)
                {
                    Pay();
                }

                e.Handled = true;
                break;

            case Key.F8:
                RemoveSelected();
                e.Handled = true;
                break;

            case Key.F3:
                Operate(Operation.Park);
                e.Handled = true;
                break;

            case Key.F4:
                // "Remise" under the selected line (B4).
                if (_selected is not null)
                {
                    OpenDiscount(_selected);
                }

                e.Handled = true;
                break;

            case Key.F6:
                Operate(Operation.TicketDiscount);
                e.Handled = true;
                break;

            case Key.Escape:
                // The innermost thing open closes first: a past ticket, the results, the list, then
                // the selection, the notice and the typed count.
                _scanner.Flush();
                if (_discount is not null)
                {
                    // A discount not given after all (B4): nothing changes on the ticket.
                    CloseDiscount();
                }
                else if (_session.Weighing is not null)
                {
                    // A weight not typed after all (B3): nothing is weighed.
                    _session.CancelWeighing();
                    _input.Text = string.Empty;
                }
                else if (_session.Viewing is not null)
                {
                    _session.CloseView();
                }
                else if (_search is not null)
                {
                    _input.Text = string.Empty;
                }
                else if (_tickets is not null)
                {
                    _tickets = null;
                    Render();
                }
                else
                {
                    _selected = null;
                    _session.ResetNextCount();
                    _session.Dismiss();
                    Render();
                }

                e.Handled = true;
                break;

            case Key.Down or Key.Up when _search?.Answer is { Results.Count: > 0 } found:
                // The highlight Entrée takes, moved through the results, never out of them.
                _scanner.Flush();
                var step = e.Key == Key.Down ? 1 : -1;
                _search = _search with { Highlighted = Math.Clamp(_search.Highlighted + step, 0, found.Results.Count - 1) };
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

    /// <summary>
    /// A key that is touched does not take the focus: the search field keeps the scanner's (G1
    /// kit §9). A touch on the staff chip that the session refused redraws only the notice, so the
    /// chip kept the focus, and the next code typed by hand went to the field while its Entrée went
    /// to the chip. Tab still reaches a key, which is how the keyboard presses one.
    /// </summary>
    private void OnGettingFocus(object? sender, FocusChangingEventArgs e)
    {
        if (_session.SignedIn is not null && e.NavigationMethod == NavigationMethod.Pointer && e.NewFocusedElement is TillKey)
        {
            e.TryCancel();
        }
    }

    /// <summary>The sign-in screen's keys: Enter opens the till, Backspace and Escape take digits back.</summary>
    private void OnSignInKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter or Key.Tab:
                if (_scanner.Accept(e.Key == Key.Tab ? '\t' : '\r'))
                {
                    // The end of a scan, which the Scanned handler ignores here.
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    OpenTill();
                    e.Handled = true;
                }

                break;

            case Key.Back or Key.Delete:
                _scanner.Flush();
                _signIn.Backspace();
                e.Handled = true;
                break;

            case Key.Escape:
                _scanner.Flush();
                _signIn.Clear();
                e.Handled = true;
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

    // ============================================================ B1: the field, the search, the tickets

    /// <summary>
    /// Entrée in the search field (D-088): what the text is decides what happens (FieldInput). A
    /// code is looked up as a scan would be; a name takes the highlighted result; a ticket number
    /// opens that ticket; "3*" sets the next count. An empty field closes a past ticket, or starts
    /// the next sale after one.
    /// </summary>
    private void EnterInField()
    {
        // A discount is being given (B4): the field holds its value, and Entrée is Continuer; at
        // the manager step Entrée validates the PIN, as at sign-in.
        if (_discount is { } discounting)
        {
            _ = discounting.Authorising ? ValidatePinAsync() : ContinueDiscountAsync();
            return;
        }

        // A product sold by weight is waiting (B3): what the field holds is its weight, whatever it
        // would otherwise be read as. A weight the unit cannot take stays in the field to be corrected.
        if (_session.Weighing is not null)
        {
            if (_session.ConfirmWeight(_input.Text ?? string.Empty))
            {
                _input.Text = string.Empty;
            }

            return;
        }

        var entry = FieldInput.Read(_input.Text);
        switch (entry.Kind)
        {
            case FieldKind.Nothing when _session.Viewing is not null:
                _session.CloseView();
                break;

            case FieldKind.Nothing when _session.Paid is not null:
                _session.StartNewSale();
                break;

            case FieldKind.Multiplier:
                _session.SetNextCount(entry.Count);
                _input.Text = string.Empty;
                break;

            case FieldKind.Ticket:
                _input.Text = string.Empty;
                _ = OpenTicketAsync(entry.Text);
                break;

            case FieldKind.Name:
                if (_search?.Answer?.Results.ElementAtOrDefault(_search.Highlighted) is { } highlighted)
                {
                    PickResult(highlighted.VariantId);
                }

                break;

            case FieldKind.Code:
                // A code typed by hand (D-063's open point, hop 1): a barcode, else a PLU (D-088).
                Submit(entry.Text);
                _input.Text = string.Empty;
                break;
        }
    }

    /// <summary>The field changed: a name starts the timer; anything else closes the results.</summary>
    private void OnFieldChanged()
    {
        _searchTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _weighTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (_discount is { Authorising: false } open)
        {
            // The discount's value (B4), or its note (F-28): the panel answers as it is typed.
            _search = null;
            _discount = open.Noting
                ? open with { Note = _input.Text ?? string.Empty, Problem = null }
                : open with { Typed = _input.Text ?? string.Empty, Problem = null };
            Render();
            return;
        }

        if (_session.Weighing is not null)
        {
            // The weight typed so far, priced by the server once the cashier pauses, as a name is
            // searched (B3): the card shows the total the sale will charge before Entrée.
            _search = null;
            var typed = _input.Text ?? string.Empty;
            _weighTimer ??= _clock.CreateTimer(
                _ => Dispatcher.UIThread.Post(() => _ = _session.PreviewWeightAsync(_input.Text ?? string.Empty)),
                null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _weighTimer.Change(typed.Length == 0 ? TimeSpan.Zero : SearchAfter, Timeout.InfiniteTimeSpan);
            Render();
            return;
        }

        var entry = FieldInput.Read(_input.Text);
        if (entry.Kind == FieldKind.Name)
        {
            _search = new SearchState(entry.Text, _search?.Query == entry.Text ? _search.Answer : null, false, 0);

            // On the injected clock, as the scanner's silence is (D-063), and back on the UI thread.
            _searchTimer ??= _clock.CreateTimer(_ => Dispatcher.UIThread.Post(() => _ = SearchAsync()), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _searchTimer.Change(SearchAfter, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _search = null;
        }

        Render();
    }

    /// <summary>
    /// Asks the server for the name typed. The answer is dropped if the field says something else by
    /// the time it arrives: an answer to "lai" must not replace the one to "lait".
    /// </summary>
    private async Task SearchAsync()
    {
        if (_search is not { } asked)
        {
            return;
        }

        var answer = await _server.SearchAsync(asked.Query);
        if (_search?.Query != asked.Query)
        {
            return;
        }

        _search = asked with { Answer = answer, Offline = answer is null, Highlighted = FirstSellable(answer) };
        Render();
    }

    private static int FirstSellable(ProductSearchAnswer? answer)
    {
        var index = answer?.Results.ToList().FindIndex(result => result.Outcome == ProductLookupOutcome.Found && result.Code is not null) ?? -1;
        return Math.Max(index, 0);
    }

    // ============================================================ B4: a discount given at the counter

    /// <summary>
    /// "Remise" under a line, or "Remise ticket" (<paramref name="lineId"/> null): the panel opens in
    /// the rail with the shop's reasons, and the value is typed in the field. A discount already
    /// given opens as it is, to be changed or taken off.
    /// </summary>
    private void OpenDiscount(string? lineId)
    {
        if (!_session.MayDiscount)
        {
            return;
        }

        var existing = lineId is null
            ? _session.Cart.TicketDiscount
            : _session.Cart.ActiveLines.FirstOrDefault(line => line.LineId == lineId)?.Discount;
        _draftsOpen = false;
        _tickets = null;
        _search = null;
        _pin = string.Empty;
        _discount = new DiscountState(lineId, existing?.Form ?? DiscountForms.Percent, string.Empty, null, false, existing?.ReasonCode);
        _input.Text = string.Empty;
        Render();
        _input.Focus();
        _ = LoadReasonsAsync();
    }

    /// <summary>
    /// "Prix" under a line (B5, D-092): the panel opens in the rail with the shop's override reasons,
    /// and the new unit price is typed in the field. Never on a weighed line.
    /// </summary>
    private void OpenPrice(string lineId)
    {
        if (!_session.MayDiscount || _session.Cart.ActiveLines.FirstOrDefault(line => line.LineId == lineId) is not { IsWeighed: false } line)
        {
            return;
        }

        _draftsOpen = false;
        _tickets = null;
        _search = null;
        _pin = string.Empty;
        _discount = new DiscountState(lineId, DiscountForms.Amount, string.Empty, null, false, line.Override?.ReasonCode, Kind: CounterKind.PriceOverride);
        _input.Text = string.Empty;
        Render();
        _input.Focus();
        _ = LoadReasonsAsync();
    }

    private async Task LoadReasonsAsync()
    {
        var reasons = _discount?.Kind == CounterKind.PriceOverride
            ? await _server.OverrideReasonsAsync()
            : await _server.DiscountReasonsAsync();
        if (_discount is { } open)
        {
            _discount = open with { Reasons = reasons, Offline = reasons is null };
            Render();
        }
    }

    /// <summary>A reason chosen; the field keeps the scanner's focus, as after any touch.</summary>
    private void PickReason(string code)
    {
        if (_discount is { } open)
        {
            _discount = open with { ReasonCode = code, Problem = null };
            Render();
            _input.Focus();
        }
    }

    private void CloseDiscount()
    {
        _discount = null;
        _pin = string.Empty;
        _input.Text = string.Empty;
        Render();
        _input.Focus();
    }

    /// <summary>"Retirer la remise": the discount is taken off; nothing needs authorising to charge more.</summary>
    private void RemoveDiscount()
    {
        if (_discount is { } open)
        {
            if (open.Kind == CounterKind.PriceOverride && open.LineId is { } lineId)
            {
                _session.OverridePrice(lineId, null);
            }
            else
            {
                _session.Discount(open.LineId, null);
            }

            CloseDiscount();
        }
    }

    /// <summary>
    /// Continuer: a value and a reason, then StoreServer is asked whether the seller may give it
    /// alone (B4). Yes: it is given. No: the manager step.
    /// </summary>
    private async Task ContinueDiscountAsync()
    {
        if (_discount is not { Authorising: false } open || _session.SignedIn is not { } person)
        {
            return;
        }

        var overriding = open.Kind == CounterKind.PriceOverride;
        if (!DiscountEntry.TryParse(open.Typed, overriding ? DiscountForms.Amount : open.Form, out var typed))
        {
            _discount = open with { Problem = DiscountProblem.ValueInvalid };
            Render();
            return;
        }

        if (overriding && BandProblem(open, typed) is { } outside)
        {
            _discount = open with { Problem = outside };
            Render();
            return;
        }

        if (open.ReasonCode is null)
        {
            _discount = open with { Problem = DiscountProblem.NoReason };
            Render();
            return;
        }

        // A reason that asks for a note is not given without one (F-28): the field takes the note.
        var reason = open.Reasons?.ReasonCodes.FirstOrDefault(r => r.Code == open.ReasonCode);
        if (!overriding && reason is { RequiresNote: true })
        {
            if (!open.Noting)
            {
                _discount = open with { Noting = true, Problem = null };
                _input.Text = string.Empty;
                Render();
                _input.Focus();
                return;
            }

            if (open.Note.Trim().Length == 0)
            {
                _discount = open with { Problem = DiscountProblem.NoteMissing };
                Render();
                return;
            }
        }

        var answer = await _server.AuthoriseAsync(new AuthoriseRequest(CapabilityOf(open), null, null), person.Token);
        if (_discount is null)
        {
            return;
        }

        switch (answer?.Outcome)
        {
            case AuthoriseOutcomes.Authorised:
                Give(answer.Authorisation!);
                break;

            case AuthoriseOutcomes.PinRequired:
                var staff = await _server.StaffAsync();
                _pin = string.Empty;
                _discount = _discount with { Authorising = true, Staff = staff, ManagerId = null, Problem = staff is null ? DiscountProblem.Offline : null };
                Render();
                break;

            default:
                _discount = _discount with { Problem = DiscountProblem.Offline, Offline = true };
                Render();
                break;
        }
    }

    private void PinDigit(char digit)
    {
        if (_discount is { Authorising: true } && _pin.Length < 12)
        {
            _pin += digit;
            _discount = _discount with { Problem = null };
            Render();
        }
    }

    /// <summary>Valider: the manager's PIN to StoreServer, which checks it with sign-in's lockout and asks their rank (§3.10).</summary>
    private async Task ValidatePinAsync()
    {
        if (_discount is not { Authorising: true, ManagerId: { } manager } || _session.SignedIn is not { } person)
        {
            return;
        }

        var pin = _pin;
        _pin = string.Empty;
        var answer = await _server.AuthoriseAsync(new AuthoriseRequest(CapabilityOf(_discount), manager, pin), person.Token);
        if (_discount is null)
        {
            return;
        }

        _discount = answer?.Outcome switch
        {
            AuthoriseOutcomes.Authorised => _discount,
            AuthoriseOutcomes.WrongPin => _discount with { Problem = DiscountProblem.WrongPin, AttemptsLeft = answer.AttemptsLeft },
            AuthoriseOutcomes.Locked => _discount with { Problem = DiscountProblem.Locked, LockedUntil = answer.LockedUntil },
            AuthoriseOutcomes.NoPin => _discount with { Problem = DiscountProblem.NoPin },
            AuthoriseOutcomes.NotAllowed or AuthoriseOutcomes.UnknownStaff => _discount with { Problem = DiscountProblem.NotAllowed },
            _ => _discount with { Problem = DiscountProblem.Offline },
        };

        if (answer?.Outcome == AuthoriseOutcomes.Authorised)
        {
            Give(answer.Authorisation!);
            return;
        }

        Render();
    }

    /// <summary>The discount onto the line or the ticket, citing the authorisation the server gave.</summary>
    private void Give(string authorisation)
    {
        if (_discount is not { } open
            || !DiscountEntry.TryParse(open.Typed, open.Kind == CounterKind.PriceOverride ? DiscountForms.Amount : open.Form, out var hundredths)
            || open.Reasons?.ReasonCodes.FirstOrDefault(reason => reason.Code == open.ReasonCode) is not { } reason)
        {
            return;
        }

        if (open.Kind == CounterKind.PriceOverride && open.LineId is { } lineId
            && _session.Cart.ActiveLines.FirstOrDefault(line => line.LineId == lineId) is { } line)
        {
            _session.OverridePrice(lineId, new PriceOverride(
                Domain.Values.Money.FromMinorUnits(hundredths, line.UnitPrice.Currency), reason.Code, reason.LabelFr, reason.LabelAr, authorisation));
        }
        else
        {
            var note = open.Note.Trim();
            _session.Discount(open.LineId, new CounterDiscount(
                open.Form, hundredths, reason.Code, reason.LabelFr, reason.LabelAr, authorisation, note.Length == 0 ? null : note));
        }

        CloseDiscount();
    }

    /// <summary>What the counter panel asks the server to authorise: a discount, rank 2, or a price, rank 3.</summary>
    private static string CapabilityOf(DiscountState? open) =>
        open?.Kind == CounterKind.PriceOverride ? Capabilities.OverridePrice : Capabilities.ApplyDiscount;

    /// <summary>
    /// The band (D-092), asked before the server is: the same rule it checks again. None while the
    /// rule is not written: the server's check still stands.
    /// </summary>
    private DiscountProblem? BandProblem(DiscountState open, long centimes)
    {
        if (_session.Cart.ActiveLines.FirstOrDefault(line => line.LineId == open.LineId) is not { } line)
        {
            return DiscountProblem.ValueInvalid;
        }

        try
        {
            return Domain.Sales.PriceOverride.Check(line.UnitPrice, Domain.Values.Money.FromMinorUnits(centimes, line.UnitPrice.Currency), line.UnitCost).Verdict switch
            {
                Domain.Sales.OverrideVerdict.AboveBand => DiscountProblem.AboveBand,
                Domain.Sales.OverrideVerdict.NotAboveZero => DiscountProblem.NotAboveZero,
                Domain.Sales.OverrideVerdict.Unchanged => DiscountProblem.Unchanged,
                _ => null,
            };
        }
        catch (NotImplementedException)
        {
            return null;
        }
    }

    /// <summary>"Poids" under a line weighed by hand (B3): the weight is typed again in the field, which takes the focus back.</summary>
    private void Reweigh(string lineId)
    {
        _input.Text = string.Empty;
        _session.Reweigh(lineId);
        _input.Focus();
    }

    /// <summary>A result touched, or taken by Entrée: sold as a scan of its code, if it can be. The field clears.</summary>
    private void PickResult(string variantId)
    {
        if (_search?.Answer?.Results.FirstOrDefault(result => result.VariantId == variantId) is not
            { Outcome: ProductLookupOutcome.Found, Product: { } product, Code: { } code })
        {
            return;
        }

        _input.Text = string.Empty;
        _ = _session.AddFoundAsync(product, code);
        _input.Focus();
    }

    /// <summary>"Tickets": today's sales at this till, first (D-088). The rail's other panel closes.</summary>
    private async Task LoadTicketsAsync()
    {
        Render();
        if (_tickets is not { } asked || _session.SignedIn is not { } person)
        {
            return;
        }

        var list = await _server.TicketsAsync(asked.Day, asked.AllTills, person.Token);
        if (_tickets is { } now && now.Day == asked.Day && now.AllTills == asked.AllTills)
        {
            _tickets = now with { List = list, Offline = list is null };
            Render();
        }
    }

    /// <summary>One past ticket, by id or number, opened read-only; or the notice saying why not.</summary>
    private async Task OpenTicketAsync(string idOrNumber)
    {
        try
        {
            if (_session.SignedIn is not { } person)
            {
                return;
            }

            var answer = await _server.TicketAsync(idOrNumber, person.Token);
            switch (answer)
            {
                case { Outcome: TicketOutcomes.Found, Ticket: { } ticket }:
                    _selected = null;
                    _session.View(ticket);
                    break;
                case { Outcome: TicketOutcomes.NotAllowed }:
                    _session.Tell(TillNoticeKind.TicketNotAllowed, idOrNumber, string.Empty);
                    break;
                case { Outcome: TicketOutcomes.Unknown }:
                    _session.Tell(TillNoticeKind.TicketUnknown, idOrNumber, string.Empty);
                    break;
                case null:
                    _session.ReportHealth(reachable: false);
                    break;
            }
        }
        catch (Exception)
        {
            _session.ReportHealth(reachable: false);
        }
    }

    /// <summary>
    /// The rail's operation keys (B2). Whether one is available is the screen model's to say; the
    /// session refuses on its own too, so a key pressed twice, or F3 pressed at the wrong moment,
    /// changes nothing.
    /// </summary>
    private void Operate(Operation operation)
    {
        switch (operation)
        {
            case Operation.Park:
                _selected = null;
                _session.Park();
                break;

            case Operation.CancelTicket:
                _selected = null;
                _session.CancelTicket();
                break;

            case Operation.Drafts:
                _draftsOpen = _session.Drafts.Count > 0;
                _tickets = null;
                Render();
                break;

            case Operation.TicketDiscount:
                OpenDiscount(null);
                break;

            case Operation.Tickets:
                _draftsOpen = false;
                _tickets = new TicketsState(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), TimeZoneInfo.Local).DateTime), false, null, false);
                _ = LoadTicketsAsync();
                break;
        }
    }

    /// <summary>
    /// Entrée in the count between − and +: a count <see cref="QuantityEntry"/> accepts becomes the
    /// line's; anything else is put back as it was. Either way the search field takes the scanner's
    /// focus again, so the next scan lands where it should.
    /// </summary>
    private void CommitQuantity(TextBox field, LineActions line)
    {
        if (QuantityEntry.TryParse(field.Text, out var count))
        {
            _session.SetCount(line.LineId, count);
        }
        else
        {
            field.Text = line.Quantity;
        }

        _input.Focus();
    }

    private void RemoveSelected()
    {
        if (_selected is { } id && _session.Paid is null)
        {
            _selected = null;
            _session.Remove(id);
        }
    }

    /// <summary>"Ouvrir la caisse": sends the PIN, and on a right one hands the till to that person.</summary>
    private async void OpenTill()
    {
        // async void, as an event handler must be: so nothing may escape it.
        try
        {
            if (await _signIn.SubmitAsync() is { } person)
            {
                _session.SignIn(person);
                await LoadContextAsync();
                await RefreshBoardAsync();
            }
        }
        catch (Exception)
        {
            _session.ReportHealth(reachable: false);
        }
    }

    /// <summary>
    /// The staff chip: "Changer de caissier". The session refuses while the ticket has lines; when
    /// it does not, the server's session is ended and the till asks who is next.
    /// </summary>
    private async void SwitchCashier()
    {
        // async void, as an event handler must be: so nothing may escape it.
        try
        {
            if (_session.SignOut() is not { } token)
            {
                return;
            }

            _selected = null;
            _board = null;
            await _server.SignOutAsync(token);
            await LoadContextAsync();
            await _signIn.LoadAsync();
        }
        catch (Exception)
        {
            _session.ReportHealth(reachable: false);
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
            if (_session.SignedIn is null)
            {
                // The server held no session for this till: back to the PIN, the ticket kept.
                _board = null;
                _signIn.Tell(SignInMessageKind.SessionEnded);
                await LoadContextAsync();
                await _signIn.LoadAsync();
                return;
            }

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
    /// <param name="codes">The codes to scan, in order; a "|" among them puts the ticket so far on hold (B2).</param>
    /// <param name="staffId">Touched on the sign-in screen; with <paramref name="pin"/> typed, and <paramref name="open"/> sent.</param>
    /// <param name="cancel">Cancels the ticket once scanned, into the drafts.</param>
    /// <param name="drafts">Opens the drafts list.</param>
    public async Task SnapshotAsync(
        string path, IReadOnlyList<string> codes, bool pay, bool select, string? staffId, string? pin, bool open, bool cancel, bool drafts)
    {
        if (_debug is not null)
        {
            _debug.IsVisible = false;
        }

        await LoadAsync();
        if (staffId is not null)
        {
            _signIn.Select(staffId);
            foreach (var digit in pin ?? string.Empty)
            {
                _signIn.Press(digit);
            }

            if (open && await _signIn.SubmitAsync() is { } person)
            {
                _session.SignIn(person);
                await LoadContextAsync();
                await RefreshBoardAsync();
            }
        }

        foreach (var code in codes)
        {
            if (code == "|")
            {
                _session.Park();
            }
            else
            {
                await _session.SubmitAsync(code);
            }
        }

        if (cancel)
        {
            _session.CancelTicket();
        }

        _draftsOpen = drafts && _session.Drafts.Count > 0;

        if (pay)
        {
            await _session.PayAsync();
        }

        if (select)
        {
            var active = _session.Cart.ActiveLines;
            _selected = active.Count > 0 ? active[^1].LineId : null;
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
        _searchTimer?.Dispose();
        _weighTimer?.Dispose();
    }
}
