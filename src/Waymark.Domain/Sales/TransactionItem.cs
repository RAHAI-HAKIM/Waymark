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

    public DateTimeOffset? RemovedAt { get; init; }

    public string? RemovedBy { get; init; }

    /// <summary>
    /// Whose PIN let a cashier strike the line once "Encaisser" had been opened on the ticket
    /// (D-106): a person of <c>VoidTransaction</c>. Null for every other row, struck or not.
    /// </summary>
    public string? RemovedAuthorisedBy { get; init; }

    /// <summary>
    /// The line's place on its ticket, from 1 (D-104). <b>Rows of one line share it</b>: a line taken
    /// from two batches is two rows and one number. Lines struck come after the lines sold. Ids do not
    /// sort within a millisecond (<c>UlidGenerator</c>), so this is the only order a ticket has.
    /// <b>Required, with no default</b>, like <see cref="QuantitySource"/>; the column's default, 0,
    /// is for the rows written before it, which read in the order they always did.
    /// </summary>
    public required int LineNumber { get; init; }

    public Money TaxAmount { get; init; }

    public required Money LineTotal { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Where <see cref="Quantity"/> came from, and so which of quantity and line total is exact
    /// (D-090). <b>Required, with no default</b>, like <see cref="Transaction.RoundingPolicy"/>: a
    /// weighed row that silently took <c>count</c> would recompute the wrong way round. The column's
    /// default exists only for the rows written before it (all of them counts, since nothing
    /// weighed could be sold until B3).
    /// </summary>
    public required QuantitySource QuantitySource { get; init; }

    /// <summary>
    /// The price in force when the row's price was overridden at the counter (B5, D-092); null when
    /// it was not. <see cref="SellPrice"/> stays what was charged, so the row still recomputes.
    /// </summary>
    public Money? ListPrice { get; init; }

    /// <summary>Why the price was overridden: a <c>price_override</c> reason (D-079). Set with <see cref="ListPrice"/>.</summary>
    public string? OverrideReasonCode { get; init; }

    /// <summary>Who allowed the override, rank 3 (D-092). Set with <see cref="ListPrice"/>.</summary>
    public string? OverrideAuthorisedBy { get; init; }

    /// <summary>What the cashier wrote for a discount reason that asks for a note (F-28, D-092).</summary>
    public string? DiscountNote { get; init; }
}
