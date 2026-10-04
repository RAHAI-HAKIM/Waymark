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
        Func<string?, string?>? tabOverrideBy = null, Func<string?, string?>? struckBy = null, bool sellerMayVoid = true) => new(
        request.TerminalId,
        sellerId,
        [.. request.Lines.Select(line => new SaleLineRequest(
            line.Barcode, line.Count, Weight(line.Weight), Discount(line.Discount, authorisedBy), Override(line.PriceOverride, overriddenBy)))],
        Discount(request.TicketDiscount, authorisedBy),
        request.Tenders is { Count: > 0 } tenders ? [.. tenders.Select(Tender)] : null,
        string.IsNullOrWhiteSpace(request.CustomerId) ? null : request.CustomerId,
        // Who let the tab part past the limit is the server's, from an authorisation of this session (B7).
        tabOverrideBy?.Invoke(request.TabOverride),
        // Who let a line be struck after "Encaisser" is the server's too (D-106).
        Removed(request.Removed, struckBy),
        request.PaymentOpenedAt,
        sellerMayVoid);

    /// <summary>
    /// A cancel as the handler reads it (B8, D-097). Whether the seller may cancel alone is the
    /// server's (their rank, through <c>StaffPermissions</c>); who authorised it, from this session.
    /// </summary>
    public static VoidTicket ToVoid(
        VoidRequest request, string sellerId, bool sellerMayVoid, Func<string?, string?>? authorisedBy = null, Func<string?, string?>? discountBy = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new VoidTicket(
            request.TerminalId,
            sellerId,
            [.. request.Lines.Select(line => new SaleLineRequest(line.Barcode, line.Count, Weight(line.Weight), Discount(line.Discount, discountBy)))],
            Discount(request.TicketDiscount, discountBy),
            request.ReasonCode,
            sellerMayVoid,
            request.PaymentOpenedAt,
            authorisedBy?.Invoke(request.Authorisation),
            Removed(request.Removed, authorisedBy));
    }

    /// <summary>
    /// Struck lines as the handler reads them; a weight that cannot be read is zero, which the lookup
    /// refuses. Who let one be struck after "Encaisser" is resolved from what the till cites (D-106).
    /// </summary>
    private static IReadOnlyList<RemovedLine>? Removed(IReadOnlyList<RemovedLineRequest>? removed, Func<string?, string?>? struckBy) =>
        removed is { Count: > 0 }
            ? [.. removed.Select(line => new RemovedLine(
                line.Barcode, line.Count, Weight(line.Weight), line.RemovedAt, struckBy?.Invoke(line.Authorisation)))]
            : null;

    public static VoidAnswer Voided(VoidedTicket ticket) =>
        new(VoidOutcomes.Voided, ticket.TransactionId, WireText.Figure(ticket.Total), null);

    public static VoidAnswer VoidRefused(SaleRefusedException refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new VoidAnswer(VoidOutcomes.Refused, null, null, refusal.Message, Coded(refusal.Code, refusal.Args));
    }

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
            TenderMethods.StoreCredit => PaymentMethod.StoreCredit,
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
        PaymentMethod.StoreCredit => TenderMethods.StoreCredit,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "A sale writes cash, card, wallet, tab and store credit rows only (B6, B7, B9b)."),
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

    /// <summary>A refusal with its code, when it has one, for the till to say in its own language (D-107).</summary>
    public static SaleOutcome Refused(SaleRefusedException refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Refused(refusal.Message) with { Refusal = Coded(refusal.Code, refusal.Args) };
    }

    /// <summary>A code and what it names as the wire carries them; null with no code.</summary>
    public static Refusal? Coded(string? code, IReadOnlyList<string> args) =>
        code is null ? null : new Refusal(code, args is { Count: > 0 } ? args : null);
}
