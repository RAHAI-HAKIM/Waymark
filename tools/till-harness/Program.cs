// The till's window, driven headlessly (block A review, 24/09/2026). TillScreenTests argue what
// the till shows; nothing else drove the window that draws it, and every defect the block A
// review found in the till was the window's: a ticket that jumped back to its first line, a focus
// that left the search field, colours taken from Windows, a list never asked for again, keys that
// took a touch only on their letters. Each check below failed before its fix. README.md says how
// to run it and what it does not cover.
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
using Waymark.Pos;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;
using Waymark.Pos.Server;
using Waymark.Pos.Ui;

var shots = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "waymark-till-harness");
Directory.CreateDirectory(shots);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var failures = 0;
void Check(string name, bool pass, string detail)
{
    failures += pass ? 0 : 1;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {name} — {detail}");
}

// ------------------------------------------------------------------ a signed-in till, a long ticket
{
    var server = new FakeStoreServer();
    var till = new TillIdentity("till-1");
    var session = new TillSession(server, server, till, TimeProvider.System);
    session.SignIn(new SignedInStaff("nabil", "token"));
    var window = new TillWindow(session, server, till, TillLanguage.French, TillThemeKind.Light, TimeProvider.System);
    window.Show();
    Pump(window);

    for (var i = 0; i < 30; i++)
    {
        Scan(session, $"61300000{i:D5}");
        Pump(window);
    }

    var scroll = CartScroll(window);
    var rows = Rows(window);
    Check("A. the line just scanned is in view", rows.Count == 30 && InView(scroll, rows[^1]), $"{rows.Count} lines; {Describe(scroll)}");
    Shot(window, "a-thirty-lines");

    // The cashier scrolls up to read the ticket; then the server blinks and the clock ticks.
    scroll.Offset = new Vector(0, 300);
    Pump(window);
    var before = CartScroll(window).Offset.Y;
    session.ReportHealth(reachable: false);
    Pump(window);
    session.ReportHealth(reachable: true);
    Pump(window);
    var after = CartScroll(window).Offset.Y;
    Check("B. a redraw that is not a scan keeps the cashier's place", before > 0 && Math.Abs(after - before) < 1, $"offset {before:0} before, {after:0} after");

    // A line near the top of the view is touched and taken out (F8): the ticket is redrawn, and
    // nothing is followed, so nothing may move.
    scroll = CartScroll(window);
    var high = Rows(window).First(row => InView(scroll, row));
    Tap(window, high, new Point(200, 20));
    var touched = CartScroll(window).Offset.Y;
    window.KeyPress(Key.F8, RawInputModifiers.None, PhysicalKey.F8, null);
    window.KeyRelease(Key.F8, RawInputModifiers.None, PhysicalKey.F8, null);
    Pump(window);
    var struck = Rows(window).Count(row => row.Tag is LineRow { Struck: true });
    var removed = CartScroll(window).Offset.Y;
    Check("C. a line taken out keeps the cashier's place", struck == 1 && touched > 0 && Math.Abs(removed - touched) < 1,
        $"struck lines {struck}; offset {touched:0} before, {removed:0} after");

    // A second unit of a product whose line is out of sight.
    CartScroll(window).Offset = new Vector(0, 0);
    Pump(window);
    Scan(session, "6130000000029");
    Pump(window);
    Check("D. a scan of a line out of view brings it into view", InView(CartScroll(window), Rows(window)[^1]), Describe(CartScroll(window)));

    // The lowest line in view is touched: its actions open beneath it, in sight.
    scroll = CartScroll(window);
    var lowest = Rows(window).Last(row => InView(scroll, row));
    Tap(window, lowest, new Point(200, 20));
    scroll = CartScroll(window);
    var actions = ((StackPanel)scroll.Content!).Children.FirstOrDefault(child => child.Tag is LineActions);
    Check("E. touching a line opens its actions in view", actions is not null && InView(scroll, actions), actions is null ? "no actions" : Describe(scroll));
    Check("F. touching a line leaves the search field focused", ReferenceEquals(Focused(window), SearchField(window)), $"focused: {Focused(window)?.GetType().Name ?? "nothing"}");
    Shot(window, "e-line-touched");

    var looked = server.Lookups.Count;
    TypeAndEnter(window, "4242");
    Check("G. a code typed after touching a line is sent by Entrée", server.Lookups.Count == looked + 1 && server.Lookups[^1] == "4242",
        $"lookups {looked} → {server.Lookups.Count}");

    // A touch on empty space redraws nothing, so nothing gives the focus back: it must not move.
    window.MouseDown(new Point(1100, 320), MouseButton.Left);
    window.MouseUp(new Point(1100, 320), MouseButton.Left);
    Pump(window);
    looked = server.Lookups.Count;
    TypeAndEnter(window, "3131");
    Check("H. a touch on empty space leaves the code typed next to Entrée", server.Lookups.Count == looked + 1 && server.Lookups[^1] == "3131",
        $"lookups {looked} → {server.Lookups.Count}; focused: {Focused(window)?.GetType().Name ?? "nothing"}");

    // The staff chip, touched with lines on the ticket: the session refuses the switch. The touch must
    // land (the chip has no ground of its own) and must leave the focus where the scanner needs it.
    var chip = window.GetVisualDescendants().OfType<TillKey>()
        .First(key => key.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Nabil B."));
    Tap(window, chip, new Point(20, 20));
    var refused = session.Notice?.Kind == TillNoticeKind.SwitchRefused;
    looked = server.Lookups.Count;
    TypeAndEnter(window, "5151");
    Check("I. a key takes a touch across its face and leaves the focus in the search field",
        refused && server.Lookups.Count == looked + 1 && server.Lookups[^1] == "5151",
        $"switch refused: {refused}; lookups {looked} → {server.Lookups.Count}; focused: {Focused(window)?.GetType().Name ?? "nothing"}");

    // Colours: none comes from the machine the till runs on.
    var light = window.FindResource(ThemeVariant.Light, "SystemAccentColor");
    var dark = window.FindResource(ThemeVariant.Dark, "SystemAccentColor");
    Check("J. Fluent's accent is the palette's action colour in both themes, never Windows'",
        light is Color l && l == Color.Parse(TillPalette.Light.Action) && dark is Color d && d == Color.Parse(TillPalette.Dark.Action),
        $"light {light}, dark {dark}");

    var input = SearchField(window);
    Check("K. selected text in the search field is the palette's action pair",
        input.SelectionBrush is ISolidColorBrush s && s.Color == Color.Parse(TillPalette.Light.Action)
        && input.SelectionForegroundBrush is ISolidColorBrush f && f.Color == Color.Parse(TillPalette.Light.ActionLabel),
        $"selection {(input.SelectionBrush as ISolidColorBrush)?.Color}, selected text {(input.SelectionForegroundBrush as ISolidColorBrush)?.Color}");
    input.Text = "6130985042117";
    input.SelectionStart = 0;
    input.SelectionEnd = input.Text.Length;
    Pump(window);
    Shot(window, "k-selected-text");
    window.Close();
}

// ------------------------------------------------------------------ a till started before its server
{
    var server = new FakeStoreServer { Up = false };
    var till = new TillIdentity("till-1");
    var session = new TillSession(server, server, till, TimeProvider.System);
    var window = new TillWindow(session, server, till, TillLanguage.French, TillThemeKind.Light, TimeProvider.System);
    window.Show();
    Pump(window);
    Shot(window, "l-server-not-up");

    // The health timer ticks every ten seconds; this calls its handler rather than wait for it.
    server.Up = true;
    var health = typeof(TillWindow).GetMethod("CheckHealthAsync", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("TillWindow.CheckHealthAsync was renamed: update the harness.");
    ((Task)health.Invoke(window, null)!).GetAwaiter().GetResult();
    Pump(window);

    var listed = window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Nabil B.");
    var offline = window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == TillText.French.Offline);
    Check("L. the list of who may sign in is asked for once the server answers", listed && !offline, $"listed: {listed}, still says offline: {offline}");
    Shot(window, "l-server-up");
    window.Close();
}

Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
Console.WriteLine($"Screenshots: {shots}");
return failures == 0 ? 0 : 1;

// ------------------------------------------------------------------ helpers

static void Pump(Window window)
{
    for (var i = 0; i < 3; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
    }
}

static void Scan(TillSession session, string code)
{
    // The fake answers at once, so the lookup is finished when this returns.
    if (!session.SubmitAsync(code).IsCompleted)
    {
        throw new InvalidOperationException("The fake server answers synchronously; a pending lookup means the harness is wrong.");
    }
}

static void Tap(Window window, Control control, Point within)
{
    var point = control.TranslatePoint(within, window) ?? throw new InvalidOperationException("Not in the window.");
    window.MouseDown(point, MouseButton.Left);
    window.MouseUp(point, MouseButton.Left);
    Pump(window);
}

static void TypeAndEnter(Window window, string text)
{
    window.KeyTextInput(text);
    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
    window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
    Pump(window);
}

static ScrollViewer CartScroll(Window window) => window.GetVisualDescendants().OfType<ScrollViewer>()
    .First(scroll => scroll.TemplatedParent is not TextBox && scroll.Content is StackPanel);

static List<Control> Rows(Window window) => ((StackPanel)CartScroll(window).Content!).Children
    .Where(child => child.Tag is LineRow)
    .ToList();

static bool InView(ScrollViewer scroll, Control control) =>
    control.TranslatePoint(new Point(0, 0), scroll) is { } top
    && top.Y >= -0.5
    && top.Y + control.Bounds.Height <= scroll.Viewport.Height + 0.5;

static string Describe(ScrollViewer scroll) =>
    $"offset {scroll.Offset.Y:0}, viewport {scroll.Viewport.Height:0}, extent {scroll.Extent.Height:0}";

static IInputElement? Focused(Window window) => window.FocusManager?.GetFocusedElement();

static TextBox SearchField(Window window) => window.GetVisualDescendants().OfType<TextBox>()
    .First(box => box.PlaceholderText == TillText.French.SearchPlaceholder);

void Shot(Window window, string name)
{
    if (window.CaptureRenderedFrame() is { } frame)
    {
        using var file = File.Create(Path.Combine(shots, name + ".png"));
        frame.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}

/// <summary>StoreServer as the till sees it, answering at once. <see cref="Up"/> is whether it answers at all.</summary>
internal sealed class FakeStoreServer : IProductSource, IStoreSales, ITillServer
{
    public bool Up { get; set; } = true;

    public List<string> Lookups { get; } = [];

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

    public Task<SaleAnswer> CompleteSaleAsync(SaleRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<SaleAnswer>(new SaleAnswer.Completed(
            new SaleOutcome(SaleOutcomes.Completed, "sale", "0142", "143.00", "22.83", "145.00", "DZD", null)));

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
