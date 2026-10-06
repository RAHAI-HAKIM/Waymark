using Waymark.Domain.Sales;

namespace Waymark.Application.Sales;

/// <summary>
/// The terminal's open cash session, which everything that touches the drawer needs (C1, D-111): a
/// sale, a cancel, a refund, cash in or out, a tab's repayment. None of them opens one any more: a
/// session is opened by counting the float (<see cref="OpenCashSessionHandler"/>), and until then
/// each is refused with <c>RefusalCodes.NoOpenSession</c>. One place, so the five say it alike.
/// </summary>
internal static class CashSessions
{
    /// <summary>The refusal's words, for the logs and a till that does not know the code.</summary>
    public const string NoneOpen = "No cash session is open on this till: the drawer is counted and opened first.";

    /// <summary>The open session's id; null when the till has none, which the caller refuses in its own exception.</summary>
    public static async Task<string?> OpenIdAsync(ISalesLedger ledger, string terminalId, CancellationToken cancellationToken) =>
        (await ledger.OpenCashSessionAsync(terminalId, cancellationToken))?.SessionId;
}
