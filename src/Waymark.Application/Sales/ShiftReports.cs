using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Application.Sales;

/// <summary>
/// A cash session's report as read (C2, D-112): its figures worked out by <see cref="ShiftReport"/>
/// and <see cref="Drawer"/> from its own rows, and the two checks a report must pass before it is
/// shown as if it added up.
/// </summary>
/// <param name="TakingsReconcile">The takings by tender come to the tickets' totals.</param>
/// <param name="Expected">What the drawer should hold: worked out now for an X, as stored at the close for a Z.</param>
/// <param name="DrawerReconciles">A Z: its rows still come to the expected cash stored at its close. Always true of an X.</param>
/// <param name="TenderRounding">What the cash step added to or took off the tickets' cash, signed: the drawer's line, apart (D-034).</param>
/// <param name="Thresholds">What the flags were judged by: today's for an X, those frozen at the close for a Z.</param>
/// <param name="CancelledAt">When each cancelled ticket was cancelled, by its id: a cancel has no number to be named by (D-097).</param>
/// <param name="Cashiers">The same figures by who did each thing, in the order of their first operation (C3, D-118).</param>
/// <param name="CashiersReconcile">The people come to the session, line by line.</param>
public sealed record ShiftReportData(
    CashSession Session,
    Takings Takings,
    bool TakingsReconcile,
    Activity Activity,
    Reductions Reductions,
    DrawerMovements Movements,
    ShiftCashCounts CashCounts,
    Money Expected,
    bool DrawerReconciles,
    Money TenderRounding,
    int RoundedTickets,
    IReadOnlyList<ShiftFlag> Flags,
    ReportThresholds Thresholds,
    IReadOnlyDictionary<string, DateTimeOffset> CancelledAt,
    IReadOnlyList<CashierShare> Cashiers,
    bool CashiersReconcile);

/// <summary>
/// Reads a cash session's X or Z (C2, D-112). Nothing is stored but what the close wrote: the report
/// is recomputed from the session's tickets, lines, payments and cash movements each time, by the one
/// rule (<see cref="ShiftReport"/>). A Z is judged by the thresholds frozen on its row at the close,
/// so changing a threshold later rewrites no past Z.
/// </summary>
public sealed class ShiftReports(ISalesLedger ledger, ICashSessionLedger sessions, ITenantConfiguration configuration)
{
    /// <summary>The terminal's open session, read now: the X. Null when the drawer is shut.</summary>
    public async Task<ShiftReportData?> OpenAsync(string terminalId, CancellationToken cancellationToken = default) =>
        await ledger.OpenCashSessionAsync(terminalId, cancellationToken) is { } session
            ? await ReadAsync(session, cancellationToken)
            : null;

    /// <summary>The terminal's close with this Z number. Null when it never gave that number.</summary>
    public async Task<ShiftReportData?> ClosedAsync(string terminalId, long zReportNumber, CancellationToken cancellationToken = default) =>
        await sessions.ClosedAsync(terminalId, zReportNumber, cancellationToken) is { } session
            ? await ReadAsync(session, cancellationToken)
            : null;

    private async Task<ShiftReportData> ReadAsync(CashSession session, CancellationToken cancellationToken)
    {
        var currency = session.OpeningFloat.Currency;
        var rows = await sessions.ReportRowsAsync(session.SessionId, cancellationToken);
        var movements = await sessions.MovementsAsync(session, cancellationToken);
        var closed = session.Status == CashSessionStatus.Closed;

        var takings = ShiftReport.TakingsOf(currency, rows.Tickets, rows.Parts);
        var activity = ShiftReport.ActivityOf(currency, rows.Tickets, rows.Rows);
        var reductions = ShiftReport.ReductionsOf(currency, rows.Tickets, rows.Rows);
        var worked = Drawer.Expected(movements);

        // By cashier (C3): the opening and the close are operations too, with no figure. Who counted
        // is the close's person; whose PIN allowed it did nothing at the drawer.
        var moments = new List<ReportMoment> { new(session.OpenedBy, session.OpenedAt) };
        if (closed && session is { ClosedBy: { } counter, ClosedAt: { } closedAt })
        {
            moments.Add(new ReportMoment(counter, closedAt));
        }

        var cashiers = ShiftReport.ByCashierOf(currency, rows.Tickets, rows.Rows, rows.Parts, rows.CashRows, moments);

        ReportThresholds thresholds;
        if (closed)
        {
            thresholds = new ReportThresholds(
                session.VoidAlertCount is { } count ? checked((int)count) : null, session.VoidAlertValue, session.VarianceAlertValue);
        }
        else
        {
            var settings = await configuration.CurrentAsync(cancellationToken);
            thresholds = new ReportThresholds(settings.VoidAlertCount, settings.VoidAlertValue, settings.VarianceAlertValue);
        }

        // Only a ticket's rounding is the drawer's (Drawer.Expected says which): shown apart, never as a variance.
        var rounding = movements.Rounding.Where(row => row.Reference == VarianceReferenceType.Transaction).ToList();

        return new ShiftReportData(
            session,
            takings,
            ShiftReport.Reconciles(takings, rows.Tickets),
            activity,
            reductions,
            movements,
            rows.Cash,
            closed ? session.ExpectedCash ?? worked : worked,
            !closed || session.ExpectedCash == worked,
            rounding.Aggregate(Money.Zero(currency), (sum, row) => sum + row.Amount),
            rounding.Count,
            ShiftReport.FlagsOf(rows.Tickets, thresholds, closed ? session.Variance : null),
            thresholds,
            rows.Tickets.Where(ticket => ticket.Kind == TicketKind.Cancel).ToDictionary(ticket => ticket.TicketId, ticket => ticket.At, StringComparer.Ordinal),
            cashiers,
            ShiftReport.SharesReconcile(
                cashiers, takings, activity, reductions, new CashierCash(movements.PaidIn, movements.PaidOut, movements.TabRepayments)));
    }
}
