using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>
/// A refund linked to its sale (B9, D-098): which lines of the ticket come back, why, and where the
/// money goes. The server works out what each line gives back from what was paid; the till never does.
/// </summary>
/// <param name="Original">The sale's transaction id, as the ticket view opened it.</param>
/// <param name="ReasonCode">An active <c>return</c> reason.</param>
/// <param name="Note">For a reason that asks for one; null otherwise.</param>
/// <param name="RefundTo">One of <see cref="RefundDestinations"/>: where what does not go back on the tab is paid.</param>
/// <param name="CustomerId">The customer store credit goes to when the ticket had none; null otherwise.</param>
/// <param name="Authorisation">What <c>/api/till/authorise</c> answered for <see cref="Capabilities.Refund"/>, when one was needed.</param>
/// <param name="Quote">True to be told what the refund would come to, writing nothing.</param>
public sealed record RefundRequest(
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("original")] string Original,
    [property: JsonPropertyName("lines")] IReadOnlyList<RefundLineRequest> Lines,
    [property: JsonPropertyName("reason_code")] string ReasonCode,
    [property: JsonPropertyName("refund_to")] string RefundTo,
    [property: JsonPropertyName("note")] string? Note = null,
    [property: JsonPropertyName("customer_id")] string? CustomerId = null,
    [property: JsonPropertyName("authorisation")] string? Authorisation = null,
    [property: JsonPropertyName("quote")] bool Quote = false,
    [property: JsonPropertyName("ticket_authorisation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TicketAuthorisation = null);

/// <summary>A line of the ticket brought back: one line per product and price, as the ticket showed it (D-088).</summary>
/// <param name="UnitPrice">Exact decimal text, "100.00": the price each unit sold at.</param>
/// <param name="Quantity">Exact decimal text in the line's unit: "2", or "1.240" for a weighed line, which comes back whole.</param>
/// <param name="Restock">"Remis en rayon": back on the shelf, unless its batch has expired.</param>
public sealed record RefundLineRequest(
    [property: JsonPropertyName("variant_id")] string VariantId,
    [property: JsonPropertyName("unit_price")] string UnitPrice,
    [property: JsonPropertyName("quantity")] string Quantity,
    [property: JsonPropertyName("restock")] bool Restock);

/// <summary>The refund written or quoted, or why not. Every amount is exact decimal text and positive: what goes back.</summary>
/// <param name="Outcome">One of <see cref="RefundOutcomes"/>.</param>
/// <param name="InvoiceNumber">The refund's own number; null for a quote.</param>
/// <param name="Total">What the refund gives back.</param>
/// <param name="ToTab">What of it goes back on the customer's tab (D-055); "0.00" when none.</param>
/// <param name="Rest">What is paid in cash or as store credit.</param>
/// <param name="CashOut">For cash, what comes out of the drawer: the rest rounded once to the cash step.</param>
/// <param name="CreditBalance">For store credit, the customer's balance after it.</param>
/// <param name="MayCredit">Whether store credit may be offered: the customer module is on (D-096).</param>
/// <param name="ToCredit">What the sale paid in store credit and comes back as store credit first (B9b); "0.00" when none.</param>
/// <param name="CustomerOnTicket">The sale, or the refund, has its customer, never named here: without one, store credit waits for one attached.</param>
public sealed record RefundAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("transaction_id")] string? TransactionId = null,
    [property: JsonPropertyName("invoice_number")] string? InvoiceNumber = null,
    [property: JsonPropertyName("total")] string? Total = null,
    [property: JsonPropertyName("to_tab")] string? ToTab = null,
    [property: JsonPropertyName("rest")] string? Rest = null,
    [property: JsonPropertyName("refund_to")] string? RefundTo = null,
    [property: JsonPropertyName("cash_out")] string? CashOut = null,
    [property: JsonPropertyName("credit_balance")] string? CreditBalance = null,
    [property: JsonPropertyName("may_credit")] bool MayCredit = false,
    [property: JsonPropertyName("currency")] string? Currency = null,
    [property: JsonPropertyName("reason")] string? Reason = null,
    [property: JsonPropertyName("customer_on_ticket")] bool CustomerOnTicket = false,
    [property: JsonPropertyName("to_credit")] string? ToCredit = null,
    [property: JsonPropertyName("refusal"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Refusal? Refusal = null);

/// <summary>The values of <see cref="RefundAnswer.Outcome"/>.</summary>
public static class RefundOutcomes
{
    public const string Refunded = "refunded";

    /// <summary>What the refund would come to; nothing was written.</summary>
    public const string Quoted = "quoted";

    public const string Refused = "refused";

    public const string NotSignedIn = "not_signed_in";

    /// <summary>The seller's rank is below the shop's <c>refund_min_rank</c>: a PIN, then send it again with the authorisation.</summary>
    public const string PinRequired = "pin_required";

    /// <summary>Another day's or another till's ticket, and the person's rank does not reach 2 (D-088).</summary>
    public const string NotAllowed = "not_allowed";
}

/// <summary>The values of <see cref="RefundRequest.RefundTo"/>.</summary>
public static class RefundDestinations
{
    public const string Cash = "cash";

    public const string StoreCredit = "store_credit";
}
