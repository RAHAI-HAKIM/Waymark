using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>
/// The "Tickets" list (session B1, D-088): the finished sales of one day, newest first. Asked with the
/// session header; another day or another till needs rank 2, and is otherwise refused as an answer.
/// </summary>
/// <param name="Outcome">One of <see cref="TicketOutcomes"/>.</param>
/// <param name="Day">The store's day listed, "yyyy-MM-dd".</param>
/// <param name="AllTills">Every till of the store, rather than the asking one.</param>
public sealed record TicketList(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("day")] string Day,
    [property: JsonPropertyName("all_tills")] bool AllTills,
    [property: JsonPropertyName("tickets")] IReadOnlyList<TicketSummary> Tickets);

/// <summary>A row of the list.</summary>
/// <param name="Status">"completed", "voided", "refunded" or "partially_refunded".</param>
public sealed record TicketSummary(
    [property: JsonPropertyName("transaction_id")] string TransactionId,
    [property: JsonPropertyName("invoice_number")] string? InvoiceNumber,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("line_count")] int LineCount,
    [property: JsonPropertyName("total")] string Total,
    [property: JsonPropertyName("currency")] string Currency);

/// <summary>One past ticket, to open read-only in the ticket view (D-088).</summary>
/// <param name="Outcome">One of <see cref="TicketOutcomes"/>.</param>
public sealed record TicketAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("ticket")] PastTicketDetail? Ticket);

/// <summary>A past ticket as it ended: its lines, its figures as recorded, its payments. No customer (D-088).</summary>
public sealed record PastTicketDetail(
    [property: JsonPropertyName("transaction_id")] string TransactionId,
    [property: JsonPropertyName("invoice_number")] string? InvoiceNumber,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("staff_name")] string? StaffName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("lines")] IReadOnlyList<PastTicketLineWire> Lines,
    [property: JsonPropertyName("subtotal")] string Subtotal,
    [property: JsonPropertyName("tax_total")] string TaxTotal,
    [property: JsonPropertyName("total")] string Total,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("payments")] IReadOnlyList<PastPaymentWire> Payments);

/// <summary>A line of a past ticket, with today's catalogue names.</summary>
/// <param name="Quantity">Exact decimal text in the line's unit.</param>
public sealed record PastTicketLineWire(
    [property: JsonPropertyName("product_name")] string ProductName,
    [property: JsonPropertyName("variant_name")] string VariantName,
    [property: JsonPropertyName("quantity")] string Quantity,
    [property: JsonPropertyName("unit_code")] string UnitCode,
    [property: JsonPropertyName("unit_price")] string UnitPrice,
    [property: JsonPropertyName("line_total")] string LineTotal);

/// <summary>A payment the sale took: "cash", "card", "mobile_wallet"...</summary>
public sealed record PastPaymentWire(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("amount")] string Amount);

/// <summary>The values of <see cref="TicketList.Outcome"/> and <see cref="TicketAnswer.Outcome"/>.</summary>
public static class TicketOutcomes
{
    public const string Answered = "answered";

    public const string Found = "found";

    /// <summary>This store has no finished sale with that id or number.</summary>
    public const string Unknown = "unknown";

    /// <summary>No session, or another till's: sign in again.</summary>
    public const string NotSignedIn = "not_signed_in";

    /// <summary>Another day or another till, and the person's rank does not reach 2 (D-088).</summary>
    public const string NotAllowed = "not_allowed";
}
