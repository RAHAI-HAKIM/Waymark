using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Domain.Sales;

/// <summary>
/// Sales already made, read back as the till showed them when they ended (session B1, D-088): the
/// "Tickets" list, and one ticket opened read-only in the ticket view.
///
/// <para>
/// A read, like the product lookup: it stages nothing. <b>Store scoping is the global filter</b>
/// (CLAUDE.md §3.3), so another store's sale is never listed nor found, whatever its id. Who may see
/// which day or which till is not this port's question: StoreServer asks
/// <c>StaffPermissions.May</c> before it asks this (D-088).
/// </para>
/// </summary>
public interface IPastTickets
{
    /// <summary>
    /// The finished sales that occurred in <c>[<paramref name="since"/>, <paramref name="until"/>)</c>,
    /// <b>newest first</b>. Finished means completed, voided, refunded or partially refunded; an
    /// open or parked transaction is not a ticket anybody can look back at.
    /// </summary>
    /// <param name="terminalId">One till's sales; null for every till of this store.</param>
    Task<IReadOnlyList<PastTicketSummary>> ListAsync(
        string? terminalId, DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken = default);

    /// <summary>
    /// One finished sale, found by its transaction id or by its invoice number ("S-2026-000142").
    /// Null when this store has no such finished sale.
    /// </summary>
    Task<PastTicket?> FindAsync(string idOrInvoiceNumber, CancellationToken cancellationToken = default);
}

/// <summary>A row of the "Tickets" list.</summary>
/// <param name="LineCount">How many lines the ticket showed: after the merge <see cref="PastTicket.Lines"/> describes, not how many rows the sale wrote.</param>
/// <param name="Total">What the ticket came to, as the sale's row recorded it.</param>
public sealed record PastTicketSummary(
    string TransactionId,
    string? InvoiceNumber,
    DateTimeOffset OccurredAt,
    string TerminalId,
    TransactionStatus Status,
    int LineCount,
    Money Total);

/// <summary>A finished sale, as the ticket view shows it.</summary>
/// <param name="StaffName">Who sold it; null if that person is no longer this store's.</param>
/// <param name="Lines">
/// <b>One line per product and price</b>, in the order the sale first wrote them. A sale writes one
/// <c>transaction_items</c> row per batch it took stock from (D-070), so one line of the ticket can
/// be several rows; read back one per row, the ticket would show two lines of milk where the cashier
/// scanned one.
/// </param>
/// <param name="Subtotal">As the sale's row recorded them, never recomputed: a receipt reads its own row (D-053).</param>
/// <param name="Payments">In the order they were taken.</param>
public sealed record PastTicket(
    string TransactionId,
    string? InvoiceNumber,
    DateTimeOffset OccurredAt,
    string TerminalId,
    string StaffId,
    string? StaffName,
    TransactionStatus Status,
    IReadOnlyList<PastTicketLine> Lines,
    Money Subtotal,
    Money TaxTotal,
    Money Total,
    IReadOnlyList<PastPayment> Payments);

/// <summary>A line of a past ticket. The names are the catalogue's today: a sale's rows keep no name (D-088).</summary>
/// <param name="Quantity">In the line's selling unit.</param>
/// <param name="UnitPrice">The price each unit sold at.</param>
public sealed record PastTicketLine(
    string VariantId,
    string ProductName,
    string VariantName,
    Quantity Quantity,
    Money UnitPrice,
    Money TaxAmount,
    Money LineTotal);

/// <summary>One payment the sale took.</summary>
public sealed record PastPayment(PaymentMethod Method, Money Amount);
