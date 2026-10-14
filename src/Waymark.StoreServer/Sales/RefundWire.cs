using Waymark.Application.Sales;
using Waymark.Contracts.Pos;
using Waymark.Domain.Organisation;
using Waymark.Domain.Sales;

namespace Waymark.StoreServer.Sales;

/// <summary>A refund's request and answer as the till reads them (B9, D-098). Pure, so it is tested without a server.</summary>
public static class RefundWire
{
    /// <summary>
    /// The refund asked by <paramref name="sellerId"/>, the session's person. A quantity or a price it
    /// cannot read becomes zero or a price no line has, which the handler refuses: a refund is refused,
    /// never written with a line missing. A destination it does not know is cash.
    /// </summary>
    /// <param name="authorisedBy">Who gave the cited authorisation, resolved in this session; null when none or not this session's.</param>
    /// <param name="mayPassWindow">The seller is the owner, or an owner's PIN was cited for the window (D-125).</param>
    public static RefundSale ToCommand(
        RefundRequest request, string sellerId, bool sellerMayRefund, Func<string?, string?>? authorisedBy = null, bool mayPassWindow = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new RefundSale(
            request.TerminalId,
            sellerId,
            request.Original,
            [.. request.Lines.Select(line => new ReturnedLine(
                line.VariantId,
                WireText.TryHundredths(line.UnitPrice, out var price) ? price : -1,
                WireText.TryThousandths(line.Quantity, out var quantity) ? quantity : 0,
                line.Restock))],
            request.ReasonCode,
            request.Note,
            request.RefundTo == RefundDestinations.StoreCredit ? RefundTo.StoreCredit : RefundTo.Cash,
            request.CustomerId,
            sellerMayRefund,
            authorisedBy?.Invoke(request.Authorisation),
            request.Quote,
            mayPassWindow);
    }

    /// <summary>
    /// Whether the seller may refund past the shop's window (D-125): the owner's rank, or an owner's
    /// PIN cited for <see cref="Capability.RefundPastWindow"/>, resolved in this session.
    /// </summary>
    public static bool MayPassWindow(long? rank, string? ownerAuthorisedBy) =>
        StaffPermissions.May(rank, Capability.RefundPastWindow) || ownerAuthorisedBy is not null;

    /// <summary>
    /// Whether the refund is answered "pin_required" for the owner before anything is worked out
    /// (D-125): a refund, not a quote, of a sale past the window, by somebody who may not pass it.
    /// The handler refuses the same refund again if it is ever sent; this is what makes it a PIN step.
    /// </summary>
    public static bool AsksOwner(bool quote, bool mayPass, DateOnly soldOn, DateOnly today, int? windowDays) =>
        !quote && !mayPass && !Refunds.WithinWindow(soldOn, today, windowDays);

    public static RefundAnswer Answered(RefundedSale refund)
    {
        ArgumentNullException.ThrowIfNull(refund);
        var credit = refund.RestTo == RefundTo.StoreCredit;
        return new RefundAnswer(
            refund.TransactionId.Length == 0 ? RefundOutcomes.Quoted : RefundOutcomes.Refunded,
            refund.TransactionId.Length == 0 ? null : refund.TransactionId,
            refund.InvoiceNumber.Length == 0 ? null : refund.InvoiceNumber,
            WireText.Figure(refund.Total),
            WireText.Figure(refund.ToTab),
            WireText.Figure(refund.Rest),
            credit ? RefundDestinations.StoreCredit : RefundDestinations.Cash,
            // What comes out of the drawer, as a positive figure: the cash rest rounded once (D-034).
            credit ? null : WireText.Figure(-refund.Cash.Tendered),
            refund.CreditBalance is { } balance ? WireText.Figure(balance) : null,
            refund.MayCredit,
            refund.Total.Currency.Code,
            null,
            refund.CustomerOnTicket,
            WireText.Figure(refund.ToCredit ?? Domain.Values.Money.Zero(refund.Total.Currency)));
    }

    public static RefundAnswer Refused(string outcome, string? reason = null, Refusal? refusal = null) =>
        new(outcome, Reason: reason, Refusal: refusal);
}
