using System.Globalization;
using Waymark.Application.Commands;
using Waymark.Domain;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.Domain.Work;

namespace Waymark.Application.Sales;

/// <summary>A cash movement refused, for a reason the cashier is told. Nothing is written.</summary>
public sealed class CashMovementRefusedException(string reason) : Exception(reason);

/// <summary>
/// Cash into the drawer or out of it with no sale behind it (B10, D-102): a paid-in or a paid-out, on
/// the terminal's open cash session, with its reason and, for a paid-out below the shop's rank, who
/// authorised it.
/// </summary>
/// <param name="Amount">Minor units, above zero.</param>
/// <param name="ReasonCode">An active <c>cash_movement</c> reason whose direction is this one, or either.</param>
/// <param name="SellerMay">For a paid-out, the seller's rank reaches <c>PaidOut</c> as the tenant raised it, as the host asked <c>StaffPermissions</c>.</param>
/// <param name="AuthorisedBy">The person whose PIN the till cited for a paid-out, resolved by the host; null when none.</param>
public sealed record RecordCashMovement(
    string TerminalId, string StaffId, CashDirection Direction, long Amount, string ReasonCode, string? Note, bool SellerMay, string? AuthorisedBy)
    : ICommand<RecordedCashMovement>;

public sealed record RecordedCashMovement(string MovementId, CashDirection Direction, Money Amount);

/// <summary>
/// Records a paid-in or a paid-out (B10, D-102): the session opened if there is none (D-070, until C1),
/// one <c>cash_movements</c> row, never a negative amount (the type says the direction). The Z-report
/// reads them (C2).
/// </summary>
public sealed class RecordCashMovementHandler(ISalesLedger ledger, IStaging staging, IReasonCodes reasons, TimeProvider clock)
    : ICommandHandler<RecordCashMovement, RecordedCashMovement>
{
    public async Task<RecordedCashMovement> HandleAsync(RecordCashMovement command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TerminalId);

        if (command.Direction == CashDirection.Out && !command.SellerMay && string.IsNullOrWhiteSpace(command.AuthorisedBy))
        {
            throw new CashMovementRefusedException("This shop asks for a manager to take cash out of the drawer.");
        }

        if (command.Amount <= 0)
        {
            throw new CashMovementRefusedException("An amount above zero.");
        }

        var reason = (await reasons.ForAsync(ReasonCodeAppliesTo.CashMovement, cancellationToken)).FirstOrDefault(r => r.Code == command.ReasonCode)
            ?? throw new CashMovementRefusedException($"'{command.ReasonCode}' is not a reason this shop gives for cash in or out.");
        if (reason.Direction is { } direction && direction != command.Direction)
        {
            throw new CashMovementRefusedException($"'{command.ReasonCode}' is a reason for cash {(direction == CashDirection.In ? "in" : "out")}, not {(command.Direction == CashDirection.In ? "in" : "out")}.");
        }

        if (reason.RequiresNote && string.IsNullOrWhiteSpace(command.Note))
        {
            throw new CashMovementRefusedException($"'{command.ReasonCode}' asks for a note, and none was written.");
        }

        var store = await ledger.CurrentStoreAsync(cancellationToken)
            ?? throw new CashMovementRefusedException("This store has no row in stores; it is not commissioned.");
        var now = clock.GetUtcNow();
        var session = await CashSessions.OpenAsync(ledger, staging, context, store, command.TerminalId, command.StaffId, now, cancellationToken);
        var amount = Money.FromMinorUnits(command.Amount, Currency.FromCode(store.Currency));

        var movement = new CashMovement
        {
            MovementId = context.NewId(),
            SessionId = session,
            MovementType = command.Direction == CashDirection.In ? CashMovementType.PaidIn : CashMovementType.PaidOut,
            Amount = amount,
            ReasonCode = command.ReasonCode,
            Note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim(),
            StaffId = command.StaffId,
            AuthorisedBy = command.Direction == CashDirection.Out && !command.SellerMay ? command.AuthorisedBy : null,
            OccurredAt = now,
        };
        staging.Add(movement);
        return new RecordedCashMovement(movement.MovementId, command.Direction, amount);
    }
}

/// <summary>A person clocking in or out at a till (B10, D-102); the host has checked their PIN.</summary>
public sealed record ToggleClock(string StaffId, string TerminalId) : ICommand<Clocked>;

/// <summary>What the clock did: in, or out after a shift that started at <paramref name="Since"/>.</summary>
public sealed record Clocked(bool In, DateTimeOffset At, DateTimeOffset? Since);

/// <summary>
/// The clock (B10, D-102): a person with no open shift at this store clocks in, one with an open shift
/// clocks out. Working time is not the till's sign-in: switching cashier opens and closes no shift.
/// </summary>
public sealed class ToggleClockHandler(ISalesLedger ledger, IShiftLedger shifts, IStaging staging, TimeProvider clock)
    : ICommandHandler<ToggleClock, Clocked>
{
    public async Task<Clocked> HandleAsync(ToggleClock command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        var store = await ledger.CurrentStoreAsync(cancellationToken)
            ?? throw new CashMovementRefusedException("This store has no row in stores; it is not commissioned.");
        var now = clock.GetUtcNow();

        if (await shifts.OpenShiftAsync(command.StaffId, cancellationToken) is { } open)
        {
            await shifts.StageCloseAsync(open.ShiftId, now, cancellationToken);
            return new Clocked(false, now, DateTimeOffset.ParseExact(open.StartTime, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
        }

        staging.Add(new Shift
        {
            ShiftId = context.NewId(),
            StaffId = command.StaffId,
            TerminalId = command.TerminalId,
            StoreId = store.StoreId,
            StartTime = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Status = ShiftStatus.Open,
            CreatedAt = now,
            UpdatedAt = now,
        });
        return new Clocked(true, now, null);
    }
}
