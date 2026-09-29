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
public sealed record SaleRequest(
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("lines")] IReadOnlyList<SaleRequestLine> Lines,
    [property: JsonPropertyName("ticket_discount")] DiscountRequest? TicketDiscount = null);

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
public sealed record SaleOutcome(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("transaction_id")] string? TransactionId,
    [property: JsonPropertyName("invoice_number")] string? InvoiceNumber,
    [property: JsonPropertyName("total_ttc")] string? TotalTtc,
    [property: JsonPropertyName("tax_total")] string? TaxTotal,
    [property: JsonPropertyName("cash_to_collect")] string? CashToCollect,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("reason")] string? Reason);

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
