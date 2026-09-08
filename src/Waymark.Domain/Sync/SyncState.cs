// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Sync;

/// <summary>
/// Maps to <c>sync_state</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>SyncStateConfiguration</c>.
/// </para>
/// </summary>
public sealed class SyncState
{
    /// <summary>Primary key (<c>state_key</c>).</summary>
    public required string StateKey { get; init; }

    public required string StateValue { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
