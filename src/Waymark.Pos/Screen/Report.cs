using System.Globalization;
using Waymark.Contracts.Pos;
using Waymark.Domain.Values;
using Waymark.Pos.Checkout;

namespace Waymark.Pos.Screen;

/// <summary>
/// A report open in the ticket's place (C2, D-112): the till's list of closes, the report read, and
/// which one. <paramref name="Authorisation"/> is a manager's PIN step, kept only while the report is
/// open: closed, the till forgets it, so nobody reopens a report on it later (as D-109 for a ticket).
/// </summary>
/// <param name="Selected">The Z number shown; null for the X.</param>
/// <param name="Problem">Why nothing is shown: the server could not say, or has no such report.</param>
/// <param name="ByCashier">The "Par caissier" tab is the one open (C3); it stays open from one report of the list to the next.</param>
public sealed record ReportState(
    long? Selected, ShiftReportList? List = null, ShiftReportWire? Report = null, bool Loading = false, string? Problem = null,
    string? Authorisation = null, bool ByCashier = false);

/// <summary>A report as drawn: one layout for an X and a Z, told apart by its title, its chip and its facts.</summary>
public sealed record ReportView(
    string Title,
    string Chip,
    IReadOnlyList<Figure> Facts,
    string SessionTitle,
    ReportListRow? Session,
    string ClosesTitle,
    IReadOnlyList<ReportListRow> Closes,
    string ListNote,
    PanelMessage? Message,
    IReadOnlyList<ReportSection> Sections,
    string TotalLabel,
    string Total,
    string Print,
    string PrintNote,
    string Close,
    string CloseKey)
{
    /// <summary>"Synthèse" and "Par caissier" (C3); none while there is no report to split.</summary>
    public IReadOnlyList<ReportTab> Tabs { get; init; } = [];

    /// <summary>The report by cashier, when its tab is the one open: then <see cref="Sections"/> is empty.</summary>
    public CashierTable? Cashiers { get; init; }
}

/// <param name="Locked">Not all of it is the reader's: said with a padlock on the tab, which still opens (C3).</param>
public sealed record ReportTab(bool ByCashier, string Label, bool Selected, bool Locked);

/// <summary>
/// The report by cashier as drawn (C3, D-118): one column per person and the session's last, the
/// synthesis's lines in its order. Every cell is the server's figure; nothing is added up here.
/// </summary>
/// <param name="Heads">The columns, in the order of the cells of every row.</param>
/// <param name="Lock">Why the other columns are not here, and the key that asks for the owner's PIN; null when they all are.</param>
public sealed record CashierTable(IReadOnlyList<CashierHead> Heads, IReadOnlyList<CashierGroup> Groups, string Note, CashierLock? Lock);

/// <param name="Session">The session's column: drawn apart, and it stays in place when the people's scroll.</param>
public sealed record CashierHead(string Name, string Detail, bool Session);

public sealed record CashierGroup(string Title, IReadOnlyList<CashierRow> Rows);

public sealed record CashierRow(string Label, IReadOnlyList<string> Cells, bool Strong = false);

public sealed record CashierLock(string Title, string Detail, string Key);

/// <param name="Number">The Z's number; null for the X's row.</param>
public sealed record ReportListRow(long? Number, string Label, string Detail, bool Selected);

/// <summary>A block of the report: its lines, then what is said of them.</summary>
/// <param name="Aside">Beside the title: "Propriétaire seulement".</param>
/// <param name="Columns">The figures' headings, the count's then the value's; empty for none.</param>
/// <param name="MessageFigure">The message's figure, drawn large: a Z's variance.</param>
public sealed record ReportSection(
    string Title, string? Aside, IReadOnlyList<string> Columns, IReadOnlyList<ReportLine> Lines, PanelMessage? Message = null,
    string? MessageFigure = null, string? Note = null)
{
    /// <summary>Across the report's whole width, before its two columns: the flags.</summary>
    public bool Wide { get; init; }

    /// <summary>In the second column (the board): what is about cash, beside what is about tickets.</summary>
    public bool Second { get; init; }
}

/// <param name="Tone">A flag's line: its label in its tone, critical or warning. Null for a plain line.</param>
public sealed record ReportLine(string Label, string? Count, string Value, bool Strong = false, Tone? Tone = null);

/// <summary>
/// The report's screen rules (C2, D-112), apart from the window so they are tested without one. Every
/// figure is the server's; nothing here adds two of them up. Label before colour (CLAUDE.md §6): a flag
/// and a variance read by their words first, and a figure that is fine has no colour.
/// </summary>
public static class ReportScreen
{
    public static ReportView Build(TillText text, TimeZoneInfo zone, TillContext? context, ReportState state)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(state);
        var report = state.Report;
        var title = report is { Kind: ShiftReportKinds.Z, ZReportNumber: { } number } ? text.ReportZTitle(number)
            : state.Selected is { } asked && report is null ? text.ReportZTitle(asked)
            : text.ReportXTitle;

        string Clock(DateTimeOffset moment) => DisplayFigures.Clock(TimeZoneInfo.ConvertTime(moment, zone));
        string Day(DateTimeOffset moment) => TimeZoneInfo.ConvertTime(moment, zone).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        var closes = (state.List?.Closes ?? []).Select(close => new ReportListRow(
            close.ZReportNumber, text.ZListRow(close.ZReportNumber),
            $"{Day(close.ClosedAt)[..5]} · {Clock(close.ClosedAt)}", state.Selected == close.ZReportNumber)).ToList();

        // By cashier (C3): its tab once there is a report to split, and its table in the sections' place.
        var split = report?.Cashiers;
        var byCashier = state.ByCashier && split is not null;

        var message = state.Problem is { } problem ? new PanelMessage(text.ClientRefused, problem)
            : report is { Reconciles: false } ? new PanelMessage(text.NotReconciled, text.NotReconciledDetail)
            : null;

        return new ReportView(
            title,
            report?.Kind == ShiftReportKinds.X || (report is null && state.Selected is null) ? text.ReportOngoing : text.ReportReadOnly,
            report is null ? [] : Facts(text, context, report, Clock, Day),
            text.ReportListSession,
            state.List is { SessionOpen: true } ? new ReportListRow(null, text.ReportXTitle, text.SessionOngoing, state.Selected is null) : null,
            text.ReportListCloses,
            closes,
            closes.Count == 0 && state.List is not null ? text.NoPastClose : text.ReportListNote,
            message,
            report is null || byCashier ? [] : Sections(text, report, Clock),
            text.ReportTotalLabel(title),
            report is null ? (state.Loading ? text.ReportLoading : "—") : Signed(report.Takings.Net, report.Currency, text),
            text.PrintKey,
            text.PrintNote,
            text.CloseReport,
            text.EscapeKey)
        {
            Tabs = split is null
                ? []
                : [new ReportTab(false, text.TabSummary, !byCashier, false), new ReportTab(true, text.TabByCashier(split.People), byCashier, !split.Whole)],
            Cashiers = byCashier ? Table(text, report!, split!, Clock) : null,
        };
    }

    /// <summary>
    /// The table by cashier: the synthesis's lines in its order, a cell per column. What is given
    /// away and what goes out of the drawer read with their minus, as the board draws them; a tender
    /// that gave back more than it took reads below zero.
    /// </summary>
    private static CashierTable Table(TillText text, ShiftReportWire report, CashierSplitWire split, Func<DateTimeOffset, string> clock)
    {
        var code = report.Currency;
        var minus = DisplayFigures.Minus.ToString();
        IReadOnlyList<CashierColumnWire> columns = split.Session is { } session ? [.. split.Columns, session] : split.Columns;

        string Amount(string? wire) => Money(wire, code) is { } money ? DisplayFigures.Amount(money) : "?";
        string Off(string? wire) => Money(wire, code) is { IsZero: false } ? minus + Amount(wire) : Amount(wire);
        string Count(int count) => DisplayFigures.Count(count);
        CashierRow Line(string label, Func<CashierColumnWire, string> cell, bool strong = false) => new(label, [.. columns.Select(cell)], strong);

        var heads = split.Columns.Select(column =>
                // Alone beside the closed frame, a column is "Vous", with the name under it (the board, 2B).
                !split.Whole && column.Own ? new CashierHead(text.You, column.StaffName ?? "?", false)
                : new CashierHead(column.StaffName ?? "?", (text.RightToLeft ? column.RoleAr : column.RoleFr) ?? string.Empty, false))
            .ToList();
        if (split.Session is not null)
        {
            heads.Add(new CashierHead(text.SessionColumn, text.PeopleCount(split.People), true));
        }

        var groups = new List<CashierGroup>();
        if (columns.Count > 0)
        {
            // One line per tender the session used: the same lines in every column, by their place.
            var tenders = columns[0].Tenders.Select((tender, place) => Line(
                text.PaymentMethod(tender.Method), column => place < column.Tenders.Count ? Amount(column.Tenders[place].Amount) : "?")).ToList();
            tenders.Add(Line(text.TotalTaken, column => Amount(column.Net), strong: true));

            groups.Add(new CashierGroup(text.CashierHoursTitle, [Line(text.FirstOperation, column => clock(column.First)), Line(text.LastOperation, column => clock(column.Last))]));
            groups.Add(new CashierGroup(text.TakingsTitle, tenders));
            groups.Add(new CashierGroup(text.ActivityTitle,
            [
                Line(text.TicketsSold, column => Count(column.Sold)), Line(text.TicketsCancelled, column => Count(column.Cancelled)),
                Line(text.LinesStruck, column => Count(column.StruckLines)), Line(text.RefundsLine, column => Count(column.Refunds.Count)),
                Line(text.RefundsAmount, column => Amount(column.Refunds.Value)),
            ]));
            groups.Add(new CashierGroup(text.ReductionsTitle,
            [
                Line(text.DiscountsLine, column => Count(column.Discounts.Count)), Line(text.DiscountsAmount, column => Off(column.Discounts.Value)),
                Line(text.ForcedPrices, column => Count(column.Overrides.Count)),
                Line(text.ForcedPricesAmount, column => Given(string.Empty, column.Overrides, code).Value),
            ]));
            groups.Add(new CashierGroup(text.CashMovesTitle,
            [
                Line(text.CashInLine, column => Amount(column.PaidIn)), Line(text.CashOutLine, column => Off(column.PaidOut)),
                Line(text.TabRepaymentsLine, column => Amount(column.TabRepayments)),

                // D-126: counted, with no figure.
                Line(text.NoSalesRow, column => Count(column.NoSales)),
            ]));
        }

        return new CashierTable(
            heads, groups, text.CashiersNote,
            split.Whole ? null : new CashierLock(
                text.CashiersLockedTitle, text.CashiersLockedDetail(split.Columns.Count > 0, split.People - split.Columns.Count), text.OwnerPinKey));
    }

    private static List<Figure> Facts(
        TillText text, TillContext? context, ShiftReportWire report, Func<DateTimeOffset, string> clock, Func<DateTimeOffset, string> day) =>
    [
        new Figure(text.FactTill, context is null ? "—" : $"{context.TerminalName} · {context.StoreName}"),

        // A session belongs to the store's day it opened on (D-111).
        new Figure(text.FactDay, day(report.OpenedAt)),
        new Figure(text.FactOpened, $"{clock(report.OpenedAt)} · {report.OpenedBy ?? "?"}"),
        new Figure(text.FactClosed, report.ClosedAt is { } closed ? $"{clock(closed)} · {report.ClosedBy ?? "?"}" : $"— {text.SessionOngoing}"),
        report.ZReportNumber is { } number
            ? new Figure(text.FactZ, number.ToString(CultureInfo.InvariantCulture))
            : new Figure(text.FactSnapshot, clock(report.ReadAt)),
    ];

    private static List<ReportSection> Sections(TillText text, ShiftReportWire report, Func<DateTimeOffset, string> clock)
    {
        var code = report.Currency;
        string Amount(string? wire) => Money(wire, code) is { } money ? DisplayFigures.Amount(money) : "?";
        string Count(int count) => DisplayFigures.Count(count);
        ReportLine Tallied(string label, TallyWire tally, string sign = "") =>
            new(label, Count(tally.Count), Money(tally.Value, code) is { IsZero: false } ? sign + Amount(tally.Value) : Amount(tally.Value));

        var sections = new List<ReportSection>();

        // The flags first, for who may read them; everyone else is told there are some, and why not which.
        if (report.FlagCount > 0)
        {
            sections.Add(report.Flags is { } flags
                ? new ReportSection(text.FlagsTitle(report.FlagCount), text.OwnerOnly, [], [.. flags.Select(flag => Flag(text, flag, code, clock))]) { Wide = true }
                : new ReportSection(text.FlagsTitle(report.FlagCount), null, [], [], Note: text.FlagsReserved(report.FlagCount)) { Wide = true });
        }

        // The takings: the sales by tender, then what the refunds gave back, then the net.
        var takings = new List<ReportLine>();
        takings.AddRange(report.Takings.Sales.Select(line => new ReportLine(text.PaymentMethod(line.Method), Count(line.Parts), Amount(line.Amount))));
        takings.AddRange(report.Takings.Refunds.Select(line => new ReportLine(
            text.RefundOf(text.PaymentMethod(line.Method)), Count(line.Parts), DisplayFigures.Minus + Amount(line.Amount))));
        takings.Add(new ReportLine(
            text.TotalTaken, Count(report.Takings.Sales.Sum(line => line.Parts)), Signed(report.Takings.Net, code, text, withCurrency: false), Strong: true));
        sections.Add(new ReportSection(text.TakingsTitle, null, [text.ColParts, text.ColAmount], takings, Note: text.TakingsNote));

        sections.Add(new ReportSection(
            text.ActivityTitle, null, [text.ColCount, text.ColValue],
            [
                Tallied(text.TicketsSold, report.Activity.Sold), Tallied(text.RefundsLine, report.Activity.Refunded),
                Tallied(text.TicketsCancelled, report.Activity.Cancelled), Tallied(text.LinesStruck, report.Activity.StruckLines),
            ]));

        var minus = DisplayFigures.Minus.ToString();
        sections.Add(new ReportSection(
            text.ReductionsTitle, null, [text.ColCount, text.ColAmount],
            [
                Tallied(text.LineDiscounts, report.Reductions.LineDiscounts, minus), Tallied(text.TicketDiscounts, report.Reductions.TicketDiscounts, minus),
                Given(text.ForcedPrices, report.Reductions.Overrides, code),
                Given(text.ReportTotal, report.Reductions.Total, code) with { Strong = true },
            ],
            Note: text.ReductionsNote));

        sections.Add(new ReportSection(
            text.CashMovesTitle, null, [text.ColCount, text.ColAmount],
            [
                Tallied(text.CashInLine, report.CashMoves.PaidIn, "+"), Tallied(text.CashOutLine, report.CashMoves.PaidOut, minus),
                Tallied(text.TabRepaymentsLine, report.CashMoves.TabRepayments, "+"),
            ]));

        sections.Insert(sections.Count - 1, DrawerSection(text, report, clock) with { Second = true });
        sections[^1] = sections[^1] with { Second = true };
        sections.Add(NoSaleSection(text, report, clock) with { Second = true });
        return sections;
    }

    /// <summary>
    /// The drawer opened with nothing sold (board 6, D-126): the count to every reader; each opening,
    /// which names people, to the owner, by its hour, who, why and who allowed it; anyone else is told
    /// the detail is the owner's. No amount column: a no-sale moves none.
    /// </summary>
    private static ReportSection NoSaleSection(TillText text, ShiftReportWire report, Func<DateTimeOffset, string> clock)
    {
        var count = report.NoSales?.Count ?? 0;
        var lines = new List<ReportLine> { new(text.NoSalesCount, null, DisplayFigures.Count(count), Strong: true) };
        if (report.NoSales?.Openings is { } openings)
        {
            lines.AddRange(openings.Select(opening => new ReportLine(
                $"{clock(opening.At)} · {opening.StaffName ?? "?"}",
                opening.AuthorisedBy ?? "—",
                (text.RightToLeft ? opening.ReasonAr : opening.ReasonFr) + (opening.Note is { } note ? $" · « {note} »" : string.Empty))));
            return new ReportSection(text.NoSaleTitle, text.OwnerOnly, [text.NoSaleAuthorisedBy, text.NoSaleReason], lines);
        }

        return new ReportSection(text.NoSaleTitle, null, [], lines, Note: count > 0 ? text.NoSaleDetailReserved : null);
    }

    /// <summary>The drawer's lines, which add up to its expected cash, then its count and its variance by its label.</summary>
    private static ReportSection DrawerSection(TillText text, ShiftReportWire report, Func<DateTimeOffset, string> clock)
    {
        var code = report.Currency;
        var drawer = report.Drawer;
        string Amount(string? wire) => Money(wire, code) is { } money ? DisplayFigures.Amount(money) : "?";

        var lines = new List<ReportLine>
        {
            new(text.DrawerLineFloat, null, Amount(drawer.OpeningFloat)),
            new(text.DrawerLineSales, null, Amount(drawer.CashSales)),
        };

        // The cash step's own line, apart: never a variance (D-034). Left out when no ticket rounded.
        if (Money(drawer.Rounding, code) is { IsZero: false } rounding)
        {
            lines.Add(new ReportLine(text.DrawerLineRounding(drawer.RoundedTickets), null, (rounding.IsPositive ? "+" : string.Empty) + DisplayFigures.Amount(rounding)));
        }

        lines.Add(new ReportLine(text.DrawerLineRefunds, null, Amount(drawer.CashRefunds)));
        lines.Add(new ReportLine(text.DrawerLinePaidIn, null, Amount(drawer.PaidIn)));
        lines.Add(new ReportLine(text.DrawerLinePaidOut, null, Amount(drawer.PaidOut)));
        lines.Add(new ReportLine(text.DrawerLineTab, null, Amount(drawer.TabRepayments)));
        if (Money(drawer.Drops, code) is { IsZero: false })
        {
            lines.Add(new ReportLine(text.DrawerLineDrops, null, Amount(drawer.Drops)));
        }

        var counted = Money(drawer.Counted, code);
        lines.Add(new ReportLine(counted is null ? text.ExpectedAt(clock(report.ReadAt)) : text.ExpectedLine, null, Amount(drawer.Expected), Strong: true));
        lines.Add(new ReportLine(text.CountedLine, null, counted is { } count ? DisplayFigures.Amount(count) : "—"));

        // An X has neither a count nor a variance: said, not left blank.
        if (counted is null || Money(drawer.Variance, code) is not { } variance)
        {
            return new ReportSection(text.DrawerTitle, null, [], lines, Note: text.SessionOngoingNote);
        }

        var (message, figure, notice) = DrawerScreen.VarianceBlock(text, variance, drawer.PastThreshold ?? false, Money(drawer.Threshold, code), attached: drawer.Note is not null);
        var note = drawer.Note is { Length: > 0 } written ? text.NoteQuoted(written) : null;
        return new ReportSection(text.DrawerTitle, null, [], lines, message, figure, notice is null ? note : note is null ? notice : $"{notice}\n{note}");
    }

    private static ReportLine Flag(TillText text, ShiftFlagWire flag, string code, Func<DateTimeOffset, string> clock)
    {
        string Value() => Money(flag.Value, code) is { } money ? DisplayFigures.Amount(money) : "?";
        string Limit() => Money(flag.Threshold, code) is { } money ? DisplayFigures.Amount(money) : "?";
        var who = flag.StaffName ?? "?";
        var at = flag.At is { } when ? clock(when) : "?";

        return flag.Kind switch
        {
            ShiftFlagKinds.CancelAfterEncaisser => new ReportLine(text.FlagAfterEncaisser, null, text.FlagCancelDetail(Value(), who, at), Tone: Tone.Critical),
            ShiftFlagKinds.VariancePastThreshold => new ReportLine(text.FlagVariance, null, text.FlagVarianceDetail(Value(), Limit()), Tone: Tone.Critical),
            ShiftFlagKinds.TooManyCancels => new ReportLine(
                text.FlagTooMany, null, text.FlagTooManyDetail(who, flag.Count ?? 0, flag.Threshold ?? "?"), Tone: Tone.Warning),
            ShiftFlagKinds.TooManyNoSales => new ReportLine(
                text.TooManyNoSales, null, text.TooManyNoSalesDetail(who, flag.Count ?? 0, flag.Threshold), Tone: Tone.Warning),
            _ => new ReportLine(text.FlagAboveValue, null, text.FlagAboveDetail(Value(), who, at, Limit()), Tone: Tone.Warning),
        };
    }

    /// <summary>What was given away, taken off: "−455,00"; a price raised reads "+20,00".</summary>
    private static ReportLine Given(string label, TallyWire tally, string code)
    {
        var value = Money(tally.Value, code);
        var shown = value is not { } given ? "?"
            : given.IsZero ? DisplayFigures.Amount(given)
            : (given.IsPositive ? DisplayFigures.Minus.ToString() : "+") + DisplayFigures.Amount(given.Abs());
        return new ReportLine(label, DisplayFigures.Count(tally.Count), shown);
    }

    /// <summary>An amount that may be below zero, as it is: a day of refunds alone nets negative.</summary>
    private static string Signed(string wire, string code, TillText text, bool withCurrency = true) =>
        Money(wire, code) is not { } money ? "?"
        : withCurrency ? DisplayFigures.AmountWithCurrency(money, text)
        : DisplayFigures.Amount(money);

    private static Money? Money(string? text, string currency)
    {
        if (text is null)
        {
            return null;
        }

        try
        {
            return WireFigures.Money(text, currency);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return null;
        }
    }
}
