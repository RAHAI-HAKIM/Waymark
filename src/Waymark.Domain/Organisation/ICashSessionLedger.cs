using Waymark.Domain.Values;

namespace Waymark.Domain.Organisation;

/// <summary>How a cash session is closed: everything written to its row at once (C1, D-111).</summary>
/// <param name="ClosedBy">Who counted.</param>
/// <param name="AuthorisedBy">Whose PIN allowed it, when who counted may not close alone; null otherwise.</param>
public sealed record SessionClose(
    string ClosedBy, string? AuthorisedBy, DateTimeOffset At, Money Counted, Money Expected, Money Variance, long ZReportNumber, string? Note);

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

    /// <summary>Stages the session closed, written when the executor commits (D-050).</summary>
    Task StageCloseAsync(string sessionId, SessionClose close, CancellationToken cancellationToken = default);
}
