using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Values;

namespace Waymark.Persistence.Organisation;

/// <summary>
/// <see cref="ICashSessionLedger"/> over the store database (C1, D-111). Never names the store:
/// <c>cash_sessions</c> is filtered, and its movements and its tickets' payments through their parent.
/// </summary>
public sealed class CashSessionLedger(WaymarkDbContext context) : ICashSessionLedger
{
    public Task<CashSession?> LastClosedAsync(string terminalId, CancellationToken cancellationToken = default) =>
        // By its number, not its date: the number is what a till gives in order (ux_cash_sessions_z_number).
        context.CashSessions.AsNoTracking()
            .Where(session => session.TerminalId == terminalId && session.Status == CashSessionStatus.Closed)
            .OrderByDescending(session => session.ZReportNumber)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<long?> LastZReportNumberAsync(string terminalId, CancellationToken cancellationToken = default) =>
        context.CashSessions
            .Where(session => session.TerminalId == terminalId)
            .MaxAsync(session => session.ZReportNumber, cancellationToken);

    public async Task<DrawerMovements> MovementsAsync(CashSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var zero = Money.Zero(session.OpeningFloat.Currency);

        // A day's rows of one till, added up here: a Money column is not summed in SQL, and a session
        // is a few hundred rows.
        var tickets = await context.Transactions.AsNoTracking()
            .Where(ticket => ticket.CashSessionId == session.SessionId && ticket.Status != TransactionStatus.Voided)
            .Select(ticket => ticket.TransactionId)
            .ToListAsync(cancellationToken);
        var cash = await context.TransactionPayments.AsNoTracking()
            .Where(payment => payment.PaymentMethod == PaymentMethod.Cash && tickets.Contains(payment.TransactionId))
            .Select(payment => payment.Amount)
            .ToListAsync(cancellationToken);

        var movements = await context.CashMovements.AsNoTracking()
            .Where(movement => movement.SessionId == session.SessionId)
            .ToListAsync(cancellationToken);
        var movementIds = movements.Select(movement => movement.MovementId).ToList();

        // The tab's repayments of this session: the receivable row names the paid_in that took the cash (D-055).
        var repayments = await context.ReceivableMovements.AsNoTracking()
            .Where(row => row.CashMovementId != null && movementIds.Contains(row.CashMovementId))
            .Select(row => new { row.MovementId, row.CashMovementId })
            .ToListAsync(cancellationToken);
        var repaid = repayments.Select(row => row.CashMovementId).ToHashSet(StringComparer.Ordinal);

        // Every rounding row of the session, a ticket's and a repayment's alike: which of them is
        // cash the drawer has not been told of is Drawer.Expected's to say, not this query's.
        var references = tickets.Concat(repayments.Select(row => row.MovementId)).ToList();
        var rounding = await context.RoundingVariances.AsNoTracking()
            .Where(row => row.Source == VarianceSource.CashTender && references.Contains(row.ReferenceId))
            .Select(row => new { row.ReferenceType, row.Amount })
            .ToListAsync(cancellationToken);

        Money Sum(IEnumerable<Money> amounts) => amounts.Aggregate(zero, (total, amount) => total + amount);
        Money Moved(Func<CashMovement, bool> which) => Sum(movements.Where(which).Select(movement => movement.Amount));

        return new DrawerMovements(
            session.OpeningFloat,
            CashSales: Sum(cash.Where(amount => amount.IsPositive)),
            CashRefunds: -Sum(cash.Where(amount => amount.IsNegative)),
            PaidIn: Moved(movement => movement.MovementType is CashMovementType.PaidIn or CashMovementType.FloatAdd && !repaid.Contains(movement.MovementId)),
            TabRepayments: Moved(movement => repaid.Contains(movement.MovementId)),
            PaidOut: Moved(movement => movement.MovementType is CashMovementType.PaidOut or CashMovementType.FloatRemove),
            Drops: Moved(movement => movement.MovementType == CashMovementType.Drop),
            Rounding: [.. rounding.Select(row => new TenderRounding(row.ReferenceType, row.Amount))]);
    }

    public Task<int> TicketCountAsync(string sessionId, CancellationToken cancellationToken = default) =>
        context.Transactions.CountAsync(
            ticket => ticket.CashSessionId == sessionId && ticket.Status != TransactionStatus.Voided
                && ticket.Status != TransactionStatus.Open && ticket.Status != TransactionStatus.Parked,
            cancellationToken);

    public async Task StageCloseAsync(string sessionId, SessionClose close, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(close);
        var session = await context.CashSessions.FirstAsync(row => row.SessionId == sessionId, cancellationToken);

        // Init-only properties: the change goes through the tracked entry, as ShiftLedger does.
        var entry = context.Entry(session);
        entry.Property(row => row.ClosedBy).CurrentValue = close.ClosedBy;
        entry.Property(row => row.ClosedAuthorisedBy).CurrentValue = close.AuthorisedBy;
        entry.Property(row => row.ClosedAt).CurrentValue = close.At;
        entry.Property(row => row.CountedCash).CurrentValue = close.Counted;
        entry.Property(row => row.ExpectedCash).CurrentValue = close.Expected;
        entry.Property(row => row.Variance).CurrentValue = close.Variance;
        entry.Property(row => row.ZReportNumber).CurrentValue = close.ZReportNumber;
        entry.Property(row => row.Notes).CurrentValue = close.Note;
        entry.Property(row => row.Status).CurrentValue = CashSessionStatus.Closed;
        entry.Property(row => row.UpdatedAt).CurrentValue = close.At;
    }
}
