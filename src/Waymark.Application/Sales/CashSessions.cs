using Waymark.Application.Commands;
using Waymark.Domain.Organisation;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.Domain.Work;

namespace Waymark.Application.Sales;

/// <summary>
/// The terminal's open cash session, or a new one opened by this staff member with no float (D-070,
/// provisional until C1). One place, because a sale and a tab repayment (B7) both put cash in the
/// drawer, and two copies of "open one if there is none" would one day open two.
/// </summary>
internal static class CashSessions
{
    public static async Task<string> OpenAsync(
        ISalesLedger ledger, IStaging staging, CommandContext context, Store store, string terminalId, string staffId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await ledger.OpenCashSessionAsync(terminalId, cancellationToken) is { } open)
        {
            return open.SessionId;
        }

        var session = new CashSession
        {
            SessionId = context.NewId(),
            StoreId = store.StoreId,
            TerminalId = terminalId,
            OpenedBy = staffId,
            OpenedAt = now,
            OpeningFloat = Money.Zero(Currency.FromCode(store.Currency)),
            CreatedAt = now,
            UpdatedAt = now,
        };
        staging.Add(session);
        return session.SessionId;
    }
}
