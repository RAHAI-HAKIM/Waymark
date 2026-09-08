// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Purchasing;

/// <summary>
/// Maps to <c>purchase_orders</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>PurchaseOrderConfiguration</c>.
/// </para>
/// </summary>
public sealed class PurchaseOrder
{
    /// <summary>Primary key (<c>order_id</c>).</summary>
    public required string OrderId { get; init; }

    public required string StoreId { get; init; }

    public required string SupplierId { get; init; }

    public required DateOnly OrderDate { get; init; }

    public DateOnly? ExpectedArrivalDate { get; init; }

    public DateTimeOffset? ReceivedAt { get; init; }

    public long? TotalAmount { get; init; }

    public string Currency { get; init; } = "DZD";

    public PurchaseOrderSource Source { get; init; } = PurchaseOrderSource.Manual;

    public string? SourceRecommendationId { get; init; }

    public string? CreatedBy { get; init; }

    public PurchaseOrderStatus Status { get; init; } = PurchaseOrderStatus.Draft;

    public string? Notes { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
