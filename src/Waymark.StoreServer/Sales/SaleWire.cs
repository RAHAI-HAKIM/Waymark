using Waymark.Application.Sales;
using Waymark.Contracts.Pos;
using Waymark.StoreServer.Security;

namespace Waymark.StoreServer.Sales;

/// <summary>The sale's answer as the till reads it (D-070). Pure, so it is tested without a server.</summary>
public static class SaleWire
{
    /// <summary>The sale to complete, sold by <paramref name="sellerId"/>: the session's person, never the till's say-so.</summary>
    public static CompleteSale ToCommand(SaleRequest request, string sellerId) => new(
        request.TerminalId,
        sellerId,
        [.. request.Lines.Select(line => new SaleLineRequest(line.Barcode, line.Count))]);

    /// <summary>
    /// Who is selling (A5, D-083): the person the session signed in, when the session is this till's.
    /// Null — sell nothing — when there is no session, or it was opened at another till: a token
    /// copied from one till must not sell at the next.
    /// </summary>
    public static string? Seller(SignedInTill? session, SaleRequest request) =>
        session is not null && string.Equals(session.TerminalId, request.TerminalId, StringComparison.Ordinal)
            ? session.StaffId
            : null;

    public static SaleOutcome NotSignedIn() => new(
        SaleOutcomes.NotSignedIn, null, null, null, null, null, null,
        "Nobody is signed in at this till: the session ended or was never opened. Sign in again.");

    public static SaleOutcome Completed(CompletedSale sale) => new(
        SaleOutcomes.Completed,
        sale.TransactionId,
        sale.InvoiceNumber,
        WireText.Figure(sale.Total),
        WireText.Figure(sale.TaxTotal),
        WireText.Figure(sale.Cash.Tendered),
        sale.Total.Currency.Code,
        Reason: null);

    public static SaleOutcome Refused(string reason) =>
        new(SaleOutcomes.Refused, null, null, null, null, null, null, reason);
}
