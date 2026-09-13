// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;
using Waymark.Domain.Values;

using Waymark.Domain;

namespace Waymark.Domain.Sales;

/// <summary>
/// Maps to <c>transactions</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>TransactionConfiguration</c>.
/// </para>
/// </summary>
public sealed class Transaction : IStoreScoped
{
    /// <summary>Primary key (<c>transaction_id</c>).</summary>
    public required string TransactionId { get; init; }

    public required string StoreId { get; init; }

    public required string TerminalId { get; init; }

    public string? CashSessionId { get; init; }

    public required string StaffId { get; init; }

    public string? CustomerId { get; init; }

    public string? InvoiceNumber { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
    /// <summary>
    /// The store's rounding policy at the moment of sale, stamped so the receipt
    /// recomputes from its own row after the store changes policy (D-032).
    /// <b>Required, with no default</b>: a sale that silently took <c>HalfUp</c> in
    /// a <c>HalfEven</c> store would be exactly the unrecomputable receipt this
    /// column exists to prevent (D-053).
    /// </summary>
    public required Rounding RoundingPolicy { get; init; }

    public Money Subtotal { get; init; }

    public Money DiscountTotal { get; init; }

    public Money TaxTotal { get; init; }

    public Money TotalAmount { get; init; }

    public string Currency { get; init; } = "DZD";

    public bool EcommerceFlag { get; init; }

    public string? OriginalTransactionId { get; init; }

    public TransactionStatus Status { get; init; } = TransactionStatus.Open;

    public DateTimeOffset? VoidedAt { get; init; }

    public string? VoidedBy { get; init; }

    public string? VoidReasonCode { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
