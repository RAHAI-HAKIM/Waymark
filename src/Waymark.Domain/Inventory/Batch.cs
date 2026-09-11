// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

using Waymark.Domain;

namespace Waymark.Domain.Inventory;

/// <summary>
/// Maps to <c>batches</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>BatchConfiguration</c>.
/// </para>
/// </summary>
public sealed class Batch : IStoreScoped
{
    /// <summary>Primary key (<c>batch_id</c>).</summary>
    public required string BatchId { get; init; }

    public string? LotNumber { get; init; }

    public string? SupplierDocumentRef { get; init; }

    public required string ProductId { get; init; }

    public required string StoreId { get; init; }

    public string? SupplierId { get; init; }

    public string? OrderId { get; init; }

    public required DateOnly ReceivedDate { get; init; }

    public DateOnly? ExpirationDate { get; init; }

    public string? ReceivedBy { get; init; }

    public BatchStatus Status { get; init; } = BatchStatus.Active;

    public required DateTimeOffset CreatedAt { get; init; }
}
