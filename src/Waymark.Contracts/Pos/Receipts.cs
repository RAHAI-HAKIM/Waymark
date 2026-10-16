using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>
/// What a receipt prints (session D1, D-125), asked with the session header like the ticket itself:
/// the same rule says who may read which (D-088, D-109). The till turns it into paper; the server
/// says only what the ticket's rows say.
/// </summary>
/// <param name="Outcome">One of <see cref="TicketOutcomes"/>: found, unknown, not signed in, a PIN required.</param>
public sealed record ReceiptAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("receipt")] ReceiptWire? Receipt);

/// <summary>A sale's or a refund's receipt.</summary>
/// <param name="Kind">One of <see cref="ReceiptKinds"/>.</param>
/// <param name="Day">The store's day it occurred, "dd/MM/yyyy", as the paper prints it.</param>
/// <param name="Time">The store's hour, "HH:mm".</param>
/// <param name="Cashier">Who sold or refunded it, first name only (D-125); null when they are no longer this store's.</param>
/// <param name="Subtotal">Before tax, the row's: printed as "Total HT" when the ticket carries TVA.</param>
/// <param name="TaxTotal">The row's. Zero: the ticket says nothing of tax (D-122).</param>
/// <param name="DiscountTotal">What the ticket's discounts came to, the row's.</param>
/// <param name="CashRounding">What the cash step added or took off (D-034), signed; null when nothing.</param>
/// <param name="Original">For a refund, the sale it refunds.</param>
/// <param name="Reason">For a refund, its reason in the shop's French.</param>
/// <param name="RefundWindowDays">The shop's window, printed in the signature; null for none (D-125).</param>
/// <param name="ReprintCount">How many times it was printed again (D-123).</param>
/// <param name="Language">"fr" or "ar" (D-124): Arabic is K2's, and prints French until then.</param>
public sealed record ReceiptWire(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("store")] ReceiptStoreWire Store,
    [property: JsonPropertyName("invoice_number")] string InvoiceNumber,
    [property: JsonPropertyName("day")] string Day,
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("terminal_name")] string TerminalName,
    [property: JsonPropertyName("cashier")] string? Cashier,
    [property: JsonPropertyName("lines")] IReadOnlyList<ReceiptLineWire> Lines,
    [property: JsonPropertyName("subtotal")] string Subtotal,
    [property: JsonPropertyName("tax_total")] string TaxTotal,
    [property: JsonPropertyName("total")] string Total,
    [property: JsonPropertyName("discount_total")] string DiscountTotal,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("payments")] IReadOnlyList<PastPaymentWire> Payments,
    [property: JsonPropertyName("cash_rounding")] string? CashRounding,
    [property: JsonPropertyName("original")] ReceiptOriginalWire? Original,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("refund_window_days")] int? RefundWindowDays,
    [property: JsonPropertyName("reprint_count")] long ReprintCount,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("reprint")] ReprintWire? Reprint = null);

/// <summary>The reprint this paper is (D3, D-127): the store's day and hour, and the first name of who asked. Null for the first paper.</summary>
public sealed record ReprintWire(
    [property: JsonPropertyName("day")] string Day,
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("by")] string? By);

/// <summary>A ticket printed again (D3): its number, and the manager's approval that opened it, when one did (D-109).</summary>
public sealed record ReprintRequest(
    [property: JsonPropertyName("ticket")] string Ticket,
    [property: JsonPropertyName("authorisation")] string? Authorisation = null);

/// <summary>The store as its receipts name it.</summary>
/// <param name="AddressLines">The address, a line each, as the store wrote it.</param>
/// <param name="TaxNumber">The NIF; null prints nothing.</param>
/// <param name="Footer">The store's own text (D-125); null prints nothing.</param>
public sealed record ReceiptStoreWire(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address_lines")] IReadOnlyList<string> AddressLines,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("tax_number")] string? TaxNumber,
    [property: JsonPropertyName("footer")] string? Footer);

/// <summary>A line of a receipt.</summary>
/// <param name="Name">The product and the variant, as the ticket view shows them.</param>
/// <param name="Quantity">Exact decimal text in the line's unit; below zero on a refund.</param>
/// <param name="Discount">What its rows took off, as recorded: above zero for a sale and a refund alike.</param>
/// <param name="LineTotal">After the discount, as recorded; below zero on a refund.</param>
/// <param name="Weighed">Sold by weight: its weight and price a kilo, and one article.</param>
public sealed record ReceiptLineWire(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("quantity")] string Quantity,
    [property: JsonPropertyName("unit_code")] string UnitCode,
    [property: JsonPropertyName("unit_price")] string UnitPrice,
    [property: JsonPropertyName("discount")] string Discount,
    [property: JsonPropertyName("line_total")] string LineTotal,
    [property: JsonPropertyName("weighed")] bool Weighed);

/// <summary>The sale a refund refunds: its number, its day and hour, its till.</summary>
public sealed record ReceiptOriginalWire(
    [property: JsonPropertyName("invoice_number")] string InvoiceNumber,
    [property: JsonPropertyName("day")] string Day,
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("terminal_name")] string TerminalName);

/// <summary>The store's lines alone, for a tab repayment's slip (D-125), which has no ticket.</summary>
public sealed record ReceiptStoreAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("store")] ReceiptStoreWire? Store);

/// <summary>The values of <see cref="ReceiptWire.Kind"/>.</summary>
public static class ReceiptKinds
{
    public const string Sale = "sale";

    public const string Refund = "refund";
}
