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
/// C2 (D-112): the X and Z reports at the till. The silent failures: a cashier under a blind close
/// shown the cash lines that give the drawer's figure away; a manager's PIN for one report kept and
/// used for the next; the flags listed to somebody they are about; a Z that no longer adds up shown
/// as if it did.
/// </summary>
public sealed partial class TillWindowTests
{
    private static ReportView? ReportOf(Till till) =>
        till.Window.GetVisualDescendants().OfType<ScrollViewer>().Select(scroll => scroll.Tag).OfType<ReportView>().SingleOrDefault();

    private static ReportSection Section(Till till, string title) =>
        ReportOf(till)!.Sections.Single(section => section.Title.StartsWith(title, StringComparison.Ordinal));

    private static void OpenX(Till till)
    {
        till.Tap(Operation(till, Screen.Operation.More), new Point(20, 20));
        till.Tap(Operation(till, Screen.Operation.XReport), new Point(20, 20));
    }

    [Fact]
    public Task An_x_is_the_open_session_read_now_with_no_count_and_no_variance() => Headless.Run(() =>
    {
        using var till = OpenTill(server => server.MayClose = true);
        OpenX(till);
        till.WaitFor(() => ReportOf(till)?.Sections.Count > 0);
        Shot(till, "5b-report-x");

        var report = ReportOf(till)!;
        Assert.Equal((TillText.French.ReportXTitle, TillText.French.ReportOngoing), (report.Title, report.Chip));
        var drawer = Section(till, TillText.French.DrawerTitle);
        Assert.Null(drawer.Message);
        Assert.Equal(TillText.French.SessionOngoingNote, drawer.Note);
        Assert.Equal("—", drawer.Lines.Single(line => line.Label == TillText.French.CountedLine).Value);
        Assert.Equal((null, null), Assert.Single(till.Server.ReportsRead));

        till.Key(Key.Escape, PhysicalKey.Escape);
        Assert.Null(ReportOf(till));
    });

    [Fact]
    public Task A_z_from_the_list_shows_its_variance_by_its_label_and_the_flags_to_the_owner() => Headless.Run(() =>
    {
        using var till = OpenTill(server =>
        {
            server.MayClose = true;
            server.ReadsFlags = true;
        });
        OpenX(till);
        till.WaitFor(() => ReportOf(till)?.Closes.Count == 2);

        till.Tap(Keyed<ReportListRow>(till, row => row.Number == 12), new Point(20, 10));
        till.WaitFor(() => ReportOf(till)?.Title == TillText.French.ReportZTitle(12));
        Shot(till, "5a-report-z");

        var report = ReportOf(till)!;
        Assert.Equal(TillText.French.ReportReadOnly, report.Chip);
        Assert.Null(report.Message);
        var flags = Section(till, TillText.French.FlagsTitle(4));
        Assert.Equal(TillText.French.OwnerOnly, flags.Aside);
        Assert.Equal(
            [(TillText.French.FlagAfterEncaisser, Tone.Critical), (TillText.French.FlagVariance, Tone.Critical),
                (TillText.French.FlagTooMany, Tone.Warning), (TillText.French.FlagAboveValue, Tone.Warning)],
            flags.Lines.Select(line => (line.Label, line.Tone!.Value)));
        Assert.Contains("Yacine R.", flags.Lines[0].Value, StringComparison.Ordinal);

        var drawer = Section(till, TillText.French.DrawerTitle);
        Assert.Equal((TillText.French.ShortPastThreshold, Tone.Critical), (drawer.Message!.Title, drawer.Message.Tone));
        Assert.Equal(Shown(-35_000), drawer.MessageFigure);
        Assert.Contains("Erreur de rendu", drawer.Note, StringComparison.Ordinal);
    });

    [Fact]
    public Task Somebody_who_reads_the_report_but_not_its_flags_is_told_how_many_and_no_more() => Headless.Run(() =>
    {
        using var till = OpenTill(server => server.MayClose = true); // a manager: ReadsFlags stays false
        OpenX(till);
        till.WaitFor(() => ReportOf(till)?.Closes.Count == 2);
        till.Tap(Keyed<ReportListRow>(till, row => row.Number == 12), new Point(20, 10));
        till.WaitFor(() => ReportOf(till)?.Title == TillText.French.ReportZTitle(12));
        Shot(till, "5c-report-manager");

        var flags = Section(till, TillText.French.FlagsTitle(4));
        Assert.Empty(flags.Lines);
        Assert.Equal(TillText.French.FlagsReserved(4), flags.Note);
        Assert.DoesNotContain(ReportOf(till)!.Sections.SelectMany(section => section.Lines), line => line.Value.Contains("Yacine", StringComparison.Ordinal));
    });

    [Fact]
    public Task A_cashier_reads_a_report_through_a_managers_pin_which_the_till_forgets_when_the_report_closes() => Headless.Run(() =>
    {
        using var till = OpenTill(server => server.ReportsNeedPin = true);
        OpenX(till);
        SamiaAuthorises(till);
        till.WaitFor(() => ReportOf(till)?.Sections.Count > 0);

        // Another report while this one is open goes on the same PIN.
        till.Tap(Keyed<ReportListRow>(till, row => row.Number == 12), new Point(20, 10));
        till.WaitFor(() => ReportOf(till)?.Title == TillText.French.ReportZTitle(12));
        Assert.Equal([(null, null), (null, "auth-samia"), (12L, "auth-samia")], till.Server.ReportsRead);

        // Closed, the till has forgotten it: the next report asks again.
        till.Key(Key.Escape, PhysicalKey.Escape);
        OpenX(till);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is ApproverRow));

        Assert.Equal((null, null), till.Server.ReportsRead[^1]);
        Assert.Null(ReportOf(till));
    });

    [Fact]
    public Task Under_a_blind_close_a_cashier_is_told_why_the_whole_report_is_the_managers_before_the_pin() => Headless.Run(() =>
    {
        using var till = OpenTill(server =>
        {
            server.Blind = true;
            server.ReportsNeedPin = true;
        });
        OpenX(till);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.ReservedTitle);
        Shot(till, "7a-report-reserved");

        Assert.Null(ReportOf(till)); // not a line of it is drawn behind the panel
        Assert.True(FormOf(till)!.Locked);

        till.Key(Key.Enter, PhysicalKey.Enter);
        SamiaAuthorises(till);
        till.WaitFor(() => ReportOf(till)?.Sections.Count > 0);
    });

    [Fact]
    public Task Voir_le_rapport_z_opens_it_over_the_result_which_is_there_again_when_it_closes() => Headless.Run(() =>
    {
        using var till = OpenTill(server => server.MayClose = true);
        StartClose(till);
        Keys(till, "48350");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.ConfirmCloseTitle);
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.DrawerClosedTitle);

        till.Tap(Keyed<FormKey>(till, key => key.Id == DrawerScreen.ZReportKey), new Point(10, 10));
        till.WaitFor(() => ReportOf(till)?.Title == TillText.French.ReportZTitle(12));
        Assert.DoesNotContain(till.Window.GetVisualDescendants().OfType<Border>(), border => border.Tag is FormPanel && border.IsEffectivelyVisible);

        till.Key(Key.Escape, PhysicalKey.Escape);

        Assert.Null(ReportOf(till));
        Assert.Equal(TillText.French.DrawerClosedTitle, FormOf(till)?.Title);
        Assert.Contains(till.Window.GetVisualDescendants().OfType<Border>(), border => border.Tag is FormPanel && border.IsEffectivelyVisible);
    });

    [Fact]
    public Task A_report_that_does_not_reconcile_says_so_before_any_figure() => Headless.Run(() =>
    {
        using var till = OpenTill(server =>
        {
            server.MayClose = true;
            server.ReportsReconcile = false;
        });
        OpenX(till);
        till.WaitFor(() => ReportOf(till)?.Sections.Count > 0);

        Assert.Equal((TillText.French.NotReconciled, Tone.Critical), (ReportOf(till)!.Message!.Title, ReportOf(till)!.Message!.Tone));
    });

    [Fact]
    public Task A_scan_is_ignored_while_a_report_is_open() => Headless.Run(() =>
    {
        using var till = OpenTill(server => server.MayClose = true);
        OpenX(till);
        till.WaitFor(() => ReportOf(till)?.Sections.Count > 0);

        till.Window.KeyTextInput(Till.Code(1));
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.Pump();

        Assert.Empty(till.Session.Cart.Lines);
        Assert.NotNull(ReportOf(till));
    });

    [Fact]
    public Task A_session_left_open_past_its_day_says_so_on_the_bar() => Headless.Run(() =>
    {
        // The fake's session opened on 05/10/2026, and the window's clock is today's.
        using var till = OpenTill();
        till.Pump();

        Assert.Contains(
            till.Window.GetVisualDescendants().OfType<TextBlock>(),
            block => block.Text?.Contains(TillText.French.DrawerOpenSinceDay("05/10", string.Empty)[..14], StringComparison.Ordinal) == true
                && block.Text.EndsWith("à clôturer", StringComparison.Ordinal));
    });

    // ------------------------------------------------------------------ C3: by cashier (D-118)

    private static readonly string[] OtherPeople = ["Samia", "Yacine", "Lina", "Omar", "Karim"];

    private static CashierTable? CashiersOf(Till till) => ReportOf(till)?.Cashiers;

    private static void OpenByCashier(Till till, long? number = 12)
    {
        OpenX(till);
        till.WaitFor(() => ReportOf(till)?.Closes.Count == 2);
        if (number is { } z)
        {
            till.Tap(Keyed<ReportListRow>(till, row => row.Number == z), new Point(20, 10));
            till.WaitFor(() => ReportOf(till)?.Title == TillText.French.ReportZTitle(z));
        }

        till.Tap(Keyed<ReportTab>(till, tab => tab.ByCashier), new Point(20, 10));
        till.WaitFor(() => CashiersOf(till) is not null);
    }

    private static string Cell(CashierTable table, string label, int column) =>
        table.Groups.SelectMany(group => group.Rows).Single(row => row.Label == label).Cells[column];

    [Fact]
    public Task The_owner_reads_a_column_per_person_and_the_sessions_which_is_the_synthesis() => Headless.Run(() =>
    {
        using var till = OpenTill(server =>
        {
            server.MayClose = true;
            server.ReadsFlags = true;
        });
        OpenByCashier(till);
        Shot(till, "6a-by-cashier");

        var report = ReportOf(till)!;
        Assert.Empty(report.Sections); // the same report behind a tab, never both at once
        Assert.Equal(
            [(TillText.French.TabSummary, false, false), (TillText.French.TabByCashier(6), true, false)],
            report.Tabs.Select(tab => (tab.Label, tab.Selected, tab.Locked)));

        var table = report.Cashiers!;
        Assert.Null(table.Lock);
        Assert.Equal(["Nabil B.", "Samia K.", "Yacine R.", "Lina H.", "Omar D.", "Karim M.", TillText.French.SessionColumn], table.Heads.Select(head => head.Name));
        Assert.Equal(("caissier", TillText.French.PeopleCount(6), true), (table.Heads[0].Detail, table.Heads[6].Detail, table.Heads[6].Session));
        Assert.Equal(
            [TillText.French.CashierHoursTitle, TillText.French.TakingsTitle, TillText.French.ActivityTitle, TillText.French.ReductionsTitle, TillText.French.CashMovesTitle],
            table.Groups.Select(group => group.Title));

        // The figures are the server's, each under its person; what is given away and what goes out read with their minus.
        Assert.Equal("11 280,00", Cell(table, TillText.French.PaymentMethod("cash"), 0).Replace('\u202F', ' ').Replace('\u00A0', ' '));
        Assert.Equal("41", Cell(table, TillText.French.TicketsSold, 0));
        Assert.Equal("3", Cell(table, TillText.French.TicketsCancelled, 2));
        Assert.Equal(DisplayFigures.Minus + "520,00", Cell(table, TillText.French.DiscountsAmount, 0));
        Assert.Equal("0,00", Cell(table, TillText.French.DiscountsAmount, 4)); // nothing given away has no sign
        Assert.Equal(DisplayFigures.Minus + "305,00", Cell(table, TillText.French.ForcedPricesAmount, 2));
        Assert.Equal(DisplayFigures.Minus + "850,00", Cell(table, TillText.French.CashOutLine, 5));
        Assert.Equal(DisplayFigures.Minus + "140,00", Cell(table, TillText.French.PaymentMethod("store_credit"), 3)); // gave back more than it took

        // The session's column says what the foot of the report says.
        Assert.StartsWith(Cell(table, TillText.French.TotalTaken, 6), report.Total, StringComparison.Ordinal);

        // Back to the synthesis: its sections again, and no table.
        till.Tap(Keyed<ReportTab>(till, tab => !tab.ByCashier), new Point(20, 10));
        till.WaitFor(() => ReportOf(till)?.Sections.Count > 0);
        Assert.Null(CashiersOf(till));
    });

    [Fact]
    public Task Without_the_right_one_reads_ones_own_column_and_the_rest_is_one_closed_frame_naming_nobody() => Headless.Run(() =>
    {
        using var till = OpenTill(server => server.MayClose = true); // reads the report, not what names people
        OpenByCashier(till, number: null);
        Shot(till, "6b-by-cashier-locked");

        var report = ReportOf(till)!;
        Assert.True(report.Tabs.Single(tab => tab.ByCashier).Locked);
        Assert.Equal(TillText.French.TabByCashier(6), report.Tabs.Single(tab => tab.ByCashier).Label); // how many is said to everyone

        var table = report.Cashiers!;
        var own = Assert.Single(table.Heads);
        Assert.Equal((TillText.French.You, "Nabil B.", false), (own.Name, own.Detail, own.Session));
        Assert.Equal("41", Cell(table, TillText.French.TicketsSold, 0));
        Assert.Equal(
            (TillText.French.CashiersLockedTitle, TillText.French.CashiersLockedDetail(true, 5), TillText.French.OwnerPinKey),
            (table.Lock!.Title, table.Lock.Detail, table.Lock.Key));

        // Nobody else is named anywhere on the screen.
        Assert.DoesNotContain(
            till.Window.GetVisualDescendants().OfType<TextBlock>(),
            block => block.IsEffectivelyVisible && OtherPeople.Any(name => block.Text?.Contains(name, StringComparison.Ordinal) == true));
    });

    [Fact]
    public Task The_owners_pin_opens_the_other_columns_and_the_till_forgets_it_when_the_report_closes() => Headless.Run(() =>
    {
        using var till = OpenTill(server => server.MayClose = true);
        OpenByCashier(till, number: null);

        till.Tap(till.Window.GetVisualDescendants().OfType<Control>().Single(control => Equals(control.Tag, TillViews.CashierUnlockTag)), new Point(20, 10));
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is ApproverRow));

        // The owner's step by its name, asked for what names people, never for the report.
        var step = till.Window.GetVisualDescendants().OfType<Control>().Select(control => control.Tag).OfType<Screen.Approval>().First();
        Assert.Equal((TillText.French.OwnerApproval, TillText.French.OwnerPin), (step.Label, step.PinTitle));
        Assert.Equal(Capabilities.ReadShiftFlags, till.Server.Authorisations[^1].Capability);

        SamiaAuthorises(till); // the fake's one approver stands for the owner here
        till.WaitFor(() => CashiersOf(till)?.Lock is null && CashiersOf(till)?.Heads.Count == 7);

        Assert.Equal((null, "auth-samia"), till.Server.ReportsRead[^1]);
        Assert.False(ReportOf(till)!.Tabs.Single(tab => tab.ByCashier).Locked);

        // Another report of the list on the same PIN, still by cashier.
        till.Tap(Keyed<ReportListRow>(till, row => row.Number == 12), new Point(20, 10));
        till.WaitFor(() => ReportOf(till)?.Title == TillText.French.ReportZTitle(12) && CashiersOf(till)?.Heads.Count == 7);

        // Closed, the till has forgotten the PIN: the columns are shut again.
        till.Key(Key.Escape, PhysicalKey.Escape);
        OpenByCashier(till, number: null);

        Assert.Equal((null, null), till.Server.ReportsRead[^1]);
        Assert.NotNull(CashiersOf(till)!.Lock);
    });

    [Fact]
    public Task Escape_on_the_owners_step_goes_back_to_the_report_which_is_still_closed() => Headless.Run(() =>
    {
        using var till = OpenTill(server => server.MayClose = true);
        OpenByCashier(till, number: null);
        till.Tap(till.Window.GetVisualDescendants().OfType<Control>().Single(control => Equals(control.Tag, TillViews.CashierUnlockTag)), new Point(20, 10));
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is ApproverRow));

        till.Key(Key.Escape, PhysicalKey.Escape);
        till.Pump();

        Assert.NotNull(CashiersOf(till)!.Lock);
        Assert.Single(CashiersOf(till)!.Heads);
    });

    [Fact]
    public Task On_a_narrow_screen_six_people_fit_and_past_six_theirs_scroll_between_the_labels_and_the_session() => Headless.Run(() =>
    {
        using var till = OpenTill(server =>
        {
            server.MayClose = true;
            server.ReadsFlags = true;
            server.ReportPeople = 8;
        });
        Narrow(till);
        OpenByCashier(till);
        Shot(till, "6c-by-cashier-narrow-8");

        var drawn = till.Window.GetVisualDescendants().OfType<Border>().Single(border => border.Tag is CashierTable);
        var sideways = drawn.GetVisualDescendants().OfType<ScrollViewer>().Single(scroll => scroll.HorizontalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        // The people's names scroll; the lines' labels and the session's column do not.
        TextBlock Words(string text) => drawn.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == text);
        Assert.Contains(sideways, Words("Nabil B.").GetVisualAncestors());
        Assert.DoesNotContain(sideways, Words(TillText.French.SessionColumn).GetVisualAncestors());
        Assert.DoesNotContain(sideways, Words(TillText.French.TicketsSold).GetVisualAncestors());
        Assert.True(sideways.Extent.Width > sideways.Viewport.Width, "Eight people fit a narrow screen without scrolling: the session's column was pushed off it.");

        // Nothing is drawn past the window's right edge.
        var session = Words(TillText.French.SessionColumn);
        Assert.True(session.TranslatePoint(new Point(session.Bounds.Width, 0), till.Window)!.Value.X <= till.Window.Width);
    });

    [Fact]
    public Task Six_people_fit_a_narrow_screen_without_scrolling() => Headless.Run(() =>
    {
        using var till = OpenTill(server =>
        {
            server.MayClose = true;
            server.ReadsFlags = true;
        });
        Narrow(till);
        OpenByCashier(till);
        Shot(till, "6d-by-cashier-narrow");

        var drawn = till.Window.GetVisualDescendants().OfType<Border>().Single(border => border.Tag is CashierTable);
        Assert.DoesNotContain(drawn.GetVisualDescendants().OfType<ScrollViewer>(), scroll => scroll.HorizontalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        var session = drawn.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == TillText.French.SessionColumn);
        Assert.True(session.TranslatePoint(new Point(session.Bounds.Width, 0), till.Window)!.Value.X <= till.Window.Width, "The session's column runs off a 1024 px screen.");
    });

    private sealed partial class FakeStoreServer
    {
        /// <summary>How many people the fake's session holds: the board's six, or more to make them scroll.</summary>
        public int ReportPeople { get; set; } = 6;

        /// <summary>
        /// The board's six people (1A). Whole for who reads the flags, or on the fake approver's PIN
        /// once it was asked for what names people; otherwise the column of whoever is signed in, Nabil.
        /// </summary>
        private CashierSplitWire SampleCashiers(string? authorisation)
        {
            var whole = ReadsFlags
                || (authorisation == "auth-samia" && Authorisations.Any(asked => asked is { Capability: Capabilities.ReadShiftFlags, StaffId: "samia" }));
            string[] names = ["Nabil B.", "Samia K.", "Yacine R.", "Lina H.", "Omar D.", "Karim M."];
            string[] roles = ["caissier", "caissière", "caissier", "caissière", "caissier", "responsable"];
            string[][] tenders =
            [
                ["11280.00", "9860.00", "7940.00", "6120.00", "3720.00", "1200.00", "40120.00"],
                ["3640.00", "3210.50", "2480.00", "1650.00", "900.00", "600.00", "12480.50"],
                ["2100.00", "1780.00", "1240.00", "980.00", "520.00", "300.00", "6920.00"],
                ["0.00", "860.00", "0.00", "-140.00", "0.00", "0.00", "720.00"],
                ["1820.80", "1140.00", "0.00", "650.00", "0.00", "600.00", "4210.80"],
            ];
            string[] methods = ["cash", "card", "mobile_wallet", "store_credit", "on_account"];
            string[] net = ["18840.80", "16850.50", "11660.00", "9260.00", "5140.00", "2700.00", "62758.70"];
            int[] sold = [41, 36, 29, 22, 14, 6, 148];
            int[] cancelled = [0, 1, 3, 0, 0, 0, 4];
            int[] struck = [5, 4, 6, 1, 1, 0, 17];
            (int, string)[] refunds = [(1, "840.00"), (1, "620.00"), (0, "0.00"), (0, "0.00"), (0, "0.00"), (1, "400.00"), (3, "1860.00")];
            (int, string)[] discounts = [(3, "520.00"), (2, "380.00"), (4, "760.00"), (1, "120.00"), (0, "0.00"), (1, "240.00"), (11, "2020.00")];
            (int, string)[] overrides = [(1, "150.00"), (0, "0.00"), (2, "305.00"), (0, "0.00"), (0, "0.00"), (0, "0.00"), (3, "455.00")];
            string[] paidIn = ["0.00", "0.00", "0.00", "0.00", "0.00", "2000.00", "2000.00"];
            string[] paidOut = ["500.00", "0.00", "0.00", "0.00", "0.00", "850.00", "1350.00"];
            string[] repaid = ["2000.00", "1200.00", "0.00", "0.00", "0.00", "0.00", "3200.00"];

            CashierColumnWire Column(int place, string? name, string? role, bool own) => new(
                name, role, role, own, OpenedAt.AddMinutes(28 * place), OpenedAt.AddHours(8 + place),
                [.. methods.Select((method, line) => new TenderNetWire(method, tenders[line][place]))], net[place],
                sold[place], cancelled[place], struck[place],
                new TallyWire(refunds[place].Item1, refunds[place].Item2), new TallyWire(discounts[place].Item1, discounts[place].Item2),
                new TallyWire(overrides[place].Item1, overrides[place].Item2), paidIn[place], paidOut[place], repaid[place]);

            if (!whole)
            {
                return new CashierSplitWire(ReportPeople, false, [Column(0, names[0], roles[0], true)]);
            }

            // Past the board's six, the fake repeats its last person under another name: only the width matters.
            var columns = Enumerable.Range(0, ReportPeople)
                .Select(place => place < 6 ? Column(place, names[place], roles[place], false) : Column(5, $"Extra {place}", "caissier", false))
                .ToList();
            return new CashierSplitWire(ReportPeople, true, columns, Column(6, null, null, false));
        }

        /// <summary>The person signed in is below the shop's rank for reports: a PIN, then the authorisation.</summary>
        public bool ReportsNeedPin { get; set; }

        /// <summary>Whoever reads reaches ReadShiftFlags: the owner.</summary>
        public bool ReadsFlags { get; set; }

        public bool ReportsReconcile { get; set; } = true;

        /// <summary>Every report asked for: its Z number (null for an X) and the authorisation cited.</summary>
        public List<(long? Number, string? Authorisation)> ReportsRead { get; } = [];

        public Task<ShiftReportList?> ShiftReportsAsync(string terminalId, string? authorisation, string sessionToken, CancellationToken cancellationToken = default) =>
            Task.FromResult<ShiftReportList?>(ReportsNeedPin && authorisation is null
                ? new ShiftReportList(ShiftReportOutcomes.PinRequired)
                : new ShiftReportList(
                    ShiftReportOutcomes.Ok, DrawerIsOpen,
                    [new ZSummaryWire(12, OpenedAt.AddHours(14)), new ZSummaryWire(11, OpenedAt.AddHours(-10))]));

        public Task<ShiftReportAnswer?> ShiftReportAsync(
            string terminalId, long? zReportNumber, string? authorisation, string sessionToken, CancellationToken cancellationToken = default)
        {
            ReportsRead.Add((zReportNumber, authorisation));
            if (ReportsNeedPin && authorisation is null)
            {
                return Task.FromResult<ShiftReportAnswer?>(new ShiftReportAnswer(ShiftReportOutcomes.PinRequired));
            }

            if (zReportNumber is null && !DrawerIsOpen)
            {
                return Task.FromResult<ShiftReportAnswer?>(new ShiftReportAnswer(ShiftReportOutcomes.NotFound));
            }

            return Task.FromResult<ShiftReportAnswer?>(new ShiftReportAnswer(
                ShiftReportOutcomes.Ok, SampleReport(zReportNumber) with { Cashiers = SampleCashiers(authorisation) }));
        }

        /// <summary>The board's day (5A, 5B): 148 tickets, 350,00 short of a 200,00 threshold, four flags.</summary>
        public ShiftReportWire SampleReport(long? number)
        {
            var z = number is not null;
            IReadOnlyList<ShiftFlagWire> flags =
            [
                new(ShiftFlagKinds.CancelAfterEncaisser, "Yacine R.", OpenedAt.AddHours(8), "2140.00"),
                new(ShiftFlagKinds.VariancePastThreshold, Value: "-350.00", Threshold: "200.00"),
                new(ShiftFlagKinds.TooManyCancels, "Yacine R.", Count: 3, Threshold: "2"),
                new(ShiftFlagKinds.CancelAboveValue, "Yacine R.", OpenedAt.AddHours(8), "2140.00", Threshold: "1500.00"),
            ];
            return new ShiftReportWire(
                z ? ShiftReportKinds.Z : ShiftReportKinds.X, number, "DZD", OpenedAt, "Nabil B.", z ? OpenedAt.AddHours(14) : null, z ? "Karim M." : null,
                OpenedAt.AddHours(6),
                new TakingsWire(
                    [
                        new TenderLineWire("cash", 112, "40147.40"), new TenderLineWire("card", 21, "12480.50"), new TenderLineWire("mobile_wallet", 14, "6920.00"),
                        new TenderLineWire("store_credit", 2, "860.00"), new TenderLineWire("on_account", 6, "4210.80"),
                    ],
                    [new TenderLineWire("cash", 2, "620.00"), new TenderLineWire("store_credit", 1, "1240.00")],
                    "64618.70", "1860.00", "62758.70"),
                new ActivityWire(new TallyWire(148, "64618.70"), new TallyWire(3, "1860.00"), new TallyWire(4, "5320.00"), new TallyWire(17, "2145.00")),
                new ReductionsWire(new TallyWire(9, "1380.00"), new TallyWire(2, "640.00"), new TallyWire(3, "455.00"), new TallyWire(14, "2475.00")),
                new CashMovesWire(new TallyWire(1, "2000.00"), new TallyWire(2, "1350.00"), new TallyWire(2, "3200.00")),
                new ReportDrawerWire(
                    "5000.00", "40147.40", "-27.40", 112, "620.00", "2000.00", "1350.00", "3200.00", "0.00", "48350.00",
                    z ? "48000.00" : null, z ? "-350.00" : null, z ? true : null, "200.00", z ? "Erreur de rendu probable sur un billet de 500 vers 16 h." : null),
                z ? 4 : 0,
                z && ReadsFlags ? flags : null,
                ReportsReconcile);
        }
    }
}
