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

    private sealed partial class FakeStoreServer
    {
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

            return Task.FromResult<ShiftReportAnswer?>(new ShiftReportAnswer(ShiftReportOutcomes.Ok, SampleReport(zReportNumber)));
        }

        /// <summary>The board's day (5A, 5B): 148 tickets, 350,00 short of a 200,00 threshold, four flags.</summary>
        private ShiftReportWire SampleReport(long? number)
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
