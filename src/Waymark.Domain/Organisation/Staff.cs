// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Organisation;

/// <summary>
/// Maps to <c>staff</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>StaffConfiguration</c>.
/// </para>
/// </summary>
public sealed class Staff
{
    /// <summary>Primary key (<c>staff_id</c>).</summary>
    public required string StaffId { get; init; }

    public required string StoreId { get; init; }

    public required string StaffName { get; init; }

    public string? ContactPhone { get; init; }

    public string? Email { get; init; }

    public required string Role { get; init; }

    public required string PinHash { get; init; }

    public required DateOnly JoinDate { get; init; }

    public DateOnly? LastSeenDate { get; init; }

    public DateOnly? TerminationDate { get; init; }

    public string? NoticeVersionAcknowledged { get; init; }

    public DateTimeOffset? NoticeAcknowledgedAt { get; init; }

    public StaffStatus Status { get; init; } = StaffStatus.Active;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
