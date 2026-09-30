using Waymark.Application.Sales;
using Waymark.Contracts.Pos;
using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
using Waymark.StoreServer.Security;

namespace Waymark.StoreServer.Sales;

/// <summary>The sale's answer as the till reads it (D-070). Pure, so it is tested without a server.</summary>
public static class SaleWire
{
    /// <summary>The sale to complete, sold by <paramref name="sellerId"/>: the session's person, never the till's say-so.</summary>
    /// <param name="authorisedBy">
    /// Who gave an authorisation the sale cites, in this session, for a discount; null when nobody
    /// did. A discount citing nothing valid reaches the handler with nobody, and is refused there.
    /// </param>
    /// <param name="overriddenBy">The same for a price override (B5), rank 3's authorisation.</param>
    /// <param name="tabOverrideBy">The same for a tab part let past its limit (B7), <c>ManageCredit</c>'s authorisation.</param>
    public static CompleteSale ToCommand(
        SaleRequest request, string sellerId, Func<string?, string?>? authorisedBy = null, Func<string?, string?>? overriddenBy = null,
        Func<string?, string?>? tabOverrideBy = null) => new(
        request.TerminalId,
        sellerId,
        [.. request.Lines.Select(line => new SaleLineRequest(
            line.Barcode, line.Count, Weight(line.Weight), Discount(line.Discount, authorisedBy), Override(line.PriceOverride, overriddenBy)))],
        Discount(request.TicketDiscount, authorisedBy),
        request.Tenders is { Count: > 0 } tenders ? [.. tenders.Select(Tender)] : null,
        string.IsNullOrWhiteSpace(request.CustomerId) ? null : request.CustomerId,
        // Who let the tab part past the limit is the server's, from an authorisation of this session (B7).
        tabOverrideBy?.Invoke(request.TabOverride));

    /// <summary>
    /// A card or BaridiMob part as the handler reads it (B6). A method it does not know becomes cash,
    /// and an amount it cannot read becomes zero, which the rule refuses: the sale is refused, never
    /// written with a part missing.
    /// </summary>
    private static GivenTender Tender(TenderRequest tender) => new(
        tender.Method switch
        {
            TenderMethods.Card => PaymentMethod.Card,
            TenderMethods.MobileWallet => PaymentMethod.MobileWallet,
            TenderMethods.OnAccount => PaymentMethod.OnAccount,
            _ => PaymentMethod.Cash,
        },
        WireText.TryHundredths(tender.Amount, out var amount) ? amount : 0,
        tender.Reference);

    /// <summary>How the wire spells a payment row's method.</summary>
    private static string Method(PaymentMethod method) => method switch
    {
        PaymentMethod.Card => TenderMethods.Card,
        PaymentMethod.MobileWallet => TenderMethods.MobileWallet,
        PaymentMethod.Cash => TenderMethods.Cash,
        PaymentMethod.OnAccount => TenderMethods.OnAccount,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "A sale writes cash, card, wallet and tab rows only (B6, B7)."),
    };

    /// <summary>
    /// A price override as the handler reads it (B5): the new price in minor units. A price that
    /// cannot be read becomes zero, which the band refuses: the sale is refused, never sold at the old price.
    /// </summary>
    private static GivenOverride? Override(PriceOverrideRequest? given, Func<string?, string?>? overriddenBy) => given is null
        ? null
        : new GivenOverride(
            WireText.TryHundredths(given.Price, out var price) ? price : 0,
            given.ReasonCode,
            overriddenBy?.Invoke(given.Authorisation) ?? string.Empty);

    /// <summary>
    /// A discount as the handler reads it (B4): a percent in basis points, an amount in minor units.
    /// A form or a value that cannot be read becomes a value of zero, which the rules refuse: the
    /// sale is refused with its reason, never sold without the discount the customer was promised.
    /// </summary>
    private static GivenDiscount? Discount(DiscountRequest? discount, Func<string?, string?>? authorisedBy) => discount is null
        ? null
        : new GivenDiscount(
            discount.Form == DiscountForms.Amount ? DiscountForm.Amount : DiscountForm.Percent,
            discount.Form is DiscountForms.Amount or DiscountForms.Percent && WireText.TryHundredths(discount.Value, out var hundredths) ? hundredths : 0,
            discount.ReasonCode,
            authorisedBy?.Invoke(discount.Authorisation) ?? string.Empty,
            string.IsNullOrWhiteSpace(discount.Note) ? null : discount.Note.Trim());

    /// <summary>
    /// A typed weight as thousandths. Text that is not a weight becomes zero, which the lookup
    /// refuses as <c>weight_invalid</c>: the sale is refused with its reason, never sold by count.
    /// </summary>
    private static long? Weight(string? text) =>
        text is null ? null : WireText.TryThousandths(text, out var thousandths) ? thousandths : 0;

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
        Reason: null,
        [.. sale.Payments.Select(payment => new PaymentLine(Method(payment.Method), WireText.Figure(payment.Amount), payment.Reference))]);

    public static SaleOutcome Refused(string reason) =>
        new(SaleOutcomes.Refused, null, null, null, null, null, null, reason);
}
