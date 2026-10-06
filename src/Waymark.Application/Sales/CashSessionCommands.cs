using Waymark.Application.Commands;
using Waymark.Domain;
using Waymark.Domain.Organisation;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.Domain.Work;
using RefusalCodes = Waymark.Contracts.Pos.RefusalCodes;

namespace Waymark.Application.Sales;

/// <summary>A cash session not opened or not closed, for a reason the cashier is told. Nothing is written.</summary>
/// <param name="code">One of <c>RefusalCodes</c> when a cashier meets this refusal in ordinary work (D-107); null otherwise.</param>
public sealed class CashSessionRefusedException(string reason, string? code = null) : Exception(reason)
{
    public string? Code { get; } = code;
}

/// <summary>The drawer counted and the till opened for the day (C1, D-111).</summary>
/// <param name="OpeningFloat">What was counted into the drawer, minor units, zero or more.</param>
public sealed record OpenCashSession(string TerminalId, string StaffId, long OpeningFloat) : ICommand<OpenedCashSession>;

public sealed record OpenedCashSession(string SessionId, DateTimeOffset OpenedAt, Money OpeningFloat);

/// <summary>
/// Opens the terminal's cash session with its counted float (C1, D-111), replacing the session a first
/// sale used to open with nothing in it (D-070). Anyone signed in opens: who did is on the row. A till
/// has one drawer, so a second session is refused while one is open, here and by
/// <c>ux_cash_sessions_one_open</c>.
/// </summary>
public sealed class OpenCashSessionHandler(ISalesLedger ledger, IStaging staging, TimeProvider clock)
    : ICommandHandler<OpenCashSession, OpenedCashSession>
{
    public async Task<OpenedCashSession> HandleAsync(OpenCashSession command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TerminalId);

        // An empty drawer is a float of zero, and a count; a negative one is nothing anybody counted.
        if (command.OpeningFloat < 0)
        {
            throw new CashSessionRefusedException("A float is zero or more.");
        }

        var store = await ledger.CurrentStoreAsync(cancellationToken)
            ?? throw new CashSessionRefusedException("This store has no row in stores; it is not commissioned.");
        if (await ledger.OpenCashSessionAsync(command.TerminalId, cancellationToken) is not null)
        {
            throw new CashSessionRefusedException("A cash session is already open on this till.", RefusalCodes.SessionAlreadyOpen);
        }

        var now = clock.GetUtcNow();
        var session = new CashSession
        {
            SessionId = context.NewId(),
            StoreId = store.StoreId,
            TerminalId = command.TerminalId,
            OpenedBy = command.StaffId,
            OpenedAt = now,
            OpeningFloat = Money.FromMinorUnits(command.OpeningFloat, Currency.FromCode(store.Currency)),
            CreatedAt = now,
            UpdatedAt = now,
        };
        staging.Add(session);
        return new OpenedCashSession(session.SessionId, now, session.OpeningFloat);
    }
}

/// <summary>The drawer counted at the end and the session closed (C1, D-111).</summary>
/// <param name="Counted">What is in the drawer, the float included, minor units, zero or more.</param>
/// <param name="Note">Asked when the variance passes the tenant's threshold; kept whenever written.</param>
/// <param name="CloserMay">Who counted reaches <c>CloseSession</c> as the tenant set it, as the host asked <c>StaffPermissions</c>.</param>
/// <param name="AuthorisedBy">The person whose PIN the till cited, resolved by the host; null when none.</param>
public sealed record CloseCashSession(string TerminalId, string StaffId, long Counted, string? Note, bool CloserMay, string? AuthorisedBy)
    : ICommand<ClosedCashSession>;

/// <param name="NoteWasNeeded">The variance passed the tenant's threshold: the Z flags it (C2).</param>
public sealed record ClosedCashSession(
    string SessionId, long ZReportNumber, DateTimeOffset ClosedAt, Money Expected, Money Counted, Money Variance, bool NoteWasNeeded,
    string ClosedBy, string? AuthorisedBy);

/// <summary>
/// Closes the terminal's cash session (C1, D-111): what the drawer should hold is worked out from the
/// session's own rows (<see cref="Drawer.Expected"/>), the count is set against it, and the session
/// takes the till's next Z number. <b>A variance is recorded, never refused</b>, as a cancel is
/// (D-097): past the tenant's threshold it needs a note, and that is all. A closed session is never
/// written again (<c>trg_cash_sessions_closed_final</c>).
/// </summary>
public sealed class CloseCashSessionHandler(
    ISalesLedger ledger, ICashSessionLedger sessions, ITenantConfiguration configuration, TimeProvider clock)
    : ICommandHandler<CloseCashSession, ClosedCashSession>
{
    public async Task<ClosedCashSession> HandleAsync(CloseCashSession command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TerminalId);

        if (command.Counted < 0)
        {
            throw new CashSessionRefusedException("A count is zero or more.");
        }

        var session = await ledger.OpenCashSessionAsync(command.TerminalId, cancellationToken)
            ?? throw new CashSessionRefusedException(CashSessions.NoneOpen, RefusalCodes.NoOpenSession);

        var expected = Drawer.Expected(await sessions.MovementsAsync(session, cancellationToken));
        var counted = Money.FromMinorUnits(command.Counted, expected.Currency);
        var variance = Drawer.Variance(expected, counted);
        var note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        var noteNeeded = Drawer.NeedsNote(variance, (await configuration.CurrentAsync(cancellationToken)).VarianceAlertValue);
        if (noteNeeded && note is null)
        {
            // It names nothing: how far the count is from the drawer is not said to who counted blind.
            throw new CashSessionRefusedException("The count is too far from what the drawer should hold to close without a note.", RefusalCodes.CloseNeedsNote);
        }

        // The note before the PIN, as the till asks them: a manager validates a count that already
        // explains itself.
        if (!command.CloserMay && string.IsNullOrWhiteSpace(command.AuthorisedBy))
        {
            throw new CashSessionRefusedException("This shop asks for a manager to close the cash session.", RefusalCodes.CloseNeedsManager);
        }

        // One command at a time on a store (StoreServer's gate), and ux_cash_sessions_z_number behind it.
        var number = (await sessions.LastZReportNumberAsync(command.TerminalId, cancellationToken) ?? 0) + 1;
        var now = clock.GetUtcNow();
        var authorisedBy = command.CloserMay ? null : command.AuthorisedBy;
        await sessions.StageCloseAsync(
            session.SessionId, new SessionClose(command.StaffId, authorisedBy, now, counted, expected, variance, number, note), cancellationToken);

        return new ClosedCashSession(session.SessionId, number, now, expected, counted, variance, noteNeeded, command.StaffId, authorisedBy);
    }
}
