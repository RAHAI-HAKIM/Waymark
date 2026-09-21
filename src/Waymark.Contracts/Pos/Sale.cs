using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>
/// The till asking StoreServer to complete a cash sale (hop 2, D-070). It sends codes and
/// counts, never prices: the server prices every line again by the scan's own rules, so a
/// price that changed since the scan is caught, not sold.
/// </summary>
/// <param name="TerminalId">The till selling.</param>
/// <param name="StaffId">Who is selling. No login until Phase 1; the till is configured with it.</param>
/// <param name="Lines">What was scanned, one line per code.</param>
public sealed record SaleRequest(
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("staff_id")] string StaffId,
    [property: JsonPropertyName("lines")] IReadOnlyList<SaleRequestLine> Lines);

/// <summary>One scanned code and how many units of it.</summary>
public sealed record SaleRequestLine(
    [property: JsonPropertyName("barcode")] string Barcode,
    [property: JsonPropertyName("count")] int Count);

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
}
