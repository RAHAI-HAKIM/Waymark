using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;
using Waymark.Domain.Values;
using Waymark.Generator.Catalogues;

namespace Waymark.Generator.Simulation;

/// <summary>
/// One till's drawer for one day: what it takes in cash, what goes in and out of it, and the
/// count at close (D-034).
///
/// <para>
/// <c>expected = float + Σcash payments + Σpaid_in − Σpaid_out − Σdrops + Σtender_variance</c>.
/// A cash payment row carries the invoice amount (negative for a refund); the drawer receives
/// the tender rounded to the cash step, and the difference is a <c>rounding_variance</c> row,
/// never part of the session's variance. The session's variance is only the miscount —
/// counted minus expected — which is what it exists to catch.
/// </para>
/// </summary>
internal sealed class CashDrawer
{
    private readonly SimulationContext _context;
    private readonly Money _float;
    private Money _cash;
    private Money _tenderVariance;
    private Money _paidIn;
    private Money _paidOut;
    private Money _drops;

    public CashDrawer(SimulationContext context, int terminal, GeneratedStaff openedBy)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        var currency = context.Store.Currency;
        Terminal = terminal;
        TerminalId = context.Store.TerminalIds[terminal];
        OpenedBy = openedBy;
        OpenedAt = context.Clock.GetUtcNow();
        SessionId = context.Ids.NewId();
        _float = new Money(checked((long)context.Config.Sales.OpeningFloat.Value * Currency.StorageScale), currency);
        _cash = _tenderVariance = _paidIn = _paidOut = _drops = Money.Zero(currency);
    }

    public int Terminal { get; }

    public string TerminalId { get; }

    public string SessionId { get; }

    public DateTimeOffset OpenedAt { get; }

    public GeneratedStaff OpenedBy { get; }

    /// <summary>What should physically be in the drawer now.</summary>
    public Money Expected => _float + _cash + _paidIn - _paidOut - _drops + _tenderVariance;

    /// <summary>
    /// A cash payment of <paramref name="exact"/> (negative for a refund): the drawer takes the
    /// tender, and any rounding is recorded against the transaction.
    /// </summary>
    public void TakeCash(Money exact, string transactionId)
    {
        var tender = exact.ToCashTender();
        if (Expected + tender.Tendered < Money.Zero(_context.Store.Currency))
        {
            // Loud rather than plausible: a drawer below zero is money that was never there.
            throw new InvalidOperationException(
                $"Terminal {TerminalId} would pay out {-tender.Tendered} holding only {Expected}; a refund was not checked against the drawer.");
        }

        _cash += exact;

        if (!tender.HasVariance)
        {
            return;
        }

        _tenderVariance += tender.Variance;
        var now = _context.Clock.GetUtcNow();
        _context.Database.Context.RoundingVariances.Add(new RoundingVariance
        {
            VarianceId = _context.Ids.NewId(),
            StoreId = _context.Store.StoreId,
            OccurredAt = now,
            ReferenceType = VarianceReferenceType.Transaction,
            ReferenceId = transactionId,
            Source = VarianceSource.CashTender,
            Amount = tender.Variance,
            Policy = _context.Store.RoundingPolicy,
            CreatedAt = now,
        });
    }

    /// <summary>
    /// A paid-in, paid-out or drop. Returns the cash movement's id, or null if refused: the drawer
    /// cannot cover money leaving it.
    /// </summary>
    public string? Move(CashMovementType type, Money amount, ReasonCodeDefinition reason, GeneratedStaff staff)
    {
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(staff);

        if (!amount.IsPositive || (type != CashMovementType.PaidIn && amount > Expected))
        {
            return null;
        }

        switch (type)
        {
            case CashMovementType.PaidIn:
                _paidIn += amount;
                break;
            case CashMovementType.PaidOut:
                _paidOut += amount;
                break;
            case CashMovementType.Drop:
                _drops += amount;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "The generator moves cash as paid_in, paid_out or drop.");
        }

        var movementId = _context.Ids.NewId();
        _context.Database.Context.CashMovements.Add(new CashMovement
        {
            MovementId = movementId,
            SessionId = SessionId,
            MovementType = type,
            Amount = amount,
            ReasonCode = reason.Code,
            Note = reason.RequiresNote ? $"{reason.LabelFr} (synthétique)" : null,
            StaffId = staff.StaffId,
            AuthorisedBy = _context.Authoriser(reason),
            OccurredAt = _context.Clock.GetUtcNow(),
        });

        return movementId;
    }

    /// <summary>
    /// Closes the session: counted is expected plus <paramref name="miscount"/> (zero for a clean
    /// count), and the session's variance is exactly that miscount.
    /// </summary>
    public void Close(GeneratedStaff closedBy, Money miscount, long zReportNumber)
    {
        ArgumentNullException.ThrowIfNull(closedBy);

        var closedAt = _context.Clock.GetUtcNow();
        var expected = Expected;

        _context.Database.Context.CashSessions.Add(new CashSession
        {
            SessionId = SessionId,
            StoreId = _context.Store.StoreId,
            TerminalId = TerminalId,
            OpenedBy = OpenedBy.StaffId,
            OpenedAt = OpenedAt,
            OpeningFloat = _float,
            ClosedBy = closedBy.StaffId,
            ClosedAt = closedAt,
            CountedCash = expected + miscount,
            ExpectedCash = expected,
            Variance = miscount,
            ZReportNumber = zReportNumber,
            Status = CashSessionStatus.Closed,
            CreatedAt = OpenedAt,
            UpdatedAt = closedAt,
        });
    }
}
