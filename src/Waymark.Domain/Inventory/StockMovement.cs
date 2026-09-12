// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;
using Waymark.Domain.Values;

using Waymark.Domain;

namespace Waymark.Domain.Inventory;

/// <summary>
/// Maps to <c>stock_movements</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>StockMovementConfiguration</c>.
/// </para>
/// </summary>
public sealed class StockMovement : IStoreScoped
{
    /// <summary>Primary key (<c>movement_id</c>).</summary>
    public required string MovementId { get; init; }

    public required string StoreId { get; init; }

    public required string VariantId { get; init; }

    public required string BatchId { get; init; }

    public required DateOnly MovementDate { get; init; }

    public required StockMovementType MovementType { get; init; }

    public required long QuantityChanged { get; init; }

    public required string UnitCode { get; init; }

    public Money? UnitCost { get; init; }

    public string? ReferenceType { get; init; }

    public string? ReferenceId { get; init; }

    public string? ReasonCode { get; init; }

    public string? StaffId { get; init; }

    public string? Note { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
