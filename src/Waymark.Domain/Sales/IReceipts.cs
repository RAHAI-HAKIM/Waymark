using Waymark.Domain.Values;

namespace Waymark.Domain.Sales;

/// <summary>
/// What a receipt prints, read from the ticket's own rows (session D1, D-125): never worked out
/// again, so the paper and the row cannot disagree (D-053). A read: it stages nothing. Store scoping
/// is the global filter (CLAUDE.md §3.3). Who may read which ticket is StoreServer's question, the
/// same as for the ticket itself (D-088, D-109).
/// </summary>
public interface IReceipts
{
    /// <summary>
    /// A sale or a refund that has its number: completed, refunded or partially refunded. Null for
    /// anything else, a cancelled ticket among them, which prints nothing (D-124).
    /// </summary>
    Task<ReceiptFacts?> FindAsync(string idOrInvoiceNumber, CancellationToken cancellationToken = default);

    /// <summary>The store's own lines, for a paper with no ticket behind it: a tab repayment's slip. Null when the store has no row.</summary>
    Task<ReceiptStore?> StoreAsync(CancellationToken cancellationToken = default);
}

/// <summary>The store as its receipts name it.</summary>
/// <param name="Address">As the store wrote it; a line break starts a new line on the paper.</param>
/// <param name="TaxNumber">The NIF, <c>stores.tax_registration_number</c>; null prints nothing.</param>
/// <param name="Footer">The store's own text (D-125); null prints nothing.</param>
/// <param name="TimeZone">The store's zone: the paper's day and hour are the store's.</param>
public sealed record ReceiptStore(string Name, string? Address, string? Phone, string? TaxNumber, string? Footer, string TimeZone);

/// <summary>A ticket as its receipt prints it.</summary>
/// <param name="StaffName">Who sold or refunded it; null if that person is no longer this store's.</param>
/// <param name="Lines">One per product and price, as the ticket view has them (D-088). A refund's are below zero.</param>
/// <param name="Subtotal">The row's, before tax: what "Total HT" prints.</param>
/// <param name="Payments">In the order they were taken. A refund's are below zero.</param>
/// <param name="CashRounding">What the cash step added to or took off the cash part (D-034); null when nothing.</param>
/// <param name="Original">For a refund, the sale it refunds.</param>
/// <param name="Reason">For a refund, its reason as the shop words it in French.</param>
public sealed record ReceiptFacts(
    ReceiptStore Store,
    string TransactionId,
    string InvoiceNumber,
    DateTimeOffset OccurredAt,
    string TerminalId,
    string TerminalName,
    string? StaffName,
    IReadOnlyList<ReceiptItem> Lines,
    Money Subtotal,
    Money TaxTotal,
    Money Total,
    Money DiscountTotal,
    IReadOnlyList<PastPayment> Payments,
    Money? CashRounding,
    long ReprintCount,
    ReceiptOriginal? Original = null,
    string? Reason = null);

/// <summary>A line of a receipt.</summary>
/// <param name="Discount">What its rows took off, as they recorded it: above zero for a sale and for a refund alike.</param>
/// <param name="LineTotal">What the line came to after its discount, as its rows recorded it.</param>
/// <param name="Weighed">Sold by weight: printed with its weight and price a kilo, and counted one article.</param>
public sealed record ReceiptItem(
    string ProductName,
    string VariantName,
    Quantity Quantity,
    Money UnitPrice,
    Money Discount,
    Money LineTotal,
    bool Weighed);

/// <summary>The sale a refund refunds, as the refund's receipt names it.</summary>
public sealed record ReceiptOriginal(string InvoiceNumber, DateTimeOffset OccurredAt, string TerminalName);
