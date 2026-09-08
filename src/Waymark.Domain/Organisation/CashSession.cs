// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Organisation;

/// <summary>
/// Maps to <c>cash_sessions</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>CashSessionConfiguration</c>.
/// </para>
/// </summary>
public sealed class CashSession
{
    /// <summary>Primary key (<c>session_id</c>).</summary>
    public required string SessionId { get; init; }

    public required string StoreId { get; init; }

    public required string TerminalId { get; init; }

    public required string OpenedBy { get; init; }

    public required DateTimeOffset OpenedAt { get; init; }

    public long OpeningFloat { get; init; }

    public string? ClosedBy { get; init; }

    public DateTimeOffset? ClosedAt { get; init; }

    public long? CountedCash { get; init; }

    public long? ExpectedCash { get; init; }

    public long? Variance { get; init; }

    public long? ZReportNumber { get; init; }

    public CashSessionStatus Status { get; init; } = CashSessionStatus.Open;

    public string? Notes { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
