// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

using Waymark.Domain;

namespace Waymark.Domain.Sales;

/// <summary>
/// Maps to <c>returns</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>SalesReturnConfiguration</c>.
/// </para>
/// </summary>
public sealed class SalesReturn : IStoreScoped
{
    /// <summary>Primary key (<c>return_id</c>).</summary>
    public required string ReturnId { get; init; }

    public required string TransactionItemId { get; init; }

    public required string StoreId { get; init; }

    public string? TerminalId { get; init; }

    public string? BatchId { get; init; }

    public required string StaffId { get; init; }

    public string? ApprovedBy { get; init; }

    public required long QuantityReturned { get; init; }

    public required long RefundAmount { get; init; }

    public required RefundMethod RefundMethod { get; init; }

    public required bool RestockFlag { get; init; }

    public required string ReasonCode { get; init; }

    public string? Note { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
