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
/// session, and "Changer de caissier" puts it on hold for whoever comes next (D-087); a ticket with
/// only struck lines is cancelled first, so that it is recorded (D-106).
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

    // The panels that freeze the ticket float over everything (D-094): the payment and the manager's
    // PIN, never both. Hidden, the host takes no touch.
    private readonly Border _floatingHost = new() { IsVisible = false };

    /// <summary>The payment panel while it is open (B6); the ticket is frozen under it.</summary>
    private PaymentState? _payment;
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

    // A refund being prepared on the past ticket open (B9), and which ticket it belongs to.
    private RefundState? _refund;
    private string? _refundOf;

    /// <summary>The ticket and the rail side by side: the rail's column narrows with the window (F-29).</summary>
    private Grid? _middle;

    /// <summary>The ticket a manager's PIN is being asked for (D-109), until the step is answered.</summary>
    private string? _ticketAsked;

    /// <summary>
    /// The authorisation that opened the past ticket on screen, when it took a manager's PIN (D-109):
    /// its refund cites it, and it is forgotten when the ticket closes.
    /// </summary>
    private (string TransactionId, string Authorisation)? _ticketApproval;

    // The customers (B7): the attached customer's tab, the carnet open in the ticket's place, the
    // panel floating over the ticket, and what a PIN step was asked for.
    private TabAnswer? _customerTab;
    private CarnetState? _carnet;
    private CustomerPanelState? _customerPanel;
    private (string Action, string? Limit)? _pendingChange;

    // "Plus…" open in the rail (B10).
    private bool _moreOpen;

    // The till's cash session as the server last gave it (C1, D-111); null until it answered.
    private CashSessionState? _drawer;

    // A blind count once confirmed (D-111): closing again starts from it, never from a new count.
    private string? _frozenCount;

    /// <summary>D-100: how long the cashier stops typing before a customer search is asked by itself.</summary>
    public static readonly TimeSpan CustomerSearchAfter = TimeSpan.FromSeconds(3);

    private ITimer? _customerTimer;
    private readonly Border _clientHost = new();
    private readonly FormActions _formActions;
    private readonly CarnetActions _carnetActions;
    private readonly Border _carnetHost = new() { IsVisible = false };
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
            // Nor while a panel freezes the ticket (D-094): the notice slot says the scan was ignored.
            // With the drawer shut the opening panel says so itself, where the cashier is looking (C1).
            if (_customerPanel is { Kind: CustomerPanelKind.DrawerOpen, Sending: false } shut)
            {
                _customerPanel = shut with { Checked = true };
                Render();
            }

            if (_session.SignedIn is not null && _payment is null && _discount is not { Authorising: true } && _refund is null
                && _customerPanel is null && _carnet is null)
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
            else if (_payment is { } paying)
            {
                // The payment (B6): typed keys go to its amount or its reference, never the field.
                _payment = text.Aggregate(paying, PaymentScreen.Press);
                Render();
            }
            else if (_customerPanel is { } panel)
            {
                // A customer panel (B7): the number, the name or the amount it asks for.
                _customerPanel = text.Aggregate(panel, CustomerScreen.Press);
                Render();
                PauseThenSearch();
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

        // A narrow till gives the ticket the room the rail can spare (F-29).
        SizeChanged += (_, e) => Fit(e.NewSize.Width);
        Background = _theme.Page;
        FontFamily = _theme.Sans(FontWeight.Normal);
        FlowDirection = _text.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        _actions = new TillActions(
            SelectLine: id =>
            {
                // While a refund is prepared (B9), a line of the past ticket is touched to bring it back.
                if (_refund is { Quote: null } refund && _session.Viewing is { } ticket
                    && id.StartsWith("past-", StringComparison.Ordinal) && int.TryParse(id.AsSpan(5), System.Globalization.CultureInfo.InvariantCulture, out var index))
                {
                    _refund = RefundScreen.Touch(refund, ticket, index);
                    _discount = _discount is { } open ? open with { Problem = null } : null;
                    Render();
                    return;
                }

                _selected = _selected == id ? null : id;
                Render();
            },
            RemoveSelected: RemoveSelected,
            Collect: OpenPayment,
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
                    if (_discount is { Kind: CounterKind.Refund } refunding && _refund is { } refund && _session.Viewing is { } ticket)
                    {
                        // "− 1", "+ 1", "En rayon" on the line touched (B9).
                        _refund = RefundScreen.Press(refund, ticket, form);
                        _discount = refunding with { Problem = null };
                        Render();
                    }
                    else if (_discount is { } open)
                    {
                        _discount = open with { Form = form, Problem = null };
                        Render();
                    }
                },
                Reason: PickReason,
                Continue: () => _ = ContinueDiscountAsync(),
                Remove: RemoveDiscount,
                Close: () =>
                {
                    // The ✕ on the manager step of a refund goes back to the refund, not out of it.
                    if (_discount is { Kind: CounterKind.Refund, Authorising: true } refunding)
                    {
                        _pin = string.Empty;
                        _discount = refunding with { Authorising = false, Problem = null };
                        Render();
                    }
                    else if (_discount is { Kind: CounterKind.CreateCustomer or CounterKind.ChangeTab or CounterKind.TabOverride or CounterKind.PaidOut or CounterKind.StrikeLine or CounterKind.OpenTicket })
                    {
                        _pin = string.Empty;
                        _discount = null;
                        Render();
                    }
                    else
                    {
                        CloseDiscount();
                    }
                },
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
                Price: OpenPrice),
            Payments: new PaymentActions(
                Choose: method =>
                {
                    if (_refund is { Quote: not null, Sending: false } refund)
                    {
                        _refund = refund with { To = method == Domain.Enums.PaymentMethod.StoreCredit ? RefundDestinations.StoreCredit : RefundDestinations.Cash, Refused = null };
                        Render();
                    }
                    else
                    {
                        UpdatePayment(open => PaymentScreen.Choose(open, method));
                    }
                },
                Key: key => UpdatePayment(open => PaymentScreen.Press(open, key)),
                Backspace: () => UpdatePayment(PaymentScreen.Backspace),
                WholeRest: () => UpdatePayment(PaymentScreen.WholeRest),
                RemovePart: index => UpdatePayment(open => PaymentScreen.RemovePart(open, index)),
                TypeInReference: reference => UpdatePayment(open => open with { OnReference = reference && open.AddingPart }),
                Primary: () =>
                {
                    if (_refund is { Quote: not null })
                    {
                        _ = SendRefundAsync(null);
                    }
                    else
                    {
                        PaymentPrimary();
                    }
                },
                Close: () =>
                {
                    if (_refund is { Quote: not null })
                    {
                        CloseRefundPanel();
                    }
                    else
                    {
                        ClosePayment();
                    }
                },
                Action: OpenClient));

        _formActions = new FormActions(
            Field: id => UpdateCustomer(panel => panel with { OnName = id == CustomerScreen.NameField }),
            Row: id => UpdateCustomer(panel => panel.Kind is CustomerPanelKind.Repay or CustomerPanelKind.PettyCash
                ? panel with { ReasonCode = id, Refused = null, OnName = false }
                : panel with { Chosen = id, Refused = null }),
            Key: CustomerKey,
            Digit: key => UpdateCustomer(panel => CustomerScreen.Press(panel, key)),
            Backspace: () => UpdateCustomer(CustomerScreen.Backspace),
            Clear: () => UpdateCustomer(CustomerScreen.Clear),
            Primary: () => _ = CustomerPrimaryAsync(),
            Close: CloseCustomerPanel);
        _carnetActions = new CarnetActions(
            Change: () => OpenCustomerPanel(new CustomerPanelState(CustomerPanelKind.ChangeTab)),
            Repay: () =>
            {
                OpenCustomerPanel(new CustomerPanelState(CustomerPanelKind.Repay));
                _ = LoadCashReasonsAsync();
            },
            Close: CloseCarnet);

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
            Child = new StackPanel { Children = { new DockPanel { Children = { Docked(_clientHost, Dock.Right), field } }, _noticeHost } },
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

                            // The carnet takes the ticket's place (B7): it does not float.
                            _carnetHost,
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
        _middle = middle;
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

        return new Panel { Children = { screen, _floatingHost } };
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

    /// <summary>
    /// The window's width class (F-29): narrow below <see cref="TillSizes.NarrowBelow"/>. When it
    /// changes the rail's column is resized and everything is drawn again, since a view may lay
    /// itself out differently.
    /// </summary>
    private void Fit(double width)
    {
        var narrow = width < TillSizes.NarrowBelow;
        if (narrow == _theme.Narrow)
        {
            return;
        }

        _theme.Narrow = narrow;
        if (_middle is { } middle)
        {
            middle.ColumnDefinitions[2].Width = new GridLength(_theme.Rail);
        }

        _screen = null;
        Render();
    }

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

        // The tab read is the attached customer's: a ticket resumed with another, or none, drops it (B7).
        if (_customerTab is not null && _customerTab.Customer?.CustomerId != _session.Cart.Customer?.CustomerId)
        {
            _customerTab = null;
        }

        // A refund belongs to the past ticket it was opened on: closed, or another opened, it goes (B9).
        // The PIN that opened a past ticket opens that ticket only, while it is open (D-109).
        if (_ticketApproval is { } kept && _session.Viewing?.TransactionId != kept.TransactionId)
        {
            _ticketApproval = null;
        }

        if (_refund is not null && _session.Viewing?.TransactionId != _refundOf)
        {
            EndRefund();
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
            _discount is { } discounting ? discounting with { PinLength = _pin.Length } : null,
            _payment,
            _refund,
            new CustomerScreenState(_context?.CustomerModule ?? false, _context?.TabAsPart ?? true, _customerTab, _carnet, _customerPanel),
            _moreOpen,
            _drawer));
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

        if (changes.Approval || changes.Payment)
        {
            // The manager step floats over whatever asked for it: a payment's tab, a customer panel (B7).
            _floatingHost.Child = screen.Approval is { } approval ? TillViews.Approval(approval, _theme, _actions)
                : screen.Form is { } form ? TillViews.Form(form, _theme, _formActions)
                : screen.Payment is { } payment ? TillViews.Payment(payment, _theme, _actions.Payments)
                : null;
            _floatingHost.IsVisible = _floatingHost.Child is not null;
        }

        if (changes.Client)
        {
            _clientHost.Child = screen.Client is { } client ? TillViews.ClientKeyView(client, _theme, OpenClient, Detach) : null;
        }

        if (changes.Carnet)
        {
            _carnetHost.Child = screen.Carnet is { } carnet ? TillViews.Carnet(carnet, _theme, _carnetActions) : null;
            _carnetHost.IsVisible = _carnetHost.Child is not null;
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

        // The drawer, asked again until the server has said (C1): a till signed in before its server
        // answered would otherwise never show that the drawer is shut.
        if (_session.SignedIn is not null && _drawer is null)
        {
            await LoadDrawerAsync();
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
                else if (e.Key == Key.Enter && _discount is { Authorising: true })
                {
                    // A manager's PIN, whatever asked for it: Entrée validates it.
                    _ = ValidatePinAsync();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && _customerPanel is not null)
                {
                    // A customer panel (B7): Entrée is its key that goes on.
                    _ = CustomerPrimaryAsync();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && _refund is { Quote: not null } && _discount is not { Authorising: true })
                {
                    // The refund's floating panel (B9): Entrée is "Rembourser".
                    _ = SendRefundAsync(null);
                    e.Handled = true;
                }
                else if (_payment is { } paying)
                {
                    // The payment (B6): Entrée is its key that goes on; Tab moves between amount and reference.
                    if (e.Key == Key.Enter)
                    {
                        PaymentPrimary();
                    }
                    else if (paying.AddingPart)
                    {
                        _payment = paying with { OnReference = !paying.OnReference };
                        Render();
                    }

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

            case Key.F5:
                // "Client" (B7): the search, or the attached customer's carnet.
                OpenClient();
                e.Handled = true;
                break;

            case Key.F12:
                // Encaisser opens the payment (B6); with it open, F12 is its key that goes on, as Entrée.
                if (_payment is not null)
                {
                    PaymentPrimary();
                }
                else
                {
                    OpenPayment();
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
                if (_discount is { Kind: CounterKind.CreateCustomer or CounterKind.ChangeTab or CounterKind.TabOverride or CounterKind.PaidOut or CounterKind.StrikeLine or CounterKind.OpenTicket or CounterKind.CloseSession })
                {
                    // Back from a customer's PIN step to what asked for it (B7).
                    _pin = string.Empty;
                    _discount = null;
                    Render();
                }
                else if (_customerPanel is not null)
                {
                    CloseCustomerPanel();
                }
                else if (_payment is not null)
                {
                    // Not paid after all (B6): the ticket is as it was.
                    ClosePayment();
                }
                else if (_carnet is not null)
                {
                    CloseCarnet();
                }
                else if (_discount is { Kind: CounterKind.Refund, Authorising: true } refunding)
                {
                    // Back from the manager step to the refund (B9).
                    _pin = string.Empty;
                    _discount = refunding with { Authorising = false, Problem = null };
                    Render();
                }
                else if (_refund is { Quote: not null })
                {
                    // Not paid out after all (B9): back to the lines and the reason.
                    CloseRefundPanel();
                }
                else if (_discount is not null)
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
                else if (_draftsOpen)
                {
                    // "Brouillons" closes as every other panel does (block B review): only its own
                    // "Fermer" did, and the rail's keys stayed hidden behind it.
                    _draftsOpen = false;
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

            case Key.Back or Key.Delete when _customerPanel is not null && _discount is not { Authorising: true }:
                _scanner.Flush();
                UpdateCustomer(CustomerScreen.Backspace);
                e.Handled = true;
                break;

            case Key.Back or Key.Delete when _payment is not null:
                // The payment's fields are not text boxes: ⌫ takes from whichever has the keys.
                _scanner.Flush();
                _payment = PaymentScreen.Backspace(_payment);
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

    /// <summary>"Annuler ticket" (B8, D-097): the cancel panel opens in the rail with the shop's cancel reasons.</summary>
    private void OpenCancel()
    {
        if (!_session.MayCancel)
        {
            return;
        }

        _selected = null;
        _draftsOpen = false;
        _tickets = null;
        _search = null;
        _pin = string.Empty;
        _discount = new DiscountState(null, DiscountForms.Amount, string.Empty, null, false, null, Kind: CounterKind.Cancel);
        _input.Text = string.Empty;
        Render();
        _input.Focus();
        _ = LoadReasonsAsync();
    }

    /// <summary>
    /// The cancel to the server, which records it before the ticket goes (B8, D-097). Recorded: the
    /// ticket goes to the drafts, as before. A manager needed: the PIN step, then this again with the
    /// authorisation. Refused or no answer: the ticket stays, and the panel says why.
    /// </summary>
    private async Task CancelAsync(string? authorisation)
    {
        if (_discount is not { Kind: CounterKind.Cancel } open || _session.SignedIn is not { } person)
        {
            return;
        }

        if (open.ReasonCode is null)
        {
            _discount = open with { Problem = DiscountProblem.NoReason };
            Render();
            return;
        }

        if (_session.VoidRequestFor(open.ReasonCode, authorisation) is not { } request)
        {
            CloseDiscount();
            return;
        }

        var answer = await _server.VoidAsync(request, person.Token);
        if (_discount is null)
        {
            return;
        }

        switch (answer?.Outcome)
        {
            case VoidOutcomes.Voided:
                _session.CancelTicket();
                CloseDiscount();
                break;

            case VoidOutcomes.PinRequired:
                var staff = await _server.ApproversAsync(Capabilities.VoidTransaction);
                _pin = string.Empty;
                _discount = _discount with { Authorising = true, Staff = staff, ManagerId = null, Problem = staff is null ? DiscountProblem.Offline : null };
                Render();
                break;

            case VoidOutcomes.Refused:
                _discount = _discount with { Authorising = false, Problem = DiscountProblem.CancelRefused };
                // Why, in the till's language, where a sale's refusal is said (D-107).
                _session.Tell(TillNoticeKind.SaleRefused, "-", answer.Reason ?? string.Empty, answer.Refusal);
                Render();
                break;

            case VoidOutcomes.NotSignedIn:
                // The server holds no session for this till: said as that, not as an outage (block B review).
                CloseDiscount();
                _session.Tell(TillNoticeKind.NotSignedIn, "-", string.Empty);
                break;

            default:
                _discount = _discount with { Authorising = false, Problem = DiscountProblem.Offline };
                Render();
                break;
        }
    }

    private async Task LoadReasonsAsync()
    {
        var reasons = _discount?.Kind switch
        {
            CounterKind.PriceOverride => await _server.OverrideReasonsAsync(),
            CounterKind.Cancel => await _server.VoidReasonsAsync(),
            CounterKind.Refund => await _server.ReturnReasonsAsync(),
            _ => await _server.DiscountReasonsAsync(),
        };
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
        // Out of a refund's panel is out of the refund (B9): the past ticket stays open, read-only.
        if (_discount is { Kind: CounterKind.Refund })
        {
            _refund = null;
            _refundOf = null;
        }

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

        // A cancel takes no value: its reason, then the server (B8).
        if (open.Kind == CounterKind.Cancel)
        {
            await CancelAsync(null);
            return;
        }

        // Nor a refund: its lines and reason, then the server's quote (B9).
        if (open.Kind == CounterKind.Refund)
        {
            await QuoteRefundAsync();
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
                var staff = await _server.ApproversAsync(CapabilityOf(_discount));
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
        // A cancel's authorisation goes with the cancel, sent again (B8).
        if (_discount is { Kind: CounterKind.Cancel })
        {
            _ = CancelAsync(authorisation);
            return;
        }

        // A customer's step goes back to what asked for it (B7).
        switch (_discount?.Kind)
        {
            case CounterKind.CreateCustomer:
                _pin = string.Empty;
                _discount = null;
                _ = CreateCustomerAsync(authorisation);
                return;
            case CounterKind.ChangeTab:
                _pin = string.Empty;
                _discount = null;
                _ = ChangeTabAsync(authorisation);
                return;
            case CounterKind.PaidOut:
                _pin = string.Empty;
                _discount = null;
                _ = SendCashAsync(authorisation);
                return;
            case CounterKind.CloseSession:
                _pin = string.Empty;
                _discount = null;
                _ = SendCloseAsync(authorisation);
                return;
            case CounterKind.TabOverride:
                _pin = string.Empty;
                _discount = null;
                if (_payment is { } paying)
                {
                    _payment = paying with { TabOverride = authorisation };
                    PaymentPrimary();
                }

                return;
            case CounterKind.StrikeLine:
                // The line is struck citing who let it be (D-106); the sale carries it to its row.
                var struck = _discount.LineId;
                _pin = string.Empty;
                _discount = null;
                if (struck is not null)
                {
                    _session.Remove(struck, authorisation);
                }

                Render();
                _input.Focus();
                return;
            case CounterKind.OpenTicket:
                _pin = string.Empty;
                _discount = null;
                if (_ticketAsked is { } asked)
                {
                    _ticketAsked = null;
                    _ = OpenTicketAsync(asked, authorisation);
                }

                return;
        }

        // A refund's too, back over the panel it came from (B9).
        if (_discount is { Kind: CounterKind.Refund } refunding)
        {
            _pin = string.Empty;
            _discount = refunding with { Authorising = false, Problem = null };
            _ = SendRefundAsync(authorisation);
            return;
        }

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
    private static string CapabilityOf(DiscountState? open) => open?.Kind switch
    {
        CounterKind.PriceOverride => Capabilities.OverridePrice,
        CounterKind.Cancel or CounterKind.StrikeLine => Capabilities.VoidTransaction,
        CounterKind.OpenTicket => Capabilities.ViewOtherTickets,
        CounterKind.Refund => Capabilities.Refund,
        CounterKind.CreateCustomer => Capabilities.CreateCustomer,
        CounterKind.ChangeTab or CounterKind.TabOverride => Capabilities.ManageCredit,
        CounterKind.PaidOut => Capabilities.PaidOut,
        CounterKind.CloseSession => Capabilities.CloseSession,
        _ => Capabilities.ApplyDiscount,
    };

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
    /// <param name="authorisation">A manager's authorisation to open an earlier day's or another till's ticket (D-109); null to ask without one.</param>
    private async Task OpenTicketAsync(string idOrNumber, string? authorisation = null)
    {
        try
        {
            if (_session.SignedIn is not { } person)
            {
                return;
            }

            var answer = await _server.OpenTicketAsync(idOrNumber, person.Token, authorisation);
            switch (answer)
            {
                case { Outcome: TicketOutcomes.Found, Ticket: { } ticket }:
                    _selected = null;
                    // Kept while this ticket is open, for its refund; forgotten when it closes (D-109).
                    _ticketApproval = authorisation is null ? null : (ticket.TransactionId, authorisation);
                    _session.View(ticket);
                    break;
                case { Outcome: TicketOutcomes.PinRequired }:
                    // An earlier day's or another till's: a manager's PIN opens this one ticket (D-109).
                    _ticketAsked = idOrNumber;
                    await AuthoriseCustomerAsync(
                        CounterKind.OpenTicket, _text.OpenTicketApprovalTitle, _text.OpenTicketApprovalSummary(idOrNumber));
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

    // =================================================================== B7: customers and the tab

    /// <summary>
    /// "Client" (F5): with a customer attached, their carnet; else the search, floating over the frozen
    /// ticket (D-094). While a refund waits for store credit's customer, the search attaches to it (B9a).
    /// </summary>
    private void OpenClient()
    {
        if (_context is not { CustomerModule: true } || _payment is not null || _discount is { Authorising: true })
        {
            return;
        }

        if (_session.Viewing is not null)
        {
            if (_refund is not null)
            {
                OpenCustomerPanel(new CustomerPanelState(CustomerPanelKind.Search, ForRefund: true));
            }

            return;
        }

        if (_session.Cart.Customer is not null)
        {
            _ = OpenCarnetAsync();
            return;
        }

        OpenCustomerPanel(new CustomerPanelState(CustomerPanelKind.Search));
    }

    /// <summary>The ✕ beside the attached name: the ticket goes on without a customer.</summary>
    private void Detach()
    {
        if (_refund is { } refund && _session.Viewing is not null)
        {
            _refund = refund with { Customer = null, Quote = null };
        }
        else
        {
            _session.Cart.Attach(null);
            _customerTab = null;
        }

        Render();
    }

    private void OpenCustomerPanel(CustomerPanelState panel)
    {
        _search = null;
        _customerPanel = panel;
        Render();
    }

    private void CloseCustomerPanel()
    {
        // The drawer's opening and a close's result have no way out but their own key (C1, D-111).
        if (_customerPanel is { Sending: true } or { Kind: CustomerPanelKind.DrawerOpen or CustomerPanelKind.DrawerClosed })
        {
            return;
        }

        _customerPanel = null;
        Render();
        _input.Focus();
    }

    private void UpdateCustomer(Func<CustomerPanelState, CustomerPanelState> change)
    {
        if (_customerPanel is { Sending: false } open)
        {
            _customerPanel = change(open);
            Render();
            PauseThenSearch();
        }
    }

    /// <summary>
    /// The search asked by itself once nothing has been typed for <see cref="CustomerSearchAfter"/> (D-100):
    /// long enough for a full name to be finished, so half a name never lists anyone. Every key starts the
    /// wait again; Entrée asks at once.
    /// </summary>
    private void PauseThenSearch()
    {
        _customerTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (_customerPanel is not { Kind: CustomerPanelKind.Search, Found: null } panel || !CustomerScreen.Searchable(panel.Phone))
        {
            return;
        }

        _customerTimer ??= _clock.CreateTimer(
            _ => Dispatcher.UIThread.Post(() =>
            {
                if (_session.SignedIn is { } person)
                {
                    _ = FindCustomersAsync(person.Token);
                }
            }),
            null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _customerTimer.Change(CustomerSearchAfter, Timeout.InfiniteTimeSpan);
    }

    /// <summary>A secondary key of a panel: "Tout le dû", "Geler", "Dégeler", "Fermer le carnet".</summary>
    private void CustomerKey(string key)
    {
        if (_customerPanel is not { Sending: false } panel)
        {
            return;
        }

        switch (key)
        {
            case CustomerScreen.WholeDueKey when _carnet?.Tab?.Balance is { } balance:
                _customerPanel = panel with { Typed = balance.Replace('.', ','), Refused = null };
                Render();
                break;
            case CustomerScreen.FreezeKey:
                AskChange(LimitActions.Freeze, null);
                break;
            case CustomerScreen.UnfreezeKey:
                AskChange(LimitActions.Unfreeze, null);
                break;
            case CustomerScreen.CloseTabKey:
                AskChange(LimitActions.Set, null);
                break;
            case DrawerScreen.SwitchUserKey when panel.Kind == CustomerPanelKind.DrawerOpen:
                // Somebody else opens the drawer: the till asks who (C1).
                SwitchCashier();
                break;
            case DrawerScreen.RecountKey when panel is { Kind: CustomerPanelKind.DrawerConfirm, Frozen: false }:
                _customerPanel = panel with { Kind = CustomerPanelKind.DrawerCount, Name = string.Empty, OnName = false, Refused = null };
                Render();
                break;
            case CustomerScreen.CashInKey or CustomerScreen.CashOutKey:
                // Which way (B10): a reason for the other way no longer fits.
                var fits = CustomerScreen.ReasonsFor(panel.Reasons, key).Any(reason => reason.Code == panel.ReasonCode);
                _customerPanel = panel with { Direction = key, ReasonCode = fits ? panel.ReasonCode : null, Refused = null };
                Render();
                break;
        }
    }

    /// <summary>The key that goes on, for the panel open.</summary>
    private async Task CustomerPrimaryAsync()
    {
        if (_customerPanel is not { Sending: false } panel || _session.SignedIn is not { } person)
        {
            return;
        }

        switch (panel.Kind)
        {
            case CustomerPanelKind.Search when panel.Found?.Customers is { Count: > 0 } found:
                if (found.FirstOrDefault(customer => customer.CustomerId == panel.Chosen) is { } chosen)
                {
                    AttachCustomer(new AttachedCustomer(chosen.CustomerId, chosen.Name, chosen.Phone), panel.ForRefund);
                }

                break;

            case CustomerPanelKind.Search when panel.Found is not null:
                // Nobody by that name or number: create them, with what was already typed.
                _customerPanel = CustomerScreen.IsNumber(panel.Phone)
                    ? panel with { Kind = CustomerPanelKind.Create, OnName = true, Refused = null, Checked = false }
                    : panel with { Kind = CustomerPanelKind.Create, Name = panel.Phone, Phone = string.Empty, OnName = false, Refused = null, Checked = false };
                Render();
                break;

            case CustomerPanelKind.Search when !CustomerScreen.Searchable(panel.Phone):
                _customerPanel = panel with { Checked = true };
                Render();
                break;

            case CustomerPanelKind.Search:
                await FindCustomersAsync(person.Token);
                break;

            case CustomerPanelKind.Create:
                if (panel.Name.Trim().Length is 0 or > 100 || !Domain.Customers.PhoneNumber.TryNormalise(panel.Phone, out _))
                {
                    _customerPanel = panel with { Checked = true };
                    Render();
                    break;
                }

                await AuthoriseCustomerAsync(CounterKind.CreateCustomer, _text.ApproveCreateTitle(panel.Name.Trim()),
                    $"{_text.PhoneTitle} {CustomerScreen.Phone(panel.Phone)}");
                break;

            case CustomerPanelKind.ChangeTab:
                if (DiscountEntry.TryParse(panel.Typed, DiscountForms.Amount, out var hundredths))
                {
                    AskChange(LimitActions.Set, Contracts.Figures.Amount(hundredths));
                }

                break;

            case CustomerPanelKind.Repay:
                await RepayAsync(panel, person.Token);
                break;

            case CustomerPanelKind.PettyCash:
                await SendCashAsync(null);
                break;

            case CustomerPanelKind.Clock:
                await ClockAsync(panel, person.Token);
                break;

            case CustomerPanelKind.DrawerOpen:
                await OpenDrawerAsync(panel, person.Token);
                break;

            case CustomerPanelKind.DrawerCount:
                // 1 of 3 to 2 of 3: nothing is sent yet. The note has the keys where there is one to write.
                if (DrawerScreen.TypedHundredths(panel) is not null && !panel.Checked && panel.Drawer?.Open is not null)
                {
                    _customerPanel = panel with { Kind = CustomerPanelKind.DrawerConfirm, OnName = DrawerScreen.ShowsFigures(panel), Refused = null };
                    Render();
                }

                break;

            case CustomerPanelKind.DrawerConfirm:
                if (!DrawerScreen.ShowsFigures(panel))
                {
                    // A blind count is frozen here, before anything is said of it (D-111).
                    _frozenCount = panel.Typed;
                    _customerPanel = panel with { Frozen = true };
                    await SendCloseAsync(null);
                }
                else if (DrawerScreen.MayConfirm(panel, _context?.Currency ?? "DZD"))
                {
                    await SendCloseAsync(null);
                }

                break;

            case CustomerPanelKind.DrawerNote:
                if (panel.Name.Trim().Length > 0)
                {
                    await SendCloseAsync(null);
                }

                break;

            case CustomerPanelKind.DrawerClosed:
                // "Terminer ramène à la connexion": the till asks who is next, and they count a float.
                SwitchCashier();
                break;

            default:
                CloseCustomerPanel();
                break;
        }
    }

    /// <summary>The number to the server; each customer it lists is a consultation, and the server logs it (D-061).</summary>
    private async Task FindCustomersAsync(string token)
    {
        if (_customerPanel is not { Kind: CustomerPanelKind.Search, Sending: false, Found: null } panel || !CustomerScreen.Searchable(panel.Phone))
        {
            return;
        }

        _customerPanel = panel with { Sending = true, Refused = null };
        Render();
        var answer = CustomerScreen.IsNumber(panel.Phone)
            ? await _server.FindCustomersAsync(panel.Phone, _till.TerminalId ?? string.Empty, token)
            : await _server.FindCustomersByNameAsync(panel.Phone.Trim(), _till.TerminalId ?? string.Empty, token);
        if (_customerPanel is null)
        {
            return;
        }

        _customerPanel = answer switch
        {
            { Outcome: CustomerOutcomes.Ok, Customers: { } customers } => _customerPanel with
            {
                Sending = false,
                Found = answer,
                Chosen = customers.Count == 1 ? customers[0].CustomerId : null,
            },
            { Outcome: CustomerOutcomes.TooMany } => _customerPanel with { Sending = false, Refused = _text.TooManyNamed },
            { Reason: { } reason } => _customerPanel with { Sending = false, Refused = reason },
            _ => _customerPanel with { Sending = false, Refused = _text.CarnetOffline },
        };
        Render();
    }

    /// <summary>The customer onto the ticket, or onto the refund for its store credit (B9a), then quoted again.</summary>
    private void AttachCustomer(AttachedCustomer customer, bool forRefund)
    {
        _customerPanel = null;
        if (forRefund && _refund is { } refund)
        {
            _refund = refund with { Customer = customer, To = RefundDestinations.StoreCredit, Quote = null };
            Render();
            _ = QuoteRefundAsync();
            return;
        }

        _session.Cart.Attach(customer);
        _customerTab = null;
        Render();
        _input.Focus();
    }

    /// <summary>
    /// A step a rank may need (B7): the seller's own rank asked first; below it, the PIN step, whose
    /// authorisation goes back to what asked (<see cref="Give"/>).
    /// </summary>
    /// <param name="lineId">The line the step is about, for a strike (D-106); null otherwise.</param>
    private async Task AuthoriseCustomerAsync(CounterKind kind, string title, string summary, string? lineId = null)
    {
        if (_session.SignedIn is not { } person)
        {
            return;
        }

        var capability = CapabilityOf(new DiscountState(null, DiscountForms.Amount, string.Empty, null, false, null, Kind: kind));
        var answer = await _server.AuthoriseAsync(new AuthoriseRequest(capability, null, null), person.Token);
        switch (answer?.Outcome)
        {
            case AuthoriseOutcomes.Authorised:
                _discount = new DiscountState(lineId, DiscountForms.Amount, string.Empty, null, false, null, Kind: kind);
                Give(answer.Authorisation!);
                break;

            case AuthoriseOutcomes.PinRequired:
                // Who may approve this, as the server says: not everybody who may open the till.
                var staff = await _server.ApproversAsync(capability);
                _pin = string.Empty;
                _discount = new DiscountState(
                    lineId, DiscountForms.Amount, string.Empty, null, false, null, Authorising: true, Staff: staff,
                    Problem: staff is null ? DiscountProblem.Offline : null, Kind: kind, Title: title, Summary: summary);
                Render();
                break;

            case AuthoriseOutcomes.NotSignedIn:
                _session.Tell(TillNoticeKind.NotSignedIn, "-", string.Empty);
                break;

            default:
                if (kind is CounterKind.StrikeLine or CounterKind.OpenTicket)
                {
                    // Nothing was struck and nothing opened: the server could not say.
                    _session.ReportHealth(reachable: false);
                }
                else if (_customerPanel is { } panel)
                {
                    _customerPanel = panel with { Refused = _text.CarnetOffline };
                }
                else if (_payment is { } paying)
                {
                    _payment = paying with { Refused = _text.CarnetOffline };
                }

                Render();
                break;
        }
    }

    private async Task CreateCustomerAsync(string authorisation)
    {
        if (_customerPanel is not { Kind: CustomerPanelKind.Create } panel || _session.SignedIn is not { } person)
        {
            return;
        }

        _customerPanel = panel with { Sending = true, Refused = null };
        Render();
        var answer = await _server.CreateCustomerAsync(
            new CreateCustomerRequest(_till.TerminalId ?? string.Empty, panel.Name.Trim(), panel.Phone, authorisation), person.Token);
        if (_customerPanel is null)
        {
            return;
        }

        if (answer is { Outcome: CustomerOutcomes.Ok, Customer: { } created })
        {
            AttachCustomer(new AttachedCustomer(created.CustomerId, created.Name, created.Phone), panel.ForRefund);
            return;
        }

        _customerPanel = _customerPanel with { Sending = false, Refused = answer is null ? _text.CarnetOffline : RefusalText.Say(_text, answer.Refusal, answer.Reason) };
        Render();
    }

    /// <summary>The carnet in the ticket's place: the server's figures, and the server logs the look (D-061).</summary>
    private async Task OpenCarnetAsync()
    {
        if (_session.Cart.Customer is not { } customer || _session.SignedIn is not { } person)
        {
            return;
        }

        _carnet = new CarnetState(null, false, _clock.GetUtcNow(), _context?.StaffName);
        Render();
        var tab = await _server.TabAsync(customer.CustomerId, _till.TerminalId ?? string.Empty, person.Token);
        if (_carnet is null)
        {
            return;
        }

        _carnet = _carnet with { Tab = tab, Offline = tab is null };
        if (tab is { Outcome: CustomerOutcomes.Ok })
        {
            _customerTab = tab;
        }

        Render();
    }

    private void CloseCarnet()
    {
        _carnet = null;
        _customerPanel = null;
        Render();
        _input.Focus();
    }

    /// <summary>The attached customer's tab, for the payment's Carnet key.</summary>
    private async Task LoadTabAsync()
    {
        if (_session.Cart.Customer is not { } customer || _session.SignedIn is not { } person)
        {
            return;
        }

        var tab = await _server.TabAsync(customer.CustomerId, _till.TerminalId ?? string.Empty, person.Token);
        if (tab is { Outcome: CustomerOutcomes.Ok } && _session.Cart.Customer?.CustomerId == customer.CustomerId)
        {
            _customerTab = tab;
            Render();
        }
    }

    private async Task LoadCashReasonsAsync()
    {
        var reasons = await _server.CashReasonsAsync();
        if (_customerPanel is { Kind: CustomerPanelKind.Repay } panel)
        {
            // A shop with one reason for cash coming in has nothing to choose: it is chosen (block B review).
            var fitting = reasons?.ReasonCodes.Where(reason => reason.Direction != Contracts.Reference.CashDirections.Out).ToList();
            _customerPanel = panel with
            {
                Reasons = reasons,
                ReasonCode = panel.ReasonCode ?? (fitting is { Count: 1 } ? fitting[0].Code : null),
                Refused = reasons is null ? _text.CarnetOffline : null,
            };
            Render();
        }
    }

    /// <summary>A change to the tab (rank 3): the owner's PIN, then the server, which refuses above the tenant's ceiling.</summary>
    private void AskChange(string action, string? limit)
    {
        var name = _carnet?.Tab?.Customer is { } customer ? new AttachedCustomer(customer.CustomerId, customer.Name, customer.Phone).ShortName : string.Empty;
        _pendingChange = (action, limit);
        var summary = action switch
        {
            LimitActions.Freeze => _text.FreezeKey,
            LimitActions.Unfreeze => _text.UnfreezeKey,
            _ when limit is null => _text.CloseTabKey,
            _ => $"{_text.CurrentLimit} {ShownWire(_carnet?.Tab?.Limit, _carnet?.Tab?.Currency)} → {ShownWire(limit, _carnet?.Tab?.Currency)}",
        };
        _ = AuthoriseCustomerAsync(CounterKind.ChangeTab, _text.ApproveChangeTitle(name), summary);
    }

    private async Task ChangeTabAsync(string authorisation)
    {
        if (_pendingChange is not { } change || _carnet?.Tab?.Customer is not { } customer || _session.SignedIn is not { } person)
        {
            return;
        }

        _pendingChange = null;
        if (_customerPanel is { } sending)
        {
            _customerPanel = sending with { Sending = true, Refused = null };
            Render();
        }

        var answer = await _server.ChangeLimitAsync(
            customer.CustomerId, new LimitRequest(_till.TerminalId ?? string.Empty, change.Action, change.Limit, authorisation), person.Token);
        if (answer is { Outcome: CustomerOutcomes.Ok })
        {
            _carnet = _carnet is { } open ? open with { Tab = answer } : null;
            _customerTab = answer;
            _customerPanel = null;
        }
        else if (_customerPanel is { } panel)
        {
            _customerPanel = panel with { Sending = false, Refused = answer is null ? _text.CarnetOffline : RefusalText.Say(_text, answer.Refusal, answer.Reason) };
        }

        Render();
    }

    /// <summary>A repayment in cash (D-055): a paid-in on the drawer and a payment on the tab, both or neither.</summary>
    private async Task RepayAsync(CustomerPanelState panel, string token)
    {
        if (_carnet?.Tab is not { Customer: { } customer } before || panel.ReasonCode is not { } reason
            || !DiscountEntry.TryParse(panel.Typed, DiscountForms.Amount, out var hundredths))
        {
            return;
        }

        // The tab's own rule, as the panel showed it: never more than is owed, never nothing (D-055),
        // and a part of the due only in whole cash steps (D-108).
        var balance = WireFigures.Money(before.Balance ?? "0", before.Currency ?? "DZD");
        if (!Domain.Customers.Tab.Repay(balance, Domain.Values.Money.FromMinorUnits(hundredths, balance.Currency)).Accepted)
        {
            return;
        }

        _customerPanel = panel with { Sending = true, Refused = null };
        Render();
        var answer = await _server.RepayAsync(
            customer.CustomerId, new RepaymentRequest(_till.TerminalId ?? string.Empty, Contracts.Figures.Amount(hundredths), reason), token);
        if (_customerPanel is null)
        {
            return;
        }

        if (answer is { Outcome: CustomerOutcomes.Ok })
        {
            _carnet = _carnet is { } open ? open with { Tab = answer } : null;
            _customerTab = answer;
            _customerPanel = new CustomerPanelState(CustomerPanelKind.Repaid, Before: before, After: answer, At: _clock.GetUtcNow());
        }
        else
        {
            _customerPanel = _customerPanel with { Sending = false, Refused = answer is null ? _text.CarnetOffline : RefusalText.Say(_text, answer.Refusal, answer.Reason) };
        }

        Render();
    }

    // =================================================================== B10: petite caisse, pointage

    private async Task LoadPettyReasonsAsync()
    {
        var reasons = await _server.CashReasonsAsync();
        if (_customerPanel is { Kind: CustomerPanelKind.PettyCash } panel)
        {
            _customerPanel = panel with { Reasons = reasons, Refused = reasons is null ? _text.CarnetOffline : null };
            Render();
        }
    }

    private async Task LoadClockStaffAsync()
    {
        var staff = await _server.StaffAsync();
        if (_customerPanel is { Kind: CustomerPanelKind.Clock } panel)
        {
            _customerPanel = panel with { Staff = staff, Refused = staff is null ? _text.CarnetOffline : null };
            Render();
        }
    }

    /// <summary>
    /// "Enregistrer" (B10, D-102): the paid-in or paid-out to the server, which records it before the
    /// till says so. Below the shop's rank for a paid-out: the PIN step, then this again with the
    /// authorisation. Refused or no answer: the panel stays and says why.
    /// </summary>
    private async Task SendCashAsync(string? authorisation)
    {
        if (_customerPanel is not { Kind: CustomerPanelKind.PettyCash, Sending: false, Direction: { } direction, ReasonCode: { } reason } panel
            || _session.SignedIn is not { } person
            || !DiscountEntry.TryParse(panel.Typed, DiscountForms.Amount, out var hundredths) || hundredths <= 0)
        {
            return;
        }

        _customerPanel = panel with { Sending = true, Refused = null };
        Render();
        var note = panel.Name.Trim();
        var answer = await _server.CashMovementAsync(
            new CashMovementRequest(_till.TerminalId ?? string.Empty, direction, Contracts.Figures.Amount(hundredths), reason, note.Length == 0 ? null : note, authorisation),
            person.Token);
        if (_customerPanel is not { Kind: CustomerPanelKind.PettyCash } now)
        {
            return;
        }

        switch (answer?.Outcome)
        {
            case CashMovementOutcomes.Recorded:
                _customerPanel = now with { Kind = CustomerPanelKind.PettyCashDone, Sending = false };
                break;

            case CashMovementOutcomes.PinRequired:
                _customerPanel = now with { Sending = false };
                var label = panel.Reasons?.ReasonCodes.FirstOrDefault(r => r.Code == reason) is { } chosen
                    ? (_text.RightToLeft ? chosen.LabelAr : chosen.LabelFr)
                    : reason;
                await AuthoriseCustomerAsync(
                    CounterKind.PaidOut, _text.CashOutKey,
                    $"{_text.AmountTitle} {ShownWire(Contracts.Figures.Amount(hundredths), _context?.Currency)} · {label}");
                return;

            default:
                _customerPanel = now with { Sending = false, Refused = answer is null ? _text.CarnetOffline : RefusalText.Say(_text, answer.Refusal, answer.Reason) };
                break;
        }

        Render();
    }

    // =================================================================== C1: the drawer opened and closed

    /// <summary>
    /// The till's cash session, asked of the server (C1, D-111). With none open, the opening panel
    /// floats over the empty ticket and stays until a float is counted: nothing sells without it.
    /// No answer: nothing is shown of the drawer, and the health check asks again.
    /// </summary>
    private async Task LoadDrawerAsync()
    {
        if (_session.SignedIn is not { } person || string.IsNullOrWhiteSpace(_till.TerminalId))
        {
            return;
        }

        var state = await _server.CashSessionAsync(_till.TerminalId, person.Token);
        if (state is not { Outcome: CashSessionOutcomes.Ok } || _session.SignedIn?.Token != person.Token)
        {
            return;
        }

        _drawer = state;
        if (state.Open is null && _customerPanel is null or { Kind: CustomerPanelKind.DrawerOpen })
        {
            _search = null;
            _moreOpen = false;
            _customerPanel = (_customerPanel ?? new CustomerPanelState(CustomerPanelKind.DrawerOpen)) with { Drawer = state };
        }
        else if (state.Open is not null && _customerPanel is { Kind: CustomerPanelKind.DrawerOpen })
        {
            // Opened meanwhile, from another sign-in at this till.
            _customerPanel = null;
        }

        Render();
    }

    /// <summary>"Ouvrir la caisse": the counted float to the server, which opens the session before the till sells.</summary>
    private async Task OpenDrawerAsync(CustomerPanelState panel, string token)
    {
        if (DrawerScreen.TypedHundredths(panel) is not { } hundredths)
        {
            return;
        }

        _customerPanel = panel with { Sending = true, Refused = null };
        Render();
        var answer = await _server.OpenCashSessionAsync(
            new OpenCashSessionRequest(_till.TerminalId ?? string.Empty, Contracts.Figures.Amount(hundredths)), token);
        if (_customerPanel is not { Kind: CustomerPanelKind.DrawerOpen } now)
        {
            return;
        }

        if (answer is { Outcome: CashSessionOutcomes.Opened, State: { } state })
        {
            _drawer = state;
            _customerPanel = null;
            Render();
            _input.Focus();
            return;
        }

        if (answer?.Refusal?.Code == RefusalCodes.SessionAlreadyOpen)
        {
            // It is open already: the till reads it and sells.
            _customerPanel = now with { Sending = false };
            await LoadDrawerAsync();
            return;
        }

        _customerPanel = now with { Sending = false, Refused = answer is null ? _text.DrawerOffline : RefusalText.Say(_text, answer.Refusal, answer.Reason) };
        Render();
    }

    /// <summary>
    /// "Clôturer la caisse": the session read again, then the count. A ticket with lines, or one on
    /// hold, is settled first (D-111): the panel says so and goes no further. A blind count already
    /// confirmed is not typed again.
    /// </summary>
    private async Task OpenCloseAsync()
    {
        if (_session.SignedIn is not { } person)
        {
            return;
        }

        var pending = (_session.Paid is null && _session.Cart.Lines.Count > 0) || _session.Parked.Count > 0 || _session.Unconfirmed is not null;
        OpenCustomerPanel(new CustomerPanelState(CustomerPanelKind.DrawerCount, Drawer: _drawer, Checked: pending));
        var state = await _server.CashSessionAsync(_till.TerminalId ?? string.Empty, person.Token);
        if (_customerPanel is not { Kind: CustomerPanelKind.DrawerCount } panel)
        {
            return;
        }

        if (state is { Outcome: CashSessionOutcomes.Ok })
        {
            _drawer = state;
            if (state.Open is null)
            {
                // Closed meanwhile: there is nothing to count, and a float to.
                _customerPanel = null;
                await LoadDrawerAsync();
                return;
            }

            panel = panel with { Drawer = state };
        }
        else
        {
            panel = panel with { Refused = _text.DrawerOffline };
        }

        if (_frozenCount is { } frozen && !DrawerScreen.ShowsFigures(panel) && !pending)
        {
            panel = panel with { Kind = CustomerPanelKind.DrawerConfirm, Typed = frozen, Frozen = true };
        }

        _customerPanel = panel;
        Render();
    }

    /// <summary>
    /// The count to the server, which works out what the drawer should hold and closes the session
    /// before the till says so (C1, D-111). A note asked: the note's panel. Somebody who may not close
    /// alone: the PIN step, then this again with the authorisation. Refused or no answer: the panel
    /// stays and says why.
    /// </summary>
    private async Task SendCloseAsync(string? authorisation)
    {
        if (_customerPanel is not { Kind: CustomerPanelKind.DrawerConfirm or CustomerPanelKind.DrawerNote, Sending: false } panel
            || _session.SignedIn is not { } person
            || DrawerScreen.TypedHundredths(panel) is not { } hundredths)
        {
            return;
        }

        _customerPanel = panel with { Sending = true, Refused = null };
        Render();
        var note = panel.Name.Trim();
        var counted = Contracts.Figures.Amount(hundredths);
        var answer = await _server.CloseCashSessionAsync(
            new CloseCashSessionRequest(_till.TerminalId ?? string.Empty, counted, note.Length == 0 ? null : note, authorisation), person.Token);
        if (_customerPanel is not { Kind: CustomerPanelKind.DrawerConfirm or CustomerPanelKind.DrawerNote } now)
        {
            return;
        }

        switch (answer?.Outcome)
        {
            case CashSessionOutcomes.Closed when answer.Closed is { } closed:
                _frozenCount = null;
                _customerPanel = now with { Kind = CustomerPanelKind.DrawerClosed, Closed = closed, Sending = false, OnName = false };
                _drawer = _drawer is { } before
                    ? before with { Open = null, LastClose = new LastCloseWire(closed.ZReportNumber, closed.ClosedAt, closed.ValidatedBy) }
                    : null;
                break;

            case CashSessionOutcomes.NoteRequired:
                _customerPanel = now with { Kind = CustomerPanelKind.DrawerNote, Sending = false, OnName = true, Frozen = true };
                break;

            case CashSessionOutcomes.PinRequired:
                _customerPanel = now with { Sending = false };
                await AuthoriseCustomerAsync(
                    CounterKind.CloseSession, $"{_text.CloseDrawerTitle} · {_context?.TerminalName}",
                    _text.ApproveCloseSummary(ShownWire(counted, _context?.Currency), note.Length > 0));
                return;

            default:
                _customerPanel = now with { Sending = false, Refused = answer is null ? _text.DrawerOffline : RefusalText.Say(_text, answer.Refusal, answer.Reason) };
                break;
        }

        Render();
    }

    /// <summary>"Pointer" (B10, D-102): the person and their PIN to the server, which checks it with sign-in's lockout, then clocks them in or out.</summary>
    private async Task ClockAsync(CustomerPanelState panel, string token)
    {
        if (panel.Chosen is not { } staffId || panel.Pin.Length < 4)
        {
            return;
        }

        var pin = panel.Pin;
        _customerPanel = panel with { Sending = true, Pin = string.Empty, Refused = null };
        Render();
        var answer = await _server.ClockAsync(new ClockRequest(_till.TerminalId ?? string.Empty, staffId, pin), token);
        if (_customerPanel is not { Kind: CustomerPanelKind.Clock } now)
        {
            return;
        }

        _customerPanel = answer?.Outcome switch
        {
            ClockOutcomes.ClockedIn or ClockOutcomes.ClockedOut => now with { Kind = CustomerPanelKind.ClockDone, Sending = false, Clocked = answer },
            SignInOutcomes.WrongPin => now with { Sending = false, Refused = _text.WrongManagerPin(answer.AttemptsLeft ?? 0) },
            SignInOutcomes.Locked => now with
            {
                Sending = false,
                Refused = _text.ManagerLocked(DisplayFigures.Clock(TimeZoneInfo.ConvertTime(answer.LockedUntil ?? _clock.GetUtcNow(), TimeZoneInfo.Local))),
            },
            SignInOutcomes.NoPin => now with { Sending = false, Refused = _text.ManagerHasNoPin },
            _ => now with { Sending = false, Refused = _text.CarnetOffline },
        };
        Render();
    }

    private string Shown(Domain.Values.Money? amount) =>
        amount is { } money ? DisplayFigures.AmountWithCurrency(money, _text) : "?";

    /// <summary>
    /// A figure off the wire ("1714.00") as the till shows every other: "1 714,00 DA". A dash for none,
    /// and the text as it came when it cannot be read: never a guess.
    /// </summary>
    private string ShownWire(string? amount, string? currency)
    {
        if (amount is null)
        {
            return "—";
        }

        try
        {
            return DisplayFigures.AmountWithCurrency(WireFigures.Money(amount, currency ?? "DZD"), _text);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return amount;
        }
    }

    // =================================================================== B9: a refund linked to its sale

    /// <summary>
    /// "Rembourser" on a past ticket (B9, D-098): the ticket stays in the ticket view, its lines are
    /// touched to bring them back, and the panel in the rail asks the shop's return reasons.
    /// </summary>
    private void OpenRefund()
    {
        if (_session.Viewing is not { } ticket || !RefundScreen.Refundable(ticket) || _payment is not null || _refund is not null)
        {
            return;
        }

        _refund = RefundState.For(ticket);
        _refundOf = ticket.TransactionId;
        _pin = string.Empty;
        _discount = new DiscountState(null, DiscountForms.Amount, string.Empty, null, false, null, Kind: CounterKind.Refund);
        _input.Text = string.Empty;
        Render();
        _input.Focus();
        _ = LoadReasonsAsync();
    }

    /// <summary>
    /// Continuer: lines, a reason and its note, then the server says what the refund comes to. The
    /// till never works it out (D-098): the floating panel shows the server's figures, or the rail says
    /// why there are none.
    /// </summary>
    private async Task QuoteRefundAsync()
    {
        if (_discount is not { Kind: CounterKind.Refund, Authorising: false } open || _refund is not { } refund
            || _session.Viewing is not { } ticket || _session.SignedIn is not { } person)
        {
            return;
        }

        var problem = refund.Chosen == 0 ? DiscountProblem.NothingChosen
            : open.ReasonCode is null ? DiscountProblem.NoReason
            : (DiscountProblem?)null;
        if (problem is not null)
        {
            _discount = open with { Problem = problem };
            Render();
            return;
        }

        // A reason that asks for a note is not given without one (F-28): the field takes the note.
        if (open.Reasons?.ReasonCodes.FirstOrDefault(r => r.Code == open.ReasonCode) is { RequiresNote: true })
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

        // Quoted in cash, unless store credit was chosen and a customer attached for it (B9a).
        var asked = refund.Customer is not null && refund.To == RefundDestinations.StoreCredit ? refund : refund with { To = RefundDestinations.Cash };
        var answer = await _server.RefundAsync(
            RefundScreen.Request(_till.TerminalId ?? string.Empty, ticket, asked, open.ReasonCode!, NoteOf(open), null, quote: true) with
            {
                TicketAuthorisation = TicketApprovalFor(ticket),
            },
            person.Token);
        if (_refund is null || _discount is not { Kind: CounterKind.Refund } now)
        {
            return;
        }

        switch (answer?.Outcome)
        {
            case RefundOutcomes.Quoted:
                _refund = _refund with { Quote = answer, To = asked.To, Refused = null };
                break;
            case RefundOutcomes.Refused or RefundOutcomes.NotAllowed:
                _refund = _refund with { Refused = RefusalText.Say(_text, answer.Refusal, answer.Reason, answer.Currency ?? "DZD") };
                break;
            default:
                _discount = now with { Problem = DiscountProblem.Offline };
                break;
        }

        Render();
    }

    /// <summary>
    /// "Rembourser" in the floating panel: the refund to the server, which writes it before anything
    /// is handed over (D-098). Written: the refund's own ticket opens in the ticket view. A manager
    /// needed: the PIN step, then this again with the authorisation. Refused or no answer: the panel
    /// stays and says why; nothing was refunded.
    /// </summary>
    private async Task SendRefundAsync(string? authorisation)
    {
        if (_refund is not { Quote: not null, Sending: false } refund || _discount is not { Kind: CounterKind.Refund, ReasonCode: { } code } open
            || _session.Viewing is not { } ticket || _session.SignedIn is not { } person)
        {
            return;
        }

        // Store credit is a named customer's (D-098): nobody on the ticket, nobody attached, nothing sent.
        if (refund.To == RefundDestinations.StoreCredit && refund.Quote is { CustomerOnTicket: false } && refund.Customer is null)
        {
            return;
        }

        _refund = refund with { Sending = true, Refused = null };
        Render();
        var answer = await _server.RefundAsync(
            RefundScreen.Request(_till.TerminalId ?? string.Empty, ticket, refund, code, NoteOf(open), authorisation, quote: false) with
            {
                TicketAuthorisation = TicketApprovalFor(ticket),
            },
            person.Token);
        if (_refund is null || _discount is not { Kind: CounterKind.Refund } now)
        {
            return;
        }

        switch (answer?.Outcome)
        {
            case RefundOutcomes.Refunded:
                EndRefund();
                _input.Text = string.Empty;
                await OpenTicketAsync(answer.InvoiceNumber ?? answer.TransactionId ?? ticket.TransactionId);

                // Cash to hand back, said again over the refund's ticket: the panel that showed it is gone.
                if (answer.RefundTo != RefundDestinations.StoreCredit && answer.CashOut is { } handBack)
                {
                    _session.Tell(TillNoticeKind.RefundPaid, answer.InvoiceNumber ?? "-", handBack);
                }

                // Store credit issued: its balance, shown once, over the refund's ticket (B9a).
                if (answer.RefundTo == RefundDestinations.StoreCredit)
                {
                    _customerPanel = new CustomerPanelState(
                        CustomerPanelKind.CreditIssued, Issued: answer, IssuedTo: refund.Customer?.ShortName ?? _text.ClientTitle, At: _clock.GetUtcNow());
                    Render();
                }

                return;

            case RefundOutcomes.PinRequired:
                var staff = await _server.ApproversAsync(Capabilities.Refund);
                _pin = string.Empty;
                _refund = _refund with { Sending = false };
                _discount = now with { Authorising = true, Staff = staff, ManagerId = null, Problem = staff is null ? DiscountProblem.Offline : null };
                break;

            case RefundOutcomes.Refused or RefundOutcomes.NotAllowed:
                _refund = _refund with { Sending = false, Refused = RefusalText.Say(_text, answer.Refusal, answer.Reason, answer.Currency ?? "DZD") };
                break;

            default:
                _refund = _refund with { Sending = false, Refused = _text.RefundOffline };
                break;
        }

        Render();
    }

    /// <summary>The authorisation that opened this ticket, when a manager's PIN did (D-109); null for a ticket the seller may open alone.</summary>
    private string? TicketApprovalFor(PastTicketDetail ticket) =>
        _ticketApproval is { } approval && approval.TransactionId == ticket.TransactionId ? approval.Authorisation : null;

    /// <summary>The floating panel closed: back to the lines and the reason, nothing refunded.</summary>
    private void CloseRefundPanel()
    {
        if (_refund is { Sending: false } refund)
        {
            _refund = refund with { Quote = null, Refused = null };
            Render();
            _input.Focus();
        }
    }

    /// <summary>The refund and its panel gone; the past ticket, if still open, read-only again.</summary>
    private void EndRefund()
    {
        _refund = null;
        _refundOf = null;
        if (_discount is { Kind: CounterKind.Refund })
        {
            _discount = null;
            _pin = string.Empty;
        }
    }

    private static string? NoteOf(DiscountState open) => open.Note.Trim() is { Length: > 0 } note ? note : null;

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
                // B8 (D-097): a reason, recorded on the server, then the drafts as before.
                OpenCancel();
                break;

            case Operation.Drafts:
                _draftsOpen = _session.Drafts.Count > 0;
                _tickets = null;
                Render();
                break;

            case Operation.TicketDiscount:
                OpenDiscount(null);
                break;

            case Operation.Refund:
                OpenRefund();
                break;

            case Operation.PettyCash:
                // B10 (D-102): cash in or out with no sale, its reason and its amount.
                _moreOpen = false;
                OpenCustomerPanel(new CustomerPanelState(CustomerPanelKind.PettyCash));
                _ = LoadPettyReasonsAsync();
                break;

            case Operation.More:
                _moreOpen = !_moreOpen;
                Render();
                break;

            case Operation.CloseDrawer:
                // C1 (D-111): the drawer counted, then the session closed.
                _moreOpen = false;
                _ = OpenCloseAsync();
                break;

            case Operation.Clock:
                _moreOpen = false;
                OpenCustomerPanel(new CustomerPanelState(CustomerPanelKind.Clock));
                _ = LoadClockStaffAsync();
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
        if (_selected is not { } id || _session.Paid is not null)
        {
            return;
        }

        _selected = null;

        // Once "Encaisser" was opened on the ticket, a strike is a cancel one line at a time (D-106):
        // the server says whether the seller may alone, and asks a manager's PIN when not.
        if (_session.Cart.PaymentOpenedAt is not null
            && _session.Cart.ActiveLines.FirstOrDefault(line => line.LineId == id) is { } line)
        {
            _ = AuthoriseCustomerAsync(
                CounterKind.StrikeLine, _text.StrikeApprovalTitle, _text.StrikeApprovalSummary(line.ProductName), id);
            return;
        }

        _session.Remove(id);
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

                // C1 (D-111): with no session open, the float is counted before anything is sold.
                await LoadDrawerAsync();
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

            // The drawer is asked again for whoever is next: what they may see of it is theirs (C1).
            _customerPanel = null;
            _drawer = null;
            _frozenCount = null;
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

    // ============================================================ B6: the payment

    /// <summary>
    /// Encaisser (F12, or the bar's key): the payment panel floats over the ticket, which is frozen
    /// under it (D-094). Only when Encaisser is available, as before: a ticket with lines, nothing
    /// unconfirmed, no past ticket open, no weight awaited, no discount being given.
    /// </summary>
    private void OpenPayment()
    {
        if (_payment is not null || _screen?.Bottom.Primary is not { Enabled: true } || _session.Paid is not null || _session.Viewing is not null)
        {
            return;
        }

        _search = null;
        _selected = null;
        _payment = PaymentState.Open with { TabAsPart = _context?.TabAsPart ?? true };

        // Kept on the ticket (B8): if it is cancelled after this, a cashier needs a manager.
        _session.MarkPaymentOpened();
        Render();

        // The attached customer's tab, read when it may be used (B7): the Carnet key needs it.
        if (_session.Cart.Customer is not null)
        {
            _ = LoadTabAsync();
        }
    }

    private void ClosePayment()
    {
        if (_payment is { Sending: true })
        {
            return;
        }

        _payment = null;
        Render();
        _input.Focus();
    }

    private void UpdatePayment(Func<PaymentState, PaymentState> change)
    {
        if (_payment is { Sending: false } open)
        {
            _payment = change(open);
            Render();
        }
    }

    /// <summary>
    /// The key that goes on: "Ajouter la part" while a card or BaridiMob part is typed, "Valider"
    /// with cash chosen, which sends the sale with its parts. A refusal keeps the panel and its
    /// parts; a sale with no usable answer closes it, and the rail's card takes over (D-085).
    /// </summary>
    private void PaymentPrimary()
    {
        if (_payment is not { Sending: false } open || _session.Cart.Total is not { } total)
        {
            return;
        }

        // The tab (B7): the server's rule asked first; the owner's PIN past the limit; the whole ticket
        // at once when the tenant says the tab is never a part.
        if (open.Method == Domain.Enums.PaymentMethod.OnAccount)
        {
            if (_customerTab is not { } tab)
            {
                return;
            }

            var charge = open.TabAsPart ? PaymentScreen.AmountOf(open, PaymentScreen.RestOf(total, open.Parts)) : total;
            var check = charge is { IsPositive: true } c ? CustomerScreen.Check(tab, c, _clock.GetUtcNow()) : null;
            if (check?.Verdict == Domain.Customers.TabVerdict.AboveLimit && open.TabOverride is null)
            {
                _ = AuthoriseCustomerAsync(CounterKind.TabOverride,
                    _text.ApproveOverrideTitle(_session.Cart.Customer?.ShortName ?? string.Empty),
                    $"{_text.TabAmountTitle} {Shown(charge)} · {_text.TabAvailable} {ShownWire(tab.Available, tab.Currency)}");
                return;
            }

            if (check?.Verdict is not (Domain.Customers.TabVerdict.Accepted or Domain.Customers.TabVerdict.AboveLimit))
            {
                return;
            }

            if (open.TabAsPart)
            {
                _payment = PaymentScreen.AddPart(open, total);
                Render();
                return;
            }

            if (open.Parts.Count > 0)
            {
                return;
            }

            _payment = open with { Sending = true, Refused = null };
            Render();
            Pay([new TenderEntry(Domain.Enums.PaymentMethod.OnAccount, total, null).ToWire()]);
            return;
        }

        // Store credit (B9b): the amount the panel shows, never more than the credit available.
        if (open.Method == Domain.Enums.PaymentMethod.StoreCredit)
        {
            if (_customerTab?.CreditAvailable is { } credit)
            {
                _payment = PaymentScreen.AddCredit(open, total, WireFigures.Money(credit, _customerTab.Currency ?? total.Currency.Code));
                Render();
            }

            return;
        }

        if (open.AddingPart)
        {
            _payment = PaymentScreen.AddPart(open, total);
            Render();
            return;
        }

        _payment = open with { Sending = true, Refused = null };
        Render();
        Pay([.. open.Parts.Select(part => part.ToWire())]);
    }

    private async void Pay(IReadOnlyList<TenderRequest> tenders)
    {
        // async void, as an event handler must be: so nothing may escape it.
        try
        {
            // Recorded against the attached customer, with the owner's word past the tab's limit (B7).
            await _session.PayAsync(tenders, _session.Cart.Customer?.CustomerId, _payment?.TabOverride);
            if (_session.Paid is not null)
            {
                _customerTab = null;
            }
            _payment = _session.Paid is null && _session.Unconfirmed is null && _session.SignedIn is not null && _payment is { } kept
                ? kept with { Sending = false, Refused = _session.Notice is { Kind: TillNoticeKind.SaleRefused } refused ? refused.Detail : null }
                : null;
            Render();
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
            _payment = null;
            _session.ReportHealth(reachable: false);
            Render();
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
                await LoadDrawerAsync();
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
        _customerTimer?.Dispose();
    }
}
