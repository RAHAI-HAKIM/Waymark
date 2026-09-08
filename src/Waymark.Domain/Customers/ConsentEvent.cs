// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Customers;

/// <summary>
/// Maps to <c>consent_events</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>ConsentEventConfiguration</c>.
/// </para>
/// </summary>
public sealed class ConsentEvent
{
    /// <summary>Primary key (<c>consent_event_id</c>).</summary>
    public required string ConsentEventId { get; init; }

    public required string CustomerId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required ConsentEventAction Action { get; init; }

    public required ConsentType ConsentType { get; init; }

    public required string NoticeVersion { get; init; }

    public string? CapturedBy { get; init; }

    public required Method Method { get; init; }

    public string? TerminalId { get; init; }
}
