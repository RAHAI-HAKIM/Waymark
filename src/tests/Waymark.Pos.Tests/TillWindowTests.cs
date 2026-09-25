using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Waymark.Contracts.Pos;
using Waymark.Contracts.Recommendations;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;
using Waymark.Pos.Server;
using Waymark.Pos.Ui;

namespace Waymark.Pos.Tests;

/// <summary>
/// The till's window, driven headlessly by Avalonia against a fake StoreServer (D-086, was
/// <c>tools/till-harness</c>). <c>TillScreenTests</c> argue what the till shows; these drive the
/// window that draws it, which is where every defect of the block A review was (D-084): a ticket
/// that jumped back to its first line, a focus that left the search field, colours taken from
/// Windows, a list never asked for again, keys that took a touch only on their letters.
///
/// <para>
/// <b>What this does not cover:</b> a real Win32 window. Focus, input and drawing go through
/// Avalonia's headless platform, so Windows' own accent colour, touch gestures and DPI scaling are
/// looked at on the till.
/// </para>
/// </summary>
[Collection(TillWindowGroup.Name)]
public sealed class TillWindowTests
{
    // ================================================================ a long ticket

    [Fact]
    public Task The_line_just_scanned_is_in_view() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        till.ScanMany(30);

        var rows = till.Rows();
        Assert.Equal(30, rows.Count);
        Assert.True(till.InView(rows[^1]), till.Describe());
    });

    [Fact]
    public Task A_redraw_that_is_not_a_scan_keeps_the_cashiers_place() => Headless.Run(() =>
    {
        // Every redraw — the 15 s clock, a health check — used to put a long ticket back at its first line.
        using var till = Till.SignedIn();
        till.ScanMany(30);
        till.Scroll.Offset = new Vector(0, 300);
        till.Pump();
        var before = till.Scroll.Offset.Y;

        till.Session.ReportHealth(reachable: false);
        till.Pump();
        till.Session.ReportHealth(reachable: true);
        till.Pump();

        Assert.True(before > 0);
        Assert.Equal(before, till.Scroll.Offset.Y, 0.5);
    });

    [Fact]
    public Task A_line_taken_out_keeps_the_cashiers_place() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        till.ScanMany(30);
        till.Scroll.Offset = new Vector(0, 300);
        till.Pump();

        var high = till.Rows().First(till.InView);
        till.Tap(high, new Point(200, 20));
        var touched = till.Scroll.Offset.Y;
        till.Key(Key.F8, PhysicalKey.F8);

        Assert.Equal(1, till.Rows().Count(row => row.Tag is LineRow { Struck: true }));
        Assert.True(touched > 0);
        Assert.Equal(touched, till.Scroll.Offset.Y, 0.5);
    });

    [Fact]
    public Task A_scan_of_a_line_out_of_view_brings_it_into_view() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        till.ScanMany(30);
        till.Scroll.Offset = new Vector(0, 0);
        till.Pump();

        till.Scan(Till.Code(29));

        Assert.True(till.InView(till.Rows()[^1]), till.Describe());
    });

    [Fact]
    public Task Touching_a_line_opens_its_actions_in_view_and_leaves_the_search_field_focused() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        till.ScanMany(30);

        var lowest = till.Rows().Last(till.InView);
        till.Tap(lowest, new Point(200, 20));

        var actions = ((StackPanel)till.Scroll.Content!).Children.FirstOrDefault(child => child.Tag is LineActions);
        Assert.NotNull(actions);
        Assert.True(till.InView(actions), till.Describe());
        Assert.Same(till.SearchField, till.Focused);
    });

    // ================================================================ the scanner's focus

    [Fact]
    public Task A_code_typed_after_touching_a_line_is_sent_by_entree() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        till.ScanMany(3);
        till.Tap(till.Rows()[0], new Point(200, 20));

        till.TypeAndEnter("4242");

        Assert.Equal("4242", till.Server.Lookups[^1]);
    });

    [Fact]
    public Task A_touch_on_empty_space_leaves_the_code_typed_next_to_entree() => Headless.Run(() =>
    {
        // Nothing is redrawn, so nothing gives the focus back: it must not have moved.
        using var till = Till.SignedIn();
        till.ScanMany(3);
        till.Window.MouseDown(new Point(1100, 320), MouseButton.Left);
        till.Window.MouseUp(new Point(1100, 320), MouseButton.Left);
        till.Pump();

        till.TypeAndEnter("3131");

        Assert.Equal("3131", till.Server.Lookups[^1]);
    });

    [Fact]
    public Task A_key_takes_a_touch_across_its_face_and_leaves_the_focus_in_the_search_field() => Headless.Run(() =>
    {
        // The staff chip has no ground of its own. Touched with lines on the ticket, the session
        // refuses the switch; the touch must land, and Entrée must still reach the search field.
        using var till = Till.SignedIn();
        till.ScanMany(2);
        var chip = till.Window.GetVisualDescendants().OfType<TillKey>()
            .First(key => key.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Nabil B."));

        till.Tap(chip, new Point(20, 20));
        Assert.Equal(TillNoticeKind.SwitchRefused, till.Session.Notice?.Kind);

        till.TypeAndEnter("5151");
        Assert.Equal("5151", till.Server.Lookups[^1]);
    });

    [Fact]
    public Task The_search_field_has_no_clipboard_menu() => Headless.Run(() =>
    {
        // F-26: Fluent's menu is in English (Cut, Copy, Paste) and the till has no use for a
        // clipboard. A right click or a long press opens nothing.
        using var till = Till.SignedIn();

        Assert.Null(till.SearchField.ContextFlyout);
        Assert.Null(till.SearchField.ContextMenu);
    });

    // ================================================================ colours

    [Fact]
    public Task Fluents_accent_is_the_palettes_action_colour_in_both_themes() => Headless.Run(() =>
    {
        // It was Windows' accent colour: red on a till set to red.
        using var till = Till.SignedIn();

        Assert.Equal(Color.Parse(TillPalette.Light.Action), till.Window.FindResource(ThemeVariant.Light, "SystemAccentColor"));
        Assert.Equal(Color.Parse(TillPalette.Dark.Action), till.Window.FindResource(ThemeVariant.Dark, "SystemAccentColor"));
    });

    [Fact]
    public Task Selected_text_in_the_search_field_is_the_palettes_action_pair() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();

        Assert.Equal(Color.Parse(TillPalette.Light.Action), Assert.IsAssignableFrom<ISolidColorBrush>(till.SearchField.SelectionBrush).Color);
        Assert.Equal(Color.Parse(TillPalette.Light.ActionLabel), Assert.IsAssignableFrom<ISolidColorBrush>(till.SearchField.SelectionForegroundBrush).Color);
    });

    // ================================================================ an unconfirmed sale (D-085)

    [Fact]
    public Task While_a_sale_is_unconfirmed_f12_sends_nothing_until_the_cashier_has_checked() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        till.Server.SaleAnswer = new SaleAnswer.Unknown("timeout");
        till.ScanMany(1);
        till.Key(Key.F12, PhysicalKey.F12);
        Assert.Equal(1, till.Server.Sales);

        till.Key(Key.Escape, PhysicalKey.Escape);
        till.Key(Key.F12, PhysicalKey.F12);
        Assert.Equal(1, till.Server.Sales);

        var checkedKey = till.Window.GetVisualDescendants().OfType<TillKey>()
            .First(key => key.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == TillText.French.AcknowledgeUnconfirmed));
        till.Tap(checkedKey, new Point(20, 20));
        till.Key(Key.F12, PhysicalKey.F12);

        Assert.Equal(2, till.Server.Sales);
    });

    // ================================================================ a till started before its server

    [Fact]
    public Task The_list_of_who_may_sign_in_is_asked_for_once_the_server_answers() => Headless.Run(() =>
    {
        // A till started before StoreServer showed nobody, and "HORS LIGNE", until restarted.
        var server = new FakeStoreServer { Up = false };
        using var till = new Till(server, signedIn: false);

        server.Up = true;
        // The health timer ticks every ten seconds; this calls its handler rather than wait for it.
        var health = typeof(TillWindow).GetMethod("CheckHealthAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("TillWindow.CheckHealthAsync was renamed: update this test.");
        var check = (Task)health.Invoke(till.Window, null)!;
        while (!check.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
        }

        till.Pump();

        var texts = till.Window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList();
        Assert.Contains("Nabil B.", texts);
        Assert.DoesNotContain(TillText.French.Offline, texts);
    });

    // ================================================================ helpers

    /// <summary>A shown till window with its session and fake server, closed at the end of the test.</summary>
    private sealed class Till : IDisposable
    {
        public Till(FakeStoreServer server, bool signedIn)
        {
            Server = server;
            var identity = new TillIdentity("till-1");
            Session = new TillSession(server, server, identity, TimeProvider.System);
            if (signedIn)
            {
                Session.SignIn(new SignedInStaff("nabil", "token"));
            }

            Window = new TillWindow(Session, server, identity, TillLanguage.French, TillThemeKind.Light, TimeProvider.System);
            Window.Show();
            Pump();
        }

        public static Till SignedIn() => new(new FakeStoreServer(), signedIn: true);

        public static string Code(int i) => $"61300000{i:D5}";

        public FakeStoreServer Server { get; }

        public TillSession Session { get; }

        public TillWindow Window { get; }

        public ScrollViewer Scroll => Window.GetVisualDescendants().OfType<ScrollViewer>()
            .First(scroll => scroll.TemplatedParent is not TextBox && scroll.Content is StackPanel);

        public TextBox SearchField => Window.GetVisualDescendants().OfType<TextBox>()
            .First(box => box.PlaceholderText == TillText.French.SearchPlaceholder);

        public IInputElement? Focused => Window.FocusManager?.GetFocusedElement();

        public List<Control> Rows() => [.. ((StackPanel)Scroll.Content!).Children.Where(child => child.Tag is LineRow)];

        public bool InView(Control control) =>
            control.TranslatePoint(new Point(0, 0), Scroll) is { } top
            && top.Y >= -0.5
            && top.Y + control.Bounds.Height <= Scroll.Viewport.Height + 0.5;

        public string Describe() =>
            $"offset {Scroll.Offset.Y:0}, viewport {Scroll.Viewport.Height:0}, extent {Scroll.Extent.Height:0}";

        public void Pump()
        {
            for (var i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Window.UpdateLayout();
            }
        }

        public void Scan(string code)
        {
            // The fake answers at once, so the lookup is finished when this returns.
            Assert.True(Session.SubmitAsync(code).IsCompleted, "The fake server answers synchronously.");
            Pump();
        }

        public void ScanMany(int count)
        {
            for (var i = 0; i < count; i++)
            {
                Scan(Code(i));
            }
        }

        public void Tap(Control control, Point within)
        {
            var point = control.TranslatePoint(within, Window) ?? throw new InvalidOperationException("Not in the window.");
            Window.MouseDown(point, MouseButton.Left);
            Window.MouseUp(point, MouseButton.Left);
            Pump();
        }

        public void Key(Key key, PhysicalKey physical)
        {
            Window.KeyPress(key, RawInputModifiers.None, physical, null);
            Window.KeyRelease(key, RawInputModifiers.None, physical, null);
            Pump();
        }

        public void TypeAndEnter(string text)
        {
            Window.KeyTextInput(text);
            Window.KeyPress(Avalonia.Input.Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Window.KeyRelease(Avalonia.Input.Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            Pump();
        }

        public void Dispose() => Window.Close();
    }

    /// <summary>StoreServer as the till sees it, answering at once. <see cref="Up"/> is whether it answers at all.</summary>
    private sealed class FakeStoreServer : IProductSource, IStoreSales, ITillServer
    {
        public bool Up { get; set; } = true;

        public List<string> Lookups { get; } = [];

        public int Sales { get; private set; }

        public SaleAnswer SaleAnswer { get; set; } = new SaleAnswer.Completed(
            new SaleOutcome(SaleOutcomes.Completed, "sale", "0142", "143.00", "22.83", "145.00", "DZD", null));

        public Task<LookupAnswer> LookupAsync(string barcode, CancellationToken cancellationToken = default)
        {
            Lookups.Add(barcode);
            if (!Up)
            {
                return Task.FromResult<LookupAnswer>(new LookupAnswer.ServerUnavailable("down"));
            }

            var product = new ProductForSale(
                "v-" + barcode, "p-" + barcode, "Article " + barcode[^3..], "1 L", "pc", 0, 1900, TvaRateSource.FromCategory,
                "143.00", "DZD", false, "100");
            return Task.FromResult<LookupAnswer>(new LookupAnswer.Answered(new ProductLookup(ProductLookupOutcome.Found, barcode, product, null)));
        }

        public Task<SaleAnswer> CompleteSaleAsync(SaleRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            Sales++;
            return Task.FromResult(SaleAnswer);
        }

        public Task<TillStaff?> StaffAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Up ? new TillStaff([new TillStaffMember("nabil", "Nabil B.", "Caissier", "أمين الصندوق", true)]) : null);

        public Task<SignInAnswer?> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignInAnswer?>(Up ? new SignInAnswer(SignInOutcomes.SignedIn, "token", null, null) : null);

        public Task SignOutAsync(string sessionToken, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<TillContext?> ContextAsync(string terminalId, string? staffId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Up
                ? new TillContext(TillContextOutcome.Found, "El Bahdja", "Caisse 1", "DZD", staffId is null ? null : "Nabil B.", "Caissier", "أمين الصندوق")
                : null);

        public Task<BoardAnswer?> BoardAsync(string staffId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Up ? new BoardAnswer(BoardOutcome.Answered, "Nabil B.", "cashier", 0, []) : null);

        public Task<DecisionAnswer?> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<DecisionAnswer?>(null);

        public Task<bool> HealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(Up);
    }
}

/// <summary>
/// The window tests share one headless Avalonia session for the whole assembly (Avalonia allows one
/// per process), and run one at a time in this collection: they all use its UI thread.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TillWindowGroup
{
    public const string Name = "The till's window";
}

/// <summary>Runs a test body on the headless UI thread.</summary>
internal static class Headless
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.StartNew(typeof(TillTestApp), AvaloniaTestIsolationLevel.PerAssembly);

    public static Task Run(Action body) => Session.Dispatch(body, CancellationToken.None);
}

/// <summary>The till's own <see cref="App"/>, on Avalonia's headless platform, drawn with Skia.</summary>
internal static class TillTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
