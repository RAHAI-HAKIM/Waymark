// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Ledgers;

/// <summary>
/// Maps to <c>credit_movements</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>CreditMovementConfiguration</c>.
/// </para>
/// </summary>
public sealed class CreditMovement
{
    /// <summary>Primary key (<c>movement_id</c>).</summary>
    public required string MovementId { get; init; }

    public required string CustomerId { get; init; }

    public required CreditMovementType MovementType { get; init; }

    public required long Amount { get; init; }

    public required long BalanceAfter { get; init; }

    public string? TransactionId { get; init; }

    public string? ReturnId { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? StaffId { get; init; }

    public string? TerminalId { get; init; }

    public string? ReasonCode { get; init; }

    public string? Note { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}
