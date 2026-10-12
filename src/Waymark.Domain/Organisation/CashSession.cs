// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;
using Waymark.Domain.Values;

using Waymark.Domain;

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
public sealed class CashSession : IStoreScoped
{
    /// <summary>Primary key (<c>session_id</c>).</summary>
    public required string SessionId { get; init; }

    public required string StoreId { get; init; }

    public required string TerminalId { get; init; }

    public required string OpenedBy { get; init; }

    public required DateTimeOffset OpenedAt { get; init; }

    public Money OpeningFloat { get; init; }

    public string? ClosedBy { get; init; }

    /// <summary>
    /// Whose PIN allowed the close, when who counted (<see cref="ClosedBy"/>) may not close alone
    /// (C1, D-111); null when they may.
    /// </summary>
    public string? ClosedAuthorisedBy { get; init; }

    public DateTimeOffset? ClosedAt { get; init; }

    public Money? CountedCash { get; init; }

    public Money? ExpectedCash { get; init; }

    public Money? Variance { get; init; }

    public long? ZReportNumber { get; init; }

    /// <summary>
    /// The tenant's thresholds in force when the session closed (C2, D-112), written with the close: a
    /// Z is judged by them for good, so a threshold changed later rewrites no past Z. Null: that
    /// threshold was off, or the session is open or was closed before the columns existed.
    /// </summary>
    public long? VoidAlertCount { get; init; }

    public Money? VoidAlertValue { get; init; }

    public Money? VarianceAlertValue { get; init; }

    /// <summary>
    /// A blind count kept by the server while the session is still open (D-119): what somebody who is
    /// not shown the drawer's figure first counted, so the close cannot be asked again and again with
    /// another figure until its answer changes. Null until such a count is sent.
    /// </summary>
    public Money? PendingCount { get; init; }

    /// <summary>
    /// How many times cash had moved in the session when <see cref="PendingCount"/> was kept
    /// (<see cref="Drawer.StandingCount"/>): the count stands while that number has not changed.
    /// </summary>
    public long? PendingCountMoves { get; init; }

    public CashSessionStatus Status { get; init; } = CashSessionStatus.Open;

    public string? Notes { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
