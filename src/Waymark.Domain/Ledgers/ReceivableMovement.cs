using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Domain.Ledgers;

/// <summary>
/// Maps to <c>receivable_movements</c>: what a customer owes the store on account (le carnet),
/// as an append-only ledger (F-16).
///
/// <para>
/// <b>Positive means the customer owes more.</b> A <c>charge</c> mirrors one
/// <c>on_account</c> payment row, sign included, and is linked to it by
/// <see cref="PaymentId"/>: a sale charges the tab, and a refund of an on-account sale credits
/// it with a negative charge rather than paying cash out of the drawer. So the charges are
/// derived from the transactions, never parallel to them. A <c>payment</c> is money the
/// customer brings in, always negative; in cash it is a <c>paid_in</c> on the open session,
/// linked by <see cref="CashMovementId"/>, so the drawer still reconciles (D-034). A
/// <c>write_off</c> is negative and an <c>adjustment</c> either sign, and both carry a
/// reason.
/// </para>
/// <para>
/// <b>The balance is derived, never cached</b>: the sum of a customer's rows. Settlement is
/// balance-level: charges stay dated so aging can be computed first-in first-out at read time,
/// and which charge a payment settled is not stored.
/// </para>
/// </summary>
public sealed class ReceivableMovement : IStoreScoped
{
    /// <summary>Primary key (<c>movement_id</c>).</summary>
    public required string MovementId { get; init; }

    public required string StoreId { get; init; }

    public required string CustomerId { get; init; }

    public required ReceivableMovementType MovementType { get; init; }

    /// <summary>Signed: positive increases what the customer owes.</summary>
    public required Money Amount { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The <c>on_account</c> payment row a charge mirrors. Set on charges only.</summary>
    public string? PaymentId { get; init; }

    /// <summary>The drawer's <c>paid_in</c> for a cash repayment. Set on payments only.</summary>
    public string? CashMovementId { get; init; }

    /// <summary>Required on write-offs and adjustments.</summary>
    public string? ReasonCode { get; init; }

    public string? StaffId { get; init; }
}
