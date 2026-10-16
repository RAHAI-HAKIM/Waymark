using Waymark.Application.Sales;
using Waymark.Contracts.Pos;
using Waymark.Domain.Engine;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Values;
using Waymark.StoreServer.Security;

namespace Waymark.StoreServer.Sales;

/// <summary>
/// The till's X and Z reports (C2, D-112). Who reads is the session's person when their rank reaches
/// <c>ReadXReport</c> as the shop set it, else whoever's PIN the request cites; nobody else is shown
/// a figure, so a blind count learns nothing here. The flags go only to a reader whose rank reaches
/// <c>ReadShiftFlags</c>; everyone else is told how many there are. Never compared here (§3.10).
///
/// <para>
/// <b>By cashier (C3, D-118):</b> the figures name people, so they follow the flags' rule: whole to
/// who reaches <c>ReadShiftFlags</c>, by their own rank or by an owner's PIN cited for it. Anyone else
/// who reads the report is sent how many people there are and their own column.
/// </para>
/// </summary>
public static class ShiftReportEndpoints
{
    /// <summary>How many past closes a till lists; older ones are Admin's.</summary>
    public const int ListedCloses = 30;

    public static void MapShiftReports(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/cash/reports", async (
            string? terminal, string? authorisation, HttpRequest http, TillSessions sessions, IRecommendationBoard staff,
            ITenantConfiguration configuration, Waymark.Domain.Sales.ISalesLedger ledger, ICashSessionLedger drawer, CancellationToken cancellationToken) =>
        {
            var reader = await ReaderAsync(sessions, http, terminal, authorisation, staff, configuration, cancellationToken);
            if (reader.Outcome != ShiftReportOutcomes.Ok)
            {
                return Results.Ok(new ShiftReportList(reader.Outcome));
            }

            var closes = await drawer.LatestClosedAsync(terminal!, ListedCloses, cancellationToken);
            return Results.Ok(new ShiftReportList(
                ShiftReportOutcomes.Ok,
                await ledger.OpenCashSessionAsync(terminal!, cancellationToken) is not null,
                [.. closes.Where(close => close is { ZReportNumber: not null, ClosedAt: not null })
                    .Select(close => new ZSummaryWire(close.ZReportNumber!.Value, close.ClosedAt!.Value))]));
        });

        app.MapGet("/api/cash/reports/x", async (
            string? terminal, string? authorisation, HttpRequest http, TillSessions sessions, IRecommendationBoard staff,
            ITenantConfiguration configuration, ShiftReports reports, ITillDirectory directory, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            var reader = await ReaderAsync(sessions, http, terminal, authorisation, staff, configuration, cancellationToken);
            return reader.Outcome != ShiftReportOutcomes.Ok ? Results.Ok(new ShiftReportAnswer(reader.Outcome))
                : await reports.OpenAsync(terminal!, cancellationToken) is { } data
                    ? Results.Ok(await AnswerAsync(data, reader, terminal!, directory, clock, cancellationToken))
                    : Results.Ok(new ShiftReportAnswer(ShiftReportOutcomes.NotFound));
        });

        app.MapGet("/api/cash/reports/z/{number:long}", async (
            long number, string? terminal, string? authorisation, HttpRequest http, TillSessions sessions, IRecommendationBoard staff,
            ITenantConfiguration configuration, ShiftReports reports, ITillDirectory directory, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            var reader = await ReaderAsync(sessions, http, terminal, authorisation, staff, configuration, cancellationToken);
            return reader.Outcome != ShiftReportOutcomes.Ok ? Results.Ok(new ShiftReportAnswer(reader.Outcome))
                : await reports.ClosedAsync(terminal!, number, cancellationToken) is { } data
                    ? Results.Ok(await AnswerAsync(data, reader, terminal!, directory, clock, cancellationToken))
                    : Results.Ok(new ShiftReportAnswer(ShiftReportOutcomes.NotFound));
        });
    }

    /// <summary>Who reads, and whether the flags and the other people's figures are theirs to read.</summary>
    private sealed record Reader(string Outcome, bool ReadsFlags = false, string? StaffId = null);

    private static async Task<Reader> ReaderAsync(
        TillSessions sessions, HttpRequest http, string? terminal, string? authorisation, IRecommendationBoard staff,
        ITenantConfiguration configuration, CancellationToken cancellationToken)
    {
        var token = http.Headers[TillSessionHeader.Name].ToString();
        if (sessions.Resolve(token) is not { } session || !string.Equals(session.TerminalId, terminal, StringComparison.Ordinal))
        {
            return new Reader(ShiftReportOutcomes.NotSignedIn);
        }

        async Task<long?> RankOf(string? id) => id is null ? null : (await staff.StaffAsync(id, cancellationToken))?.Rank;

        // A PIN cited for the report, or the owner's cited for what names people (C3).
        var approver = sessions.AuthorisedBy(token, authorisation, Capability.ReadXReport);
        var owner = sessions.AuthorisedBy(token, authorisation, Capability.ReadShiftFlags);
        var settings = await configuration.CurrentAsync(cancellationToken);

        // The PIN that just let a close through opens its Z too (block D review), when its giver's rank
        // reads reports as the shop set it: the same manager, asked twice in a breath, was asked once too many.
        if (approver is null && sessions.AuthorisedBy(token, authorisation, Capability.CloseSession) is { } closer
            && ClosersRead(await RankOf(closer), settings.XReportMinRank))
        {
            approver = closer;
        }
        var (reads, readsFlags, reader) = WhoReads(
            session.StaffId, await RankOf(session.StaffId), settings.XReportMinRank, approver, await RankOf(approver), owner, await RankOf(owner));
        return reads ? new Reader(ShiftReportOutcomes.Ok, readsFlags, reader) : new Reader(ShiftReportOutcomes.PinRequired);
    }

    /// <summary>
    /// Who reads a report, and whether what names people is theirs (C3, D-118). Pure, so it is tested
    /// without a server. In this order: the person whose PIN was cited for <c>ReadShiftFlags</c>, when
    /// their rank does reach it: they read everything, whoever is signed in. Else the seller, when
    /// their own rank reads reports. Else whoever's PIN was cited for the report. The flags and the
    /// other people's figures follow that one person's rank, and "their own column" is that person's.
    /// </summary>
    public static (bool Reads, bool ReadsFlags, string? StaffId) WhoReads(
        string sellerId, long? sellerRank, long? xReportMinRank, string? approverId, long? approverRank, string? ownerId, long? ownerRank)
    {
        if (ownerId is not null && StaffPermissions.May(ownerRank, Capability.ReadShiftFlags))
        {
            return (true, true, ownerId);
        }

        ReaderRank(sellerRank, xReportMinRank, null, out var alone);
        if (alone)
        {
            return (true, StaffPermissions.May(sellerRank, Capability.ReadShiftFlags), sellerId);
        }

        var rank = ReaderRank(sellerRank, xReportMinRank, approverId is null ? null : approverRank, out var reads);
        return reads ? (true, StaffPermissions.May(rank, Capability.ReadShiftFlags), approverId) : (false, false, null);
    }

    /// <summary>Whether whoever approved a close may read reports, as the shop set it (block D review).</summary>
    public static bool ClosersRead(long? rank, long? xReportMinRank) => xReportMinRank is { } setTo
        ? StaffPermissions.May(rank, Capability.ReadXReport, setTo)
        : StaffPermissions.May(rank, Capability.ReadXReport);

    /// <summary>
    /// The rank a report is read with: the seller's when it reaches <c>ReadXReport</c> as the shop set it,
    /// else the rank of whoever's PIN was cited. Pure, so it is tested without a server. The flags
    /// follow that same person: an owner's PIN at a cashier's till shows them, a manager's does not.
    /// </summary>
    public static long? ReaderRank(long? sellerRank, long? xReportMinRank, long? approverRank, out bool reads)
    {
        bool May(long? rank) => xReportMinRank is { } setTo
            ? StaffPermissions.May(rank, Capability.ReadXReport, setTo)
            : StaffPermissions.May(rank, Capability.ReadXReport);

        if (May(sellerRank))
        {
            reads = true;
            return sellerRank;
        }

        // An authorisation was given by somebody the server found fit to give it: their rank reads.
        reads = approverRank is not null;
        return approverRank;
    }

    private static async Task<ShiftReportAnswer> AnswerAsync(
        ShiftReportData data, Reader reader, string terminal, ITillDirectory directory, TimeProvider clock, CancellationToken cancellationToken)
    {
        var people = new Dictionary<string, StaffDescription?>(StringComparer.Ordinal);
        foreach (var id in new[] { data.Session.OpenedBy, data.Session.ClosedAuthorisedBy ?? data.Session.ClosedBy }
                     .Concat(data.Flags.Select(flag => flag.StaffId)).Concat(data.Cashiers.Select(share => share.StaffId))
                     .Concat((data.NoSales ?? []).SelectMany(opening => new[] { opening.StaffId, opening.AuthorisedBy }))
                     .OfType<string>().Distinct(StringComparer.Ordinal))
        {
            people[id] = (await directory.DescribeAsync(terminal, id, cancellationToken))?.Staff;
        }

        StaffDescription? Described(string? id) => id is not null && people.TryGetValue(id, out var person) ? person : null;
        return new ShiftReportAnswer(
            ShiftReportOutcomes.Ok,
            ToWire(data, id => Described(id)?.StaffName, reader.ReadsFlags, clock.GetUtcNow(), reader.StaffId, Described));
    }

    /// <summary>
    /// A report as the wire carries it: the flags only to who may read them, their count to everyone;
    /// and the same of the figures by cashier, of which <paramref name="reader"/> is still sent their own.
    /// </summary>
    /// <param name="reader">Who reads; null when nobody in particular does, and then no column is anybody's own.</param>
    /// <param name="describe">A person's name and role; null to name them by <paramref name="nameOf"/> alone.</param>
    public static ShiftReportWire ToWire(
        ShiftReportData data, Func<string?, string?> nameOf, bool readsFlags, DateTimeOffset readAt, string? reader = null,
        Func<string?, StaffDescription?>? describe = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(nameOf);
        var session = data.Session;
        var closed = session.Status == CashSessionStatus.Closed;
        var moves = data.Movements;

        return new ShiftReportWire(
            closed ? ShiftReportKinds.Z : ShiftReportKinds.X,
            closed ? session.ZReportNumber : null,
            session.OpeningFloat.Currency.Code,
            session.OpenedAt,
            nameOf(session.OpenedBy),
            session.ClosedAt,
            nameOf(session.ClosedAuthorisedBy ?? session.ClosedBy),
            readAt,
            new TakingsWire(
                [.. data.Takings.Sales.Select(Line)], [.. data.Takings.Refunds.Select(Line)],
                WireText.Figure(data.Takings.SalesTotal), WireText.Figure(data.Takings.RefundsTotal), WireText.Figure(data.Takings.Net)),
            new ActivityWire(Tally(data.Activity.Sold), Tally(data.Activity.Refunded), Tally(data.Activity.Cancelled), Tally(data.Activity.StruckLines)),
            new ReductionsWire(
                Tally(data.Reductions.LineDiscounts), Tally(data.Reductions.TicketDiscounts), Tally(data.Reductions.Overrides), Tally(data.Reductions.Total)),
            new CashMovesWire(
                new TallyWire(data.CashCounts.PaidIn, WireText.Figure(moves.PaidIn)),
                new TallyWire(data.CashCounts.PaidOut, WireText.Figure(moves.PaidOut)),
                new TallyWire(data.CashCounts.TabRepayments, WireText.Figure(moves.TabRepayments))),
            new ReportDrawerWire(
                WireText.Figure(moves.OpeningFloat), WireText.Figure(moves.CashSales), WireText.Figure(data.TenderRounding), data.RoundedTickets,
                WireText.Figure(moves.CashRefunds), WireText.Figure(moves.PaidIn), WireText.Figure(moves.PaidOut), WireText.Figure(moves.TabRepayments),
                WireText.Figure(moves.Drops), WireText.Figure(data.Expected),
                closed && session.CountedCash is { } counted ? WireText.Figure(counted) : null,
                closed && session.Variance is { } variance ? WireText.Figure(variance) : null,
                closed && session.Variance is { } gap ? Drawer.NeedsNote(gap, data.Thresholds.Variance) : null,
                data.Thresholds.Variance is { } threshold ? WireText.Figure(threshold) : null,
                closed ? session.Notes : null),
            data.Flags.Count,
            readsFlags ? [.. data.Flags.Select(flag => Flag(flag, data, nameOf))] : null,
            data.TakingsReconcile && data.DrawerReconciles && data.CashiersReconcile,
            Cashiers(data, nameOf, readsFlags, reader, describe),
            // D-126: the count to everyone who reads; each opening names people, so the owner's.
            new NoSalesWire(
                data.NoSales?.Count ?? 0,
                readsFlags
                    ? [.. (data.NoSales ?? []).Select(opening => new NoSaleRowWire(
                        opening.At, nameOf(opening.StaffId), opening.ReasonFr, opening.ReasonAr, opening.Note, nameOf(opening.AuthorisedBy)))]
                    : null));
    }

    /// <summary>
    /// The figures by cashier (C3, D-118). They name people: whole to who reads the flags; to anyone
    /// else, how many people there are and the reader's own column. The session's column is the
    /// report's own figures, never the columns added up here: that they agree is
    /// <see cref="ShiftReportData.CashiersReconcile"/>.
    /// </summary>
    private static CashierSplitWire Cashiers(
        ShiftReportData data, Func<string?, string?> nameOf, bool whole, string? reader, Func<string?, StaffDescription?>? describe)
    {
        // One line per tender the session used, in the report's order, in every column.
        var methods = data.Takings.Sales.Concat(data.Takings.Refunds).Select(line => line.Method).Distinct().Order().ToList();

        CashierColumnWire Column(CashierShare share, string? staffId)
        {
            var person = describe?.Invoke(staffId);
            return new CashierColumnWire(
                person?.StaffName ?? nameOf(staffId), person?.RoleLabelFr, person?.RoleLabelAr,
                staffId is not null && string.Equals(staffId, reader, StringComparison.Ordinal),
                share.First, share.Last,
                [.. methods.Select(method => new TenderNetWire(Method(method), WireText.Figure(ShiftReport.NetOf(share.Takings, method))))],
                WireText.Figure(share.Takings.Net),
                share.Activity.Sold.Count, share.Activity.Cancelled.Count, share.Activity.StruckLines.Count,
                Tally(share.Activity.Refunded),
                new TallyWire(
                    share.Reductions.LineDiscounts.Count + share.Reductions.TicketDiscounts.Count,
                    WireText.Figure(share.Reductions.LineDiscounts.Value + share.Reductions.TicketDiscounts.Value)),
                Tally(share.Reductions.Overrides),
                WireText.Figure(share.Cash.PaidIn), WireText.Figure(share.Cash.PaidOut), WireText.Figure(share.Cash.TabRepayments),
                share.NoSales);
        }

        var shown = data.Cashiers.Where(share => whole || string.Equals(share.StaffId, reader, StringComparison.Ordinal)).ToList();
        if (!whole || data.Cashiers.Count == 0)
        {
            return new CashierSplitWire(data.Cashiers.Count, whole, [.. shown.Select(share => Column(share, share.StaffId))]);
        }

        var session = new CashierShare(
            string.Empty, data.Cashiers.Min(share => share.First), data.Cashiers.Max(share => share.Last), data.Takings, data.Activity, data.Reductions,
            new CashierCash(data.Movements.PaidIn, data.Movements.PaidOut, data.Movements.TabRepayments),
            data.NoSales?.Count ?? 0);
        return new CashierSplitWire(data.Cashiers.Count, true, [.. shown.Select(share => Column(share, share.StaffId))], Column(session, null));
    }

    private static ShiftFlagWire Flag(ShiftFlag flag, ShiftReportData data, Func<string?, string?> nameOf) => flag.Kind switch
    {
        ShiftFlagKind.CancelAfterEncaisser => new ShiftFlagWire(
            ShiftFlagKinds.CancelAfterEncaisser, nameOf(flag.StaffId), At(flag, data), Value(flag)),
        ShiftFlagKind.CancelAboveValue => new ShiftFlagWire(
            ShiftFlagKinds.CancelAboveValue, nameOf(flag.StaffId), At(flag, data), Value(flag),
            Threshold: data.Thresholds.VoidValue is { } most ? WireText.Figure(most) : null),
        ShiftFlagKind.TooManyCancels => new ShiftFlagWire(
            ShiftFlagKinds.TooManyCancels, nameOf(flag.StaffId), Count: flag.Count,
            Threshold: data.Thresholds.VoidCount?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ShiftFlagKind.TooManyNoSales => new ShiftFlagWire(
            ShiftFlagKinds.TooManyNoSales, nameOf(flag.StaffId), Count: flag.Count,
            Threshold: data.Thresholds.NoSaleCount?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        _ => new ShiftFlagWire(
            ShiftFlagKinds.VariancePastThreshold, Value: Value(flag),
            Threshold: data.Thresholds.Variance is { } limit ? WireText.Figure(limit) : null),
    };

    private static string? Value(ShiftFlag flag) => flag.Value is { } value ? WireText.Figure(value) : null;

    /// <summary>When the flagged ticket was cancelled: the rule names it by its id, the report by its hour.</summary>
    private static DateTimeOffset? At(ShiftFlag flag, ShiftReportData data) =>
        flag.TicketId is { } id && data.CancelledAt.TryGetValue(id, out var at) ? at : null;

    private static TenderLineWire Line(TenderLine line) => new(Method(line.Method), line.Parts, WireText.Figure(line.Amount));

    private static TallyWire Tally(Tally tally) => new(tally.Count, WireText.Figure(tally.Value));

    private static string Method(PaymentMethod method) => method switch
    {
        PaymentMethod.Card => TenderMethods.Card,
        PaymentMethod.MobileWallet => TenderMethods.MobileWallet,
        PaymentMethod.Cash => TenderMethods.Cash,
        PaymentMethod.OnAccount => TenderMethods.OnAccount,
        PaymentMethod.StoreCredit => TenderMethods.StoreCredit,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "A ticket is paid in cash, by card, by wallet, on the tab or in store credit."),
    };
}
