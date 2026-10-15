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

    public Task<CashSession?> ClosedAsync(string terminalId, long zReportNumber, CancellationToken cancellationToken = default) =>
        context.CashSessions.AsNoTracking()
            .Where(session => session.TerminalId == terminalId && session.Status == CashSessionStatus.Closed && session.ZReportNumber == zReportNumber)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CashSession>> LatestClosedAsync(string terminalId, int take, CancellationToken cancellationToken = default) =>
        await context.CashSessions.AsNoTracking()
            .Where(session => session.TerminalId == terminalId && session.Status == CashSessionStatus.Closed)
            .OrderByDescending(session => session.ZReportNumber)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ReportNoSale>> NoSalesAsync(string sessionId, CancellationToken cancellationToken = default) =>
        await context.NoSaleOpenings
            .Where(opening => opening.SessionId == sessionId)
            .Join(context.ReasonCodes, opening => opening.ReasonCode, reason => reason.ReasonCodeValue, (opening, reason) => new { opening, reason })
            .OrderBy(row => row.opening.OccurredAt)
            .Select(row => new ReportNoSale(
                row.opening.StaffId, row.opening.OccurredAt, row.reason.LabelFr, row.reason.LabelAr, row.opening.Note, row.opening.AuthorisedBy))
            .ToListAsync(cancellationToken);

    public async Task<ShiftRows> ReportRowsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var tickets = await context.Transactions.AsNoTracking()
            .Where(ticket => ticket.CashSessionId == sessionId
                && ticket.Status != TransactionStatus.Open && ticket.Status != TransactionStatus.Parked)
            .ToListAsync(cancellationToken);

        // The session's tickets as a subquery, never as a list of ids: a list becomes one SQL
        // variable an id, SQLite takes 32 766 of them, and nothing closes a session by itself
        // (D-111), so one left open for weeks could no longer be read, paid out of or closed.
        var ids = context.Transactions
            .Where(ticket => ticket.CashSessionId == sessionId
                && ticket.Status != TransactionStatus.Open && ticket.Status != TransactionStatus.Parked)
            .Select(ticket => ticket.TransactionId);
        var items = await context.TransactionItems.AsNoTracking()
            .Where(item => ids.Contains(item.TransactionId))
            .ToListAsync(cancellationToken);
        var payments = await context.TransactionPayments.AsNoTracking()
            .Where(payment => ids.Contains(payment.TransactionId))
            .ToListAsync(cancellationToken);

        var movements = await context.CashMovements.AsNoTracking()
            .Where(movement => movement.SessionId == sessionId)
            .Select(movement => new { movement.MovementId, movement.MovementType, movement.StaffId, movement.OccurredAt, movement.Amount })
            .ToListAsync(cancellationToken);
        var repaid = (await context.ReceivableMovements.AsNoTracking()
                .Where(row => row.CashMovementId != null && SessionMovementIds(sessionId).Contains(row.CashMovementId))
                .Select(row => row.CashMovementId)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        return new ShiftRows(
            [.. tickets.Select(ticket => new ReportTicket(
                ticket.TransactionId,
                // A cancel is voided; a refund names the sale it refunds (D-098); anything else was sold.
                ticket.Status == TransactionStatus.Voided ? TicketKind.Cancel
                    : ticket.OriginalTransactionId is not null ? TicketKind.Refund
                    : TicketKind.Sale,
                ticket.Status == TransactionStatus.Voided ? ticket.VoidedBy ?? ticket.StaffId : ticket.StaffId,
                ticket.Status == TransactionStatus.Voided ? ticket.VoidedAt ?? ticket.OccurredAt : ticket.OccurredAt,
                ticket.TotalAmount,
                ticket.PaymentOpenedAt is not null,
                ticket.DiscountReasonCode,
                ticket.RoundingPolicy))],
            [.. items.Select(item => new ReportRow(
                item.TransactionId, item.LineNumber, item.RemovedAt is not null, item.Quantity, item.LineTotal, item.DiscountAmount,
                item.DiscountReasonCode, item.ListPrice, item.RemovedBy, item.RemovedAt))],
            [.. payments.Select(payment => new ReportPart(payment.TransactionId, payment.PaymentMethod, payment.Amount))],
            new ShiftCashCounts(
                movements.Count(movement => movement.MovementType is CashMovementType.PaidIn or CashMovementType.FloatAdd && !repaid.Contains(movement.MovementId)),
                movements.Count(movement => movement.MovementType is CashMovementType.PaidOut or CashMovementType.FloatRemove),
                movements.Count(movement => repaid.Contains(movement.MovementId))),

            // The same rows one by one, sorted as MovementsAsync sums them: a tab's repayment is a
            // paid_in the receivable names (D-055), and it is its own line, never an "entrée".
            [.. movements.Select(movement => new ReportCash(
                movement.StaffId,
                movement.OccurredAt,
                repaid.Contains(movement.MovementId) ? CashRowKind.TabRepayment
                    : movement.MovementType is CashMovementType.PaidIn or CashMovementType.FloatAdd ? CashRowKind.PaidIn
                    : movement.MovementType is CashMovementType.PaidOut or CashMovementType.FloatRemove ? CashRowKind.PaidOut
                    : CashRowKind.Drop,
                movement.Amount))]);
    }

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
        // A subquery, not a list of ids: see ReportRowsAsync.
        var sessionId = session.SessionId;
        var tickets = context.Transactions
            .Where(ticket => ticket.CashSessionId == sessionId && ticket.Status != TransactionStatus.Voided)
            .Select(ticket => ticket.TransactionId);
        var cash = await context.TransactionPayments.AsNoTracking()
            .Where(payment => payment.PaymentMethod == PaymentMethod.Cash && tickets.Contains(payment.TransactionId))
            .Select(payment => payment.Amount)
            .ToListAsync(cancellationToken);

        var movements = await context.CashMovements.AsNoTracking()
            .Where(movement => movement.SessionId == session.SessionId)
            .ToListAsync(cancellationToken);

        // The tab's repayments of this session: the receivable row names the paid_in that took the cash (D-055).
        var repayments = context.ReceivableMovements
            .Where(row => row.CashMovementId != null && SessionMovementIds(sessionId).Contains(row.CashMovementId));
        var repaid = (await repayments.AsNoTracking().Select(row => row.CashMovementId).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        // Every rounding row of the session, a ticket's and a repayment's alike: which of them is
        // cash the drawer has not been told of is Drawer.Expected's to say, not this query's.
        var rounding = await context.RoundingVariances.AsNoTracking()
            .Where(row => row.Source == VarianceSource.CashTender
                && (tickets.Contains(row.ReferenceId) || repayments.Select(repayment => repayment.MovementId).Contains(row.ReferenceId)))
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

    /// <summary>The session's cash movements, as a subquery.</summary>
    private IQueryable<string> SessionMovementIds(string sessionId) =>
        context.CashMovements.Where(movement => movement.SessionId == sessionId).Select(movement => movement.MovementId);

    public Task<int> TicketCountAsync(string sessionId, CancellationToken cancellationToken = default) =>
        context.Transactions.CountAsync(
            ticket => ticket.CashSessionId == sessionId && ticket.Status != TransactionStatus.Voided
                && ticket.Status != TransactionStatus.Open && ticket.Status != TransactionStatus.Parked,
            cancellationToken);

    public async Task<long> CashMovesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var tickets = context.Transactions
            .Where(ticket => ticket.CashSessionId == sessionId && ticket.Status != TransactionStatus.Voided)
            .Select(ticket => ticket.TransactionId);
        var parts = await context.TransactionPayments
            .LongCountAsync(payment => payment.PaymentMethod == PaymentMethod.Cash && tickets.Contains(payment.TransactionId), cancellationToken);
        return parts + await context.CashMovements.LongCountAsync(movement => movement.SessionId == sessionId, cancellationToken);
    }

    public async Task StagePendingCountAsync(string sessionId, Money counted, long moves, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var session = await context.CashSessions.FirstAsync(row => row.SessionId == sessionId, cancellationToken);
        var entry = context.Entry(session);
        entry.Property(row => row.PendingCount).CurrentValue = counted;
        entry.Property(row => row.PendingCountMoves).CurrentValue = moves;
        entry.Property(row => row.UpdatedAt).CurrentValue = at;
    }

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
        entry.Property(row => row.VoidAlertCount).CurrentValue = close.Thresholds.VoidCount;
        entry.Property(row => row.VoidAlertValue).CurrentValue = close.Thresholds.VoidValue;
        entry.Property(row => row.VarianceAlertValue).CurrentValue = close.Thresholds.Variance;
        entry.Property(row => row.NoSaleAlertCount).CurrentValue = close.Thresholds.NoSaleCount;
        entry.Property(row => row.Status).CurrentValue = CashSessionStatus.Closed;
        entry.Property(row => row.UpdatedAt).CurrentValue = close.At;
    }
}
