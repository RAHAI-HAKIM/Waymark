// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Reference;

/// <summary>
/// Maps to <c>system_config</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>SystemConfigEntryConfiguration</c>.
/// </para>
/// </summary>
public sealed class SystemConfigEntry
{
    /// <summary>Primary key (<c>config_key</c>).</summary>
    public required string ConfigKey { get; init; }

    public required string ConfigValue { get; init; }

    public required SystemConfigEntryDataType DataType { get; init; }

    public string? Description { get; init; }

    public string? UpdatedBy { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
