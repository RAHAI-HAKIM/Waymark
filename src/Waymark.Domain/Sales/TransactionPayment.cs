// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Domain.Sales;

/// <summary>
/// Maps to <c>transaction_payments</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>TransactionPaymentConfiguration</c>.
/// </para>
/// </summary>
public sealed class TransactionPayment
{
    /// <summary>Primary key (<c>payment_id</c>).</summary>
    public required string PaymentId { get; init; }

    public required string TransactionId { get; init; }

    public required long Sequence { get; init; }

    public required PaymentMethod PaymentMethod { get; init; }

    public required Money Amount { get; init; }

    public string Currency { get; init; } = "DZD";

    public string? Reference { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
