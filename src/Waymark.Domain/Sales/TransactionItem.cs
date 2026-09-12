// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Values;

namespace Waymark.Domain.Sales;

/// <summary>
/// Maps to <c>transaction_items</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>TransactionItemConfiguration</c>.
/// </para>
/// </summary>
public sealed class TransactionItem
{
    /// <summary>Primary key (<c>transaction_item_id</c>).</summary>
    public required string TransactionItemId { get; init; }

    public required string TransactionId { get; init; }

    public required string VariantId { get; init; }

    public string? BatchId { get; init; }

    public string? PromotionId { get; init; }

    public required long Quantity { get; init; }

    public required string UnitCode { get; init; }

    public required Money SellPrice { get; init; }

    public Money? UnitCostAtSale { get; init; }

    public Money DiscountAmount { get; init; }

    public string? DiscountReasonCode { get; init; }

    public string? AuthorisedBy { get; init; }

    public Money TaxAmount { get; init; }

    public required Money LineTotal { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
