using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Domain.Sales;

/// <summary>
/// What a refund reads of the sale it refunds, and the one change it makes to that sale (B9, D-098).
/// Implemented in Persistence; the global filter scopes every read to the current store, so a sale of
/// another store is never found, whatever its id (Operating Rules: a cross-store refund is blocked).
/// </summary>
public interface IRefundLedger
{
    /// <summary>
    /// The sale, by its transaction id or its invoice number, with every row it sold and what has
    /// already come back of each. Null when this store has no such sale with a number.
    /// </summary>
    Task<SaleToRefund?> SaleAsync(string idOrInvoiceNumber, CancellationToken cancellationToken = default);

    /// <summary>Stages the sale's status after the refund, written when the executor commits (D-050).</summary>
    Task StageStatusAsync(string transactionId, TransactionStatus status, DateTimeOffset at, CancellationToken cancellationToken = default);
}

/// <summary>A sale as a refund reads it.</summary>
/// <param name="Sale">The sale's row, as recorded.</param>
/// <param name="Items">Every row it sold, in the order it wrote them; struck lines (B8) are not among them.</param>
/// <param name="TabPaid">What the sale put on the tab: its <c>on_account</c> payment, zero when none.</param>
/// <param name="TabRefunded">What earlier refunds of it took back off the tab, as a positive figure.</param>
/// <param name="CreditPaid">What the sale paid in store credit (B9b): its <c>store_credit</c> payment, zero when none.</param>
/// <param name="CreditRefunded">
/// What earlier refunds of it gave back as store credit, as a positive figure: the share given back as
/// credit and any rest the cashier chose to give as credit too, so the share can only be counted as used
/// sooner, never later, and store credit never comes back as cash.
/// </param>
public sealed record SaleToRefund(Transaction Sale, IReadOnlyList<SoldItem> Items, Money TabPaid, Money TabRefunded, Money CreditPaid, Money CreditRefunded);

/// <summary>One row of the sale, what has already come back of it, and its batch's last day.</summary>
/// <param name="Returned">The sum of <c>returns.quantity_returned</c> against it; zero when none.</param>
/// <param name="BatchExpires">The batch's expiry date; null when it has none.</param>
public sealed record SoldItem(TransactionItem Item, Quantity Returned, DateOnly? BatchExpires);
