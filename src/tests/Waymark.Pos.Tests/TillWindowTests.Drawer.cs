using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Waymark.Contracts.Pos;
using Waymark.Pos.Screen;
using Waymark.Pos.Ui;

namespace Waymark.Pos.Tests;

/// <summary>
/// C1 (D-111): the drawer opened with a counted float and closed with a count, at the till. The silent
/// failures: a till that sells with its drawer shut; a blind count shown what the drawer should hold,
/// or let be typed again once the server said it was off; a close sent with nobody's authorisation
/// where the shop asks for one; a ticket on hold carried into the next session.
/// </summary>
public sealed partial class TillWindowTests
{
    private static Till ShutTill(Action<FakeStoreServer>? setup = null) => Till.SignedIn(server =>
    {
        server.DrawerAnswers = true;
        setup?.Invoke(server);
    });

    private static Till OpenTill(Action<FakeStoreServer>? setup = null) => ShutTill(server =>
    {
        server.DrawerIsOpen = true;
        setup?.Invoke(server);
    });

    private static void StartClose(Till till)
    {
        till.Tap(Operation(till, Screen.Operation.More), new Point(20, 20));
        till.Tap(Operation(till, Screen.Operation.CloseDrawer), new Point(20, 20));
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.CloseDrawerTitle);
    }

    /// <summary>Opt-in frames for a look at the panels: WAYMARK_TEST_SHOTS names the directory.</summary>
    private static void Shot(Till till, string name)
    {
        if (Environment.GetEnvironmentVariable("WAYMARK_TEST_SHOTS") is { Length: > 0 } directory)
        {
            till.Pump();
            using var file = File.Create(Path.Combine(directory, name + ".png"));
            till.Window.CaptureRenderedFrame()?.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }

    /// <summary>An amount as the till writes it, its own spaces and minus included.</summary>
    private static string Shown(long centimes) =>
        DisplayFigures.AmountWithCurrency(Domain.Values.Money.FromMinorUnits(centimes, Domain.Values.Currency.Dzd), TillText.French);

    // ================================================================== opening

    [Fact]
    public Task With_no_session_the_opening_panel_floats_and_cannot_be_left() => Headless.Run(() =>
    {
        using var till = ShutTill();
        till.WaitFor(() => FormOf(till) is not null);
        Shot(till, "1a-open");

        var form = FormOf(till)!;
        Assert.Equal(TillText.French.DrawerOpenTitle, form.Title);
        Assert.True(form.NoClose);
        Assert.False(form.MayPrimary); // nothing counted yet
        Assert.Contains("Z n° 11", form.Notice, StringComparison.Ordinal);

        till.Key(Key.Escape, PhysicalKey.Escape);
        Assert.Equal(TillText.French.DrawerOpenTitle, FormOf(till)?.Title);
    });

    [Fact]
    public Task A_scan_with_the_drawer_shut_adds_nothing_and_the_panel_says_why() => Headless.Run(() =>
    {
        using var till = ShutTill();
        till.WaitFor(() => FormOf(till) is not null);

        till.Window.KeyTextInput(Till.Code(1));
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Message is not null);
        Shot(till, "1b-open-scan");

        Assert.Equal(TillText.French.NoSaleWithoutDrawer, FormOf(till)!.Message!.Title);
        Assert.Empty(till.Session.Cart.Lines);
        Assert.Empty(till.Server.DrawerOpenings);
    });

    [Fact]
    public Task The_counted_float_opens_the_drawer_and_the_till_sells() => Headless.Run(() =>
    {
        using var till = ShutTill();
        till.WaitFor(() => FormOf(till) is not null);

        Keys(till, "5000");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till) is null);

        Assert.Equal(new OpenCashSessionRequest("till-1", "5000.00"), Assert.Single(till.Server.DrawerOpenings));
        till.Scan(Till.Code(1));
        Assert.Single(till.Session.Cart.Lines);
        Shot(till, "2b-bar-open");
    });

    [Fact]
    public Task An_empty_drawer_is_a_float_of_zero_typed_as_such() => Headless.Run(() =>
    {
        using var till = ShutTill();
        till.WaitFor(() => FormOf(till) is not null);

        Keys(till, "0");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till) is null);

        Assert.Equal("0.00", Assert.Single(till.Server.DrawerOpenings).OpeningFloat);
    });

    [Fact]
    public Task A_server_that_says_nothing_of_the_drawer_shows_no_panel() => Headless.Run(() =>
    {
        // Every earlier test of the window runs like this: the drawer's panel is the server's to ask for.
        using var till = Till.SignedIn();
        till.Pump();

        Assert.Null(FormOf(till));
    });

    // ================================================================== closing, the figures shown

    [Fact]
    public Task A_close_with_the_figures_shown_says_the_variance_asks_its_note_and_ends_at_the_sign_in() => Headless.Run(() =>
    {
        using var till = OpenTill(server => server.MayClose = true);
        StartClose(till);
        till.WaitFor(() => FormOf(till)?.Big is not null);
        Shot(till, "2d-count-shown");
        Assert.Equal(Shown(4_835_000), FormOf(till)!.Big);

        Keys(till, "48000");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.ConfirmCloseTitle);
        Shot(till, "2e-confirm-past");

        var confirm = FormOf(till)!;
        Assert.Equal((TillText.French.ShortPastThreshold, Tone.Critical), (confirm.Message!.Title, confirm.Message.Tone));
        Assert.Equal(Shown(-35_000), confirm.MessageFigure);
        Assert.False(confirm.MayPrimary); // 350,00 short of a 200,00 threshold: a note first

        till.Key(Key.Enter, PhysicalKey.Enter);
        Assert.Empty(till.Server.DrawerCloses);

        Keys(till, "Erreur");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.DrawerClosedTitle);
        Shot(till, "3a-closed-past");

        Assert.Equal(new CloseCashSessionRequest("till-1", "48000.00", "Erreur", null), Assert.Single(till.Server.DrawerCloses));
        var closed = FormOf(till)!;
        Assert.True(closed.NoClose);
        Assert.Equal("12", closed.Big);
        Assert.Equal(Tone.Critical, closed.Message!.Tone);

        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Session.SignedIn is null);
    });

    [Theory]
    [InlineData("48230", "ShortWithin", -12_000)]
    [InlineData("48430", "OverCount", 8_000)]
    public Task A_variance_within_the_threshold_is_a_warning_by_its_label_and_asks_no_note(string counted, string label, long variance) => Headless.Run(() =>
    {
        using var till = OpenTill(server => server.MayClose = true);
        StartClose(till);
        Keys(till, counted);
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.ConfirmCloseTitle);
        Shot(till, "2f-confirm-" + label);

        var confirm = FormOf(till)!;
        var expected = label == "ShortWithin" ? TillText.French.ShortWithin : TillText.French.OverCount;
        Assert.Equal((expected, Tone.Warning, (variance > 0 ? "+" : string.Empty) + Shown(variance)), (confirm.Message!.Title, confirm.Message.Tone, confirm.MessageFigure));
        Assert.True(confirm.MayPrimary);
    });

    [Fact]
    public Task A_count_that_matches_has_no_colour() => Headless.Run(() =>
    {
        using var till = OpenTill(server => server.MayClose = true);
        StartClose(till);
        Keys(till, "48350");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.ConfirmCloseTitle);

        Assert.Null(FormOf(till)!.Message); // there is no positive state: said in plain words
        Assert.StartsWith(TillText.French.NoVariance, FormOf(till)!.Notice, StringComparison.Ordinal);

        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.DrawerClosedTitle);
        Shot(till, "3d-closed-none");
        Assert.Null(FormOf(till)!.Message);
    });

    [Fact]
    public Task Recompter_goes_back_to_the_count_and_sends_nothing() => Headless.Run(() =>
    {
        using var till = OpenTill(server => server.MayClose = true);
        StartClose(till);
        Keys(till, "48000");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.ConfirmCloseTitle);

        till.Tap(Keyed<FormKey>(till, key => key.Id == DrawerScreen.RecountKey), new Point(10, 10));

        Assert.Equal(TillText.French.CloseDrawerTitle, FormOf(till)?.Title);
        Assert.Empty(till.Server.DrawerCloses);
    });

    [Fact]
    public Task Someone_who_may_not_close_alone_goes_through_a_managers_pin() => Headless.Run(() =>
    {
        using var till = OpenTill(); // the figures shown, a cashier
        StartClose(till);
        Keys(till, "48350");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.ConfirmCloseTitle);
        Assert.True(FormOf(till)!.Locked);

        till.Key(Key.Enter, PhysicalKey.Enter);
        SamiaAuthorises(till);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.DrawerClosedTitle);

        Assert.Equal([null, "auth-samia"], till.Server.DrawerCloses.Select(close => close.Authorisation));
    });

    // ================================================================== closing blind

    [Fact]
    public Task A_blind_count_is_shown_no_figure_frozen_at_confirm_and_asked_its_note_without_the_amount() => Headless.Run(() =>
    {
        using var till = OpenTill(server => server.Blind = true);
        StartClose(till);
        Shot(till, "2c-count-blind");
        Assert.Null(FormOf(till)!.Big); // what the drawer should hold is not on this till

        Keys(till, "48000");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.ConfirmCountTitle);
        Shot(till, "2g-confirm-blind");
        Assert.Null(FormOf(till)!.Message);
        Assert.Empty(FormOf(till)!.Tiles);

        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.ExplainCountTitle);
        Shot(till, "2h-note-blind");

        var note = FormOf(till)!;
        Assert.Equal((TillText.French.NoteAsked, Tone.Warning), (note.Message!.Title, note.Message.Tone));
        Assert.DoesNotContain("350", note.Message.Body + note.Big + note.Subtitle, StringComparison.Ordinal);
        Assert.Empty(note.Keys); // no Recompter: the count is frozen

        Keys(till, "Billets");
        till.Key(Key.Enter, PhysicalKey.Enter);
        SamiaAuthorises(till);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.DrawerClosedTitle);
        Shot(till, "3e-closed-blind");

        var closed = FormOf(till)!;
        Assert.Equal(("12", TillText.French.ZWithManager), (closed.Big, closed.Note));
        Assert.Empty(closed.Tiles);
        Assert.Null(closed.Message);
        Assert.All(till.Server.DrawerCloses, close => Assert.Equal("48000.00", close.Counted));
    });

    [Fact]
    public Task A_blind_count_once_confirmed_is_not_typed_again_by_leaving_and_coming_back() => Headless.Run(() =>
    {
        // Otherwise the note's question is an oracle: leave, count 10,00 more, ask again, and the
        // drawer's figure is found in a dozen tries.
        using var till = OpenTill(server => server.Blind = true);
        StartClose(till);
        Keys(till, "48000");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.ConfirmCountTitle);
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.ExplainCountTitle);

        till.Key(Key.Escape, PhysicalKey.Escape);
        Assert.Null(FormOf(till));
        till.Tap(Operation(till, Screen.Operation.More), new Point(20, 20));
        till.Tap(Operation(till, Screen.Operation.CloseDrawer), new Point(20, 20));
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.ConfirmCountTitle);

        var again = FormOf(till)!;
        Assert.Equal(Shown(4_800_000), again.Big);
        Assert.Empty(again.Keys); // no Recompter
        Assert.Empty(again.Fields); // and no field to type another count in
    });

    // ================================================================== tickets settled first

    [Fact]
    public Task A_ticket_with_lines_or_on_hold_is_settled_before_the_drawer_is_counted() => Headless.Run(() =>
    {
        using var till = OpenTill(server => server.MayClose = true);
        till.Scan(Till.Code(1));

        StartClose(till);

        var form = FormOf(till)!;
        Assert.Equal(TillText.French.SettleTicketsFirst, form.Message?.Body);
        Keys(till, "48350");
        Assert.False(FormOf(till)!.MayPrimary);
        till.Key(Key.Enter, PhysicalKey.Enter);
        Assert.Equal(TillText.French.CloseDrawerTitle, FormOf(till)?.Title);
    });

    private sealed partial class FakeStoreServer
    {
        /// <summary>False, as every other test of the window has it: the server says nothing of the drawer, and the till shows nothing of it.</summary>
        public bool DrawerAnswers { get; set; }

        public bool DrawerIsOpen { get; set; }

        /// <summary>The shop's blind_close; the figures still go to someone who closes alone.</summary>
        public bool Blind { get; set; }

        /// <summary>The person signed in reaches CloseSession.</summary>
        public bool MayClose { get; set; }

        public List<OpenCashSessionRequest> DrawerOpenings { get; } = [];

        public List<CloseCashSessionRequest> DrawerCloses { get; } = [];

        private static readonly DateTimeOffset OpenedAt = new(2026, 10, 5, 7, 12, 0, TimeSpan.Zero);

        private bool Shows => !Blind || MayClose;

        /// <summary>A drawer that should hold 48 350,00, with a note asked past 200,00; the till's last close was Z n° 11.</summary>
        private CashSessionState DrawerState() => new(
            CashSessionOutcomes.Ok,
            DrawerIsOpen
                ? new OpenCashSessionWire(
                    OpenedAt, "Nabil B.", "5000.00", 148,
                    Shows ? new DrawerWire("5000.00", "40120.00", "620.00", "2000.00", "1350.00", "3200.00", "0.00", "48350.00") : null)
                : null,
            new LastCloseWire(11, OpenedAt.AddHours(-10), "Karim M."),
            Blind,
            MayClose,
            Shows ? "200.00" : null);

        public Task<CashSessionState?> CashSessionAsync(string terminalId, string sessionToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(DrawerAnswers ? DrawerState() : null);

        public Task<CashSessionAnswer?> OpenCashSessionAsync(OpenCashSessionRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            DrawerOpenings.Add(request);
            DrawerIsOpen = true;
            return Task.FromResult<CashSessionAnswer?>(new CashSessionAnswer(CashSessionOutcomes.Opened, DrawerState()));
        }

        /// <summary>The server's order: the note before the PIN; the figures only to who may see them.</summary>
        public Task<CashSessionAnswer?> CloseCashSessionAsync(CloseCashSessionRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            DrawerCloses.Add(request);
            var counted = decimal.Parse(request.Counted, System.Globalization.CultureInfo.InvariantCulture);
            var variance = counted - 48_350m;
            if (Math.Abs(variance) > 200m && request.Note is null)
            {
                return Task.FromResult<CashSessionAnswer?>(new CashSessionAnswer(CashSessionOutcomes.NoteRequired));
            }

            if (!MayClose && request.Authorisation is null)
            {
                return Task.FromResult<CashSessionAnswer?>(new CashSessionAnswer(CashSessionOutcomes.PinRequired));
            }

            DrawerIsOpen = false;
            var at = OpenedAt.AddHours(14);
            return Task.FromResult<CashSessionAnswer?>(new CashSessionAnswer(CashSessionOutcomes.Closed, Closed: Shows
                ? new ClosedCashSessionWire(
                    12, at, "Karim M.", "48350.00", request.Counted, variance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Math.Abs(variance) > 200m)
                : new ClosedCashSessionWire(12, at, "Karim M.")));
        }
    }
}
