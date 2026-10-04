using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>
/// The till asking StoreServer to complete a cash sale (hop 2, D-070). It sends codes and
/// counts, never prices: the server prices every line again by the scan's own rules, so a
/// price that changed since the scan is caught, not sold.
/// </summary>
/// <param name="TerminalId">The till selling.</param>
/// <param name="Lines">What was scanned, one line per code.</param>
/// <remarks>
/// <b>It does not say who is selling</b> (D-083). The till sends its session token in
/// <see cref="TillSessionHeader.Name"/>, and the server takes the seller from the session that
/// token opened: a field here would be a claim the server had to decide whether to believe.
/// </remarks>
/// <param name="TicketDiscount">A discount given on the whole ticket at the counter (B4, D-091), or null.</param>
/// <param name="CustomerId">The customer the ticket is recorded against (B7, D-096); needed for a tab part. Left out when there is none.</param>
/// <param name="TabOverride">An authorisation for <see cref="Capabilities.ManageCredit"/> letting the tab part past the limit (B7); left out when none.</param>
/// <param name="Tenders">
/// The card and BaridiMob parts (B6, D-095), in the order they were added: what was given, never a
/// price. Cash is never a part: it is whatever the server finds left. Null, and left out of the JSON,
/// when the whole ticket is cash.
/// </param>
/// <param name="PaymentOpenedAt">
/// When "Encaisser" was first opened on this ticket (D-106): a line struck after it by someone who
/// may not cancel alone cites a manager's authorisation. Null, and left out, when no line was struck.
/// </param>
public sealed record SaleRequest(
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("lines")] IReadOnlyList<SaleRequestLine> Lines,
    [property: JsonPropertyName("ticket_discount")] DiscountRequest? TicketDiscount = null,
    [property: JsonPropertyName("tenders"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<TenderRequest>? Tenders = null,
    [property: JsonPropertyName("customer_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CustomerId = null,
    [property: JsonPropertyName("tab_override"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TabOverride = null,
    [property: JsonPropertyName("removed"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RemovedLineRequest>? Removed = null,
    [property: JsonPropertyName("payment_opened_at"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? PaymentOpenedAt = null);

/// <summary>A line struck on the ticket before it was paid or cancelled (B8, D-097): recorded, never charged.</summary>
/// <param name="Weight">A typed weight, as on <see cref="SaleRequestLine"/>; null for a count or a label.</param>
/// <param name="RemovedAt">When it was struck, as the till's clock said.</param>
/// <param name="Authorisation">
/// What <c>/api/till/authorise</c> answered for <see cref="Capabilities.VoidTransaction"/> when the
/// line was struck after "Encaisser" (D-106); left out otherwise.
/// </param>
public sealed record RemovedLineRequest(
    [property: JsonPropertyName("barcode")] string Barcode,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("weight")] string? Weight,
    [property: JsonPropertyName("removed_at")] DateTimeOffset RemovedAt,
    [property: JsonPropertyName("authorisation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Authorisation = null);

/// <summary>
/// A ticket cancelled at the till (B8, D-097): its lines as a sale would send them, why, and whether
/// the payment panel had been opened on it. The server records it priced, as voided; nothing is sold.
/// </summary>
/// <param name="ReasonCode">An active <c>void</c> reason.</param>
/// <param name="PaymentOpenedAt">When "Encaisser" was opened on this ticket; null when it never was.</param>
/// <param name="Authorisation">What <c>/api/till/authorise</c> answered for <see cref="Capabilities.VoidTransaction"/>, when one was needed.</param>
public sealed record VoidRequest(
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("lines")] IReadOnlyList<SaleRequestLine> Lines,
    [property: JsonPropertyName("reason_code")] string ReasonCode,
    [property: JsonPropertyName("ticket_discount")] DiscountRequest? TicketDiscount = null,
    [property: JsonPropertyName("payment_opened_at")] DateTimeOffset? PaymentOpenedAt = null,
    [property: JsonPropertyName("authorisation")] string? Authorisation = null,
    [property: JsonPropertyName("removed")] IReadOnlyList<RemovedLineRequest>? Removed = null);

/// <summary>A cancel recorded, or why not.</summary>
/// <param name="Outcome">"voided", "refused", "not_signed_in", or "pin_required" when a manager must authorise it first.</param>
/// <param name="Refusal">Why, as a code the till says in its own language (D-107); null when the refusal has none.</param>
public sealed record VoidAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("transaction_id")] string? TransactionId,
    [property: JsonPropertyName("total")] string? Total,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("refusal"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Refusal? Refusal = null);

/// <summary>The values of <see cref="VoidAnswer.Outcome"/>.</summary>
public static class VoidOutcomes
{
    public const string Voided = "voided";

    public const string Refused = "refused";

    public const string NotSignedIn = "not_signed_in";

    /// <summary>A cashier's cancel after "Encaisser": a manager's PIN, then send it again with the authorisation.</summary>
    public const string PinRequired = "pin_required";
}

/// <summary>A part of the ticket paid by card or BaridiMob (B6).</summary>
/// <param name="Method">One of <see cref="TenderMethods"/>.</param>
/// <param name="Amount">Exact decimal text, "2000.00": what the terminal or the phone took.</param>
/// <param name="Reference">The authorisation number or transfer id, optional; never a card number.</param>
public sealed record TenderRequest(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("amount")] string Amount,
    [property: JsonPropertyName("reference")] string? Reference = null);

/// <summary>A row of <c>transaction_payments</c> as the till shows it once the sale is written (B6).</summary>
/// <param name="Method">One of <see cref="TenderMethods"/>, or <c>cash</c>.</param>
/// <param name="Amount">Exact decimal text. For cash, the exact rest; what the drawer takes is <see cref="SaleOutcome.CashToCollect"/>.</param>
public sealed record PaymentLine(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("amount")] string Amount,
    [property: JsonPropertyName("reference")] string? Reference);

/// <summary>The payment methods as the wire spells them: <c>transaction_payments.payment_method</c>'s values.</summary>
public static class TenderMethods
{
    public const string Cash = "cash";

    public const string Card = "card";

    /// <summary>BaridiMob, and any transfer made from a phone.</summary>
    public const string MobileWallet = "mobile_wallet";

    /// <summary>On the customer's tab, le carnet (B7).</summary>
    public const string OnAccount = "on_account";

    /// <summary>The customer's store credit, l'avoir (B9b).</summary>
    public const string StoreCredit = "store_credit";
}

/// <summary>One scanned code and how many units of it.</summary>
/// <param name="Weight">
/// For a product sold by weight whose weight was typed (B3): the weight in its selling unit, as
/// exact decimal text, "0.556", and <paramref name="Count"/> is 1. Null for a count, and for a scale
/// label, whose code carries its own weight or price and is read again by the server (D-090).
/// </param>
/// <param name="Discount">A discount given on this line at the counter (B4), or null.</param>
/// <param name="PriceOverride">
/// A unit price typed in place of the price in force (B5), or null, and then left out of the JSON: a
/// sale sends no price unless one was typed at the counter, and the server checks that one again (D-092).
/// </param>
public sealed record SaleRequestLine(
    [property: JsonPropertyName("barcode")] string Barcode,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("weight")] string? Weight = null,
    [property: JsonPropertyName("discount")] DiscountRequest? Discount = null,
    [property: JsonPropertyName("price_override"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PriceOverrideRequest? PriceOverride = null);

/// <summary>
/// StoreServer's answer to a sale. Like the lookup (D-066), a refusal is an answer, a 200
/// with its reason; an HTTP error means only that the server failed.
/// </summary>
/// <param name="Outcome">One of <see cref="SaleOutcomes"/>.</param>
/// <param name="TransactionId">The sale's row, when completed.</param>
/// <param name="InvoiceNumber">When completed.</param>
/// <param name="TotalTtc">The exact TTC total, as exact decimal text.</param>
/// <param name="TaxTotal">The TVA in it.</param>
/// <param name="CashToCollect">The total rounded to the cash step: what the customer hands over (D-034).</param>
/// <param name="Currency">The currency of the figures.</param>
/// <param name="Reason">Why the sale was refused, in words, when refused.</param>
/// <param name="Payments">The payment rows written, in order (B6); null when refused, and from a server before B6.</param>
/// <param name="Refusal">Why the sale was refused, as a code the till says in its own language (D-107); null when the refusal has none.</param>
public sealed record SaleOutcome(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("transaction_id")] string? TransactionId,
    [property: JsonPropertyName("invoice_number")] string? InvoiceNumber,
    [property: JsonPropertyName("total_ttc")] string? TotalTtc,
    [property: JsonPropertyName("tax_total")] string? TaxTotal,
    [property: JsonPropertyName("cash_to_collect")] string? CashToCollect,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("payments")] IReadOnlyList<PaymentLine>? Payments = null,
    [property: JsonPropertyName("refusal"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Refusal? Refusal = null);

/// <summary>The values of <see cref="SaleOutcome.Outcome"/>.</summary>
public static class SaleOutcomes
{
    /// <summary>Every row was written; the figures are set.</summary>
    public const string Completed = "completed";

    /// <summary>Nothing was written; <see cref="SaleOutcome.Reason"/> says why.</summary>
    public const string Refused = "refused";

    /// <summary>
    /// Nothing was written: the till's session is not one the server holds (never opened, signed
    /// out, or the server restarted), or it is another till's. The till asks for a PIN again.
    /// </summary>
    public const string NotSignedIn = "not_signed_in";
}
