// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Organisation;

/// <summary>
/// Maps to <c>shifts</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>ShiftConfiguration</c>.
/// </para>
/// </summary>
public sealed class Shift
{
    /// <summary>Primary key (<c>shift_id</c>).</summary>
    public required string ShiftId { get; init; }

    public required string StaffId { get; init; }

    public string? TerminalId { get; init; }

    public required string StoreId { get; init; }

    public required string StartTime { get; init; }

    public string? EndTime { get; init; }

    public ShiftStatus Status { get; init; } = ShiftStatus.Open;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
