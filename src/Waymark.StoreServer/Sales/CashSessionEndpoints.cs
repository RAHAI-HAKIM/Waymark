using Waymark.Application.Commands;
using Waymark.Application.Sales;
using Waymark.Contracts.Pos;
using Waymark.Domain.Engine;
using Waymark.Domain.Organisation;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.StoreServer.Security;

namespace Waymark.StoreServer.Sales;

/// <summary>
/// The till's cash session (C1, D-111): its state, its opening with a counted float, its close with
/// a count. Who acts is the session's person (D-083); whether they close alone is asked of
/// <see cref="StaffPermissions"/> with the shop's setting, never compared here (CLAUDE.md §3.10).
/// Every answer is a 200 with its outcome.
/// </summary>
public static class CashSessionEndpoints
{
    /// <param name="oneAtATime">The sales' gate: a session opens and closes between sales, never during one.</param>
    public static void MapCashSessions(this WebApplication app, SemaphoreSlim oneAtATime)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(oneAtATime);

        app.MapGet("/api/cash/session", async (
            string? terminal, HttpRequest http, TillSessions sessions, ISalesLedger ledger, ICashSessionLedger drawer, ITillDirectory directory,
            IRecommendationBoard staff, ITenantConfiguration configuration, CancellationToken cancellationToken) =>
            Session(sessions, http, terminal) is { } session
                ? Results.Ok(await StateAsync(session, ledger, drawer, directory, staff, configuration, cancellationToken))
                : Results.Ok(new CashSessionState(CashSessionOutcomes.NotSignedIn)));

        app.MapPost("/api/cash/session/open", async (
            OpenCashSessionRequest request, HttpRequest http, TillSessions sessions, ISalesLedger ledger, ICashSessionLedger drawer,
            ITillDirectory directory, IRecommendationBoard staff, ITenantConfiguration configuration, CommandExecutor executor,
            OpenCashSessionHandler handler, CancellationToken cancellationToken) =>
        {
            if (Session(sessions, http, request.TerminalId) is not { } session)
            {
                return Results.Ok(new CashSessionAnswer(CashSessionOutcomes.NotSignedIn));
            }

            // What cannot be read is refused, never opened as an empty drawer: zero is a count too.
            if (!WireText.TryHundredths(request.OpeningFloat, out var opening))
            {
                return Results.Ok(new CashSessionAnswer(CashSessionOutcomes.Refused, Reason: "A float is an amount: 5000.00."));
            }

            await oneAtATime.WaitAsync(cancellationToken);
            try
            {
                await executor.ExecuteAsync(handler, new OpenCashSession(session.TerminalId, session.StaffId, opening), cancellationToken);
            }
            catch (CashSessionRefusedException refusal)
            {
                return Results.Ok(Refused(refusal));
            }
            finally
            {
                oneAtATime.Release();
            }

            return Results.Ok(new CashSessionAnswer(
                CashSessionOutcomes.Opened, await StateAsync(session, ledger, drawer, directory, staff, configuration, cancellationToken)));
        });

        app.MapPost("/api/cash/session/close", async (
            CloseCashSessionRequest request, HttpRequest http, TillSessions sessions, ITillDirectory directory, IRecommendationBoard staff,
            ITenantConfiguration configuration, CommandExecutor executor, CloseCashSessionHandler handler, CancellationToken cancellationToken) =>
        {
            if (Session(sessions, http, request.TerminalId) is not { } session)
            {
                return Results.Ok(new CashSessionAnswer(CashSessionOutcomes.NotSignedIn));
            }

            if (!WireText.TryHundredths(request.Counted, out var counted))
            {
                return Results.Ok(new CashSessionAnswer(CashSessionOutcomes.Refused, Reason: "A count is an amount: 48000.00."));
            }

            var settings = await configuration.CurrentAsync(cancellationToken);
            var closerMay = May((await staff.StaffAsync(session.StaffId, cancellationToken))?.Rank, Capability.CloseSession, settings);
            var authorisedBy = sessions.AuthorisedBy(http.Headers[TillSessionHeader.Name].ToString(), request.Authorisation, Capability.CloseSession);

            await oneAtATime.WaitAsync(cancellationToken);
            try
            {
                var closed = await executor.ExecuteAsync(
                    handler, new CloseCashSession(session.TerminalId, session.StaffId, counted, request.Note, closerMay, authorisedBy), cancellationToken);
                var validatedBy = (await directory.DescribeAsync(session.TerminalId, closed.AuthorisedBy ?? closed.ClosedBy, cancellationToken))?.Staff?.StaffName;
                return Results.Ok(new CashSessionAnswer(
                    CashSessionOutcomes.Closed, Closed: ClosedWire(closed, validatedBy, ShowsFigures(settings, closerMay))));
            }
            catch (CashSessionRefusedException refusal)
            {
                return Results.Ok(Refused(refusal));
            }
            finally
            {
                oneAtATime.Release();
            }
        });
    }

    /// <summary>
    /// Whether who counts is shown what the drawer should hold (D-111): always when the close is not
    /// blind; when it is, only someone who closes alone. Pure, so it is tested without a server.
    /// </summary>
    public static bool ShowsFigures(TenantSettings settings, bool closerMay)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return !settings.BlindClose || closerMay;
    }

    /// <summary>A close as the till may show it: the figures only to who <see cref="ShowsFigures"/> says.</summary>
    public static ClosedCashSessionWire ClosedWire(ClosedCashSession closed, string? validatedBy, bool showsFigures)
    {
        ArgumentNullException.ThrowIfNull(closed);
        return showsFigures
            ? new ClosedCashSessionWire(
                closed.ZReportNumber, closed.ClosedAt, validatedBy,
                WireText.Figure(closed.Expected), WireText.Figure(closed.Counted), WireText.Figure(closed.Variance), closed.NoteWasNeeded)
            : new ClosedCashSessionWire(closed.ZReportNumber, closed.ClosedAt, validatedBy);
    }

    /// <summary>The drawer's lines as the till lists them: the tender rounding inside the cash taken, so they add up.</summary>
    public static DrawerWire DrawerLines(DrawerMovements movements)
    {
        ArgumentNullException.ThrowIfNull(movements);
        var expected = Drawer.Expected(movements);

        // What the lines below leave unexplained is the tickets' rounding, and only that: shown where
        // the cash it belongs to is, never as a line a cashier would have to understand.
        var taken = expected - movements.OpeningFloat + movements.CashRefunds - movements.PaidIn - movements.TabRepayments
            + movements.PaidOut + movements.Drops;
        return new DrawerWire(
            WireText.Figure(movements.OpeningFloat), WireText.Figure(taken), WireText.Figure(movements.CashRefunds),
            WireText.Figure(movements.PaidIn), WireText.Figure(movements.PaidOut), WireText.Figure(movements.TabRepayments),
            WireText.Figure(movements.Drops), WireText.Figure(expected));
    }

    private static async Task<CashSessionState> StateAsync(
        SignedInTill session, ISalesLedger ledger, ICashSessionLedger drawer, ITillDirectory directory, IRecommendationBoard staff,
        ITenantConfiguration configuration, CancellationToken cancellationToken)
    {
        var settings = await configuration.CurrentAsync(cancellationToken);
        var mayClose = May((await staff.StaffAsync(session.StaffId, cancellationToken))?.Rank, Capability.CloseSession, settings);
        var shows = ShowsFigures(settings, mayClose);

        async Task<string?> NameAsync(string? staffId) =>
            staffId is null ? null : (await directory.DescribeAsync(session.TerminalId, staffId, cancellationToken))?.Staff?.StaffName;

        var last = await drawer.LastClosedAsync(session.TerminalId, cancellationToken);
        var lastClose = last is { ZReportNumber: { } number, ClosedAt: { } closedAt }
            ? new LastCloseWire(number, closedAt, await NameAsync(last.ClosedAuthorisedBy ?? last.ClosedBy))
            : null;

        OpenCashSessionWire? open = null;
        if (await ledger.OpenCashSessionAsync(session.TerminalId, cancellationToken) is { } current)
        {
            open = new OpenCashSessionWire(
                current.OpenedAt, await NameAsync(current.OpenedBy), WireText.Figure(current.OpeningFloat),
                await drawer.TicketCountAsync(current.SessionId, cancellationToken),
                shows ? DrawerLines(await drawer.MovementsAsync(current, cancellationToken)) : null);
        }

        return new CashSessionState(
            CashSessionOutcomes.Ok, open, lastClose, settings.BlindClose, mayClose,
            shows && settings.VarianceAlertValue is { } threshold ? WireText.Figure(threshold) : null);
    }

    private static CashSessionAnswer Refused(CashSessionRefusedException refusal) => refusal.Code switch
    {
        RefusalCodes.CloseNeedsManager => new CashSessionAnswer(CashSessionOutcomes.PinRequired, Reason: refusal.Message, Refusal: SaleWire.Coded(refusal.Code, [])),
        RefusalCodes.CloseNeedsNote => new CashSessionAnswer(CashSessionOutcomes.NoteRequired, Reason: refusal.Message, Refusal: SaleWire.Coded(refusal.Code, [])),
        _ => new CashSessionAnswer(CashSessionOutcomes.Refused, Reason: refusal.Message, Refusal: SaleWire.Coded(refusal.Code, [])),
    };

    /// <summary>The ladder, or the ladder as the shop set it (D-110); never compared here (§3.10).</summary>
    private static bool May(long? rank, Capability capability, TenantSettings settings) =>
        TillStaffWire.RaisedTo(capability, settings) is { } setTo ? StaffPermissions.May(rank, capability, setTo) : StaffPermissions.May(rank, capability);

    private static SignedInTill? Session(TillSessions sessions, HttpRequest http, string? terminalId) =>
        sessions.Resolve(http.Headers[TillSessionHeader.Name].ToString()) is { } session
            && string.Equals(session.TerminalId, terminalId, StringComparison.Ordinal)
            ? session
            : null;
}
