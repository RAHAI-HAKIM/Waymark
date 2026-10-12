using Waymark.Domain.Values;

namespace Waymark.Domain.Organisation;

/// <summary>How a cash session is closed: everything written to its row at once (C1, D-111).</summary>
/// <param name="ClosedBy">Who counted.</param>
/// <param name="AuthorisedBy">Whose PIN allowed it, when who counted may not close alone; null otherwise.</param>
/// <param name="Thresholds">The tenant's thresholds at this moment, frozen on the row for its Z (C2, D-112).</param>
public sealed record SessionClose(
    string ClosedBy, string? AuthorisedBy, DateTimeOffset At, Money Counted, Money Expected, Money Variance, long ZReportNumber, string? Note,
    ReportThresholds Thresholds);

/// <summary>How many cash movements of each kind a session holds, for its report: the sums are <see cref="DrawerMovements"/>'.</summary>
public sealed record ShiftCashCounts(int PaidIn, int PaidOut, int TabRepayments);

/// <summary>A session's tickets, their lines and their payments, as <see cref="ShiftReport"/> reads them (C2).</summary>
/// <param name="CashRows">Its cash movements one by one, each with who moved it, for the report by cashier (C3).</param>
public sealed record ShiftRows(
    IReadOnlyList<ReportTicket> Tickets, IReadOnlyList<ReportRow> Rows, IReadOnlyList<ReportPart> Parts, ShiftCashCounts Cash,
    IReadOnlyList<ReportCash> CashRows);

/// <summary>
/// What a cash session reads and the one row it changes (C1, D-111). Implemented in Persistence; scoped
/// to the current store by the global filter, so a session of another store is not found here.
/// </summary>
public interface ICashSessionLedger
{
    /// <summary>The terminal's latest closed session, for "Dernière clôture"; null when it never closed one.</summary>
    Task<CashSession?> LastClosedAsync(string terminalId, CancellationToken cancellationToken = default);

    /// <summary>The highest Z number this terminal has given; null when it has given none.</summary>
    Task<long?> LastZReportNumberAsync(string terminalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Everything that moved cash in the session, as <see cref="Drawer.Expected"/> reads it: the cash
    /// parts of its completed tickets, its cash movements, and every rounding row of either.
    /// </summary>
    Task<DrawerMovements> MovementsAsync(CashSession session, CancellationToken cancellationToken = default);

    /// <summary>How many tickets the session sold or refunded: completed, a cancel left out.</summary>
    Task<int> TicketCountAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>The terminal's closed session with this Z number; null when it gave none such.</summary>
    Task<CashSession?> ClosedAsync(string terminalId, long zReportNumber, CancellationToken cancellationToken = default);

    /// <summary>The terminal's latest closes, the newest first, <paramref name="take"/> at most: the till's list of past Zs.</summary>
    Task<IReadOnlyList<CashSession>> LatestClosedAsync(string terminalId, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// The session's tickets (sales, refunds, cancels), every line of them, struck ones included, and
    /// their payment rows, for its report (C2). Open and parked tickets are not the session's yet.
    /// </summary>
    Task<ShiftRows> ReportRowsAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many times cash has moved in the session (D-119): the cash parts of its tickets and its
    /// cash movements, counted. A card sale and a cancel move none.
    /// </summary>
    Task<long> CashMovesAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Stages a blind count kept on the open session, with how many times cash had moved (D-119).</summary>
    Task StagePendingCountAsync(string sessionId, Money counted, long moves, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Stages the session closed, written when the executor commits (D-050).</summary>
    Task StageCloseAsync(string sessionId, SessionClose close, CancellationToken cancellationToken = default);
}
