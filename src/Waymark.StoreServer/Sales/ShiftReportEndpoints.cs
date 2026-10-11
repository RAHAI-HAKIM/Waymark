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

    /// <summary>Who reads, and whether the flags are theirs to read.</summary>
    private sealed record Reader(string Outcome, bool ReadsFlags = false);

    private static async Task<Reader> ReaderAsync(
        TillSessions sessions, HttpRequest http, string? terminal, string? authorisation, IRecommendationBoard staff,
        ITenantConfiguration configuration, CancellationToken cancellationToken)
    {
        var token = http.Headers[TillSessionHeader.Name].ToString();
        if (sessions.Resolve(token) is not { } session || !string.Equals(session.TerminalId, terminal, StringComparison.Ordinal))
        {
            return new Reader(ShiftReportOutcomes.NotSignedIn);
        }

        var settings = await configuration.CurrentAsync(cancellationToken);
        var sellerRank = (await staff.StaffAsync(session.StaffId, cancellationToken))?.Rank;
        var readerRank = ReaderRank(
            sellerRank, settings.XReportMinRank,
            sessions.AuthorisedBy(token, authorisation, Capability.ReadXReport) is { } approver
                ? (await staff.StaffAsync(approver, cancellationToken))?.Rank
                : null,
            out var reads);
        return reads ? new Reader(ShiftReportOutcomes.Ok, StaffPermissions.May(readerRank, Capability.ReadShiftFlags)) : new Reader(ShiftReportOutcomes.PinRequired);
    }

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
        var names = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var id in new[] { data.Session.OpenedBy, data.Session.ClosedAuthorisedBy ?? data.Session.ClosedBy }
                     .Concat(data.Flags.Select(flag => flag.StaffId)).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            names[id] = (await directory.DescribeAsync(terminal, id, cancellationToken))?.Staff?.StaffName;
        }

        return new ShiftReportAnswer(ShiftReportOutcomes.Ok, ToWire(data, id => id is not null && names.TryGetValue(id, out var name) ? name : null, reader.ReadsFlags, clock.GetUtcNow()));
    }

    /// <summary>A report as the wire carries it: the flags only to who may read them, their count to everyone.</summary>
    public static ShiftReportWire ToWire(ShiftReportData data, Func<string?, string?> nameOf, bool readsFlags, DateTimeOffset readAt)
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
            data.TakingsReconcile && data.DrawerReconciles);
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
