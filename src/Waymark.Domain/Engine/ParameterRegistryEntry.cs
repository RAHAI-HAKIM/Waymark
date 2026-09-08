// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Engine;

/// <summary>
/// Maps to <c>parameter_registry</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>ParameterRegistryEntryConfiguration</c>.
/// </para>
/// </summary>
public sealed class ParameterRegistryEntry
{
    /// <summary>Part of the primary key (<c>parameter_code</c>).</summary>
    public required string ParameterCode { get; init; }

    /// <summary>Part of the primary key (<c>scope_type</c>).</summary>
    public required ScopeType ScopeType { get; init; }

    /// <summary>Part of the primary key (<c>scope_id</c>).</summary>
    public string ScopeId { get; init; } = "";

    /// <summary>Part of the primary key (<c>version</c>).</summary>
    public required long Version { get; init; }

    public long? ValueNumber { get; init; }

    public string? ValueText { get; init; }

    public string? UnitCode { get; init; }

    public long? IntervalLow { get; init; }

    public long? IntervalHigh { get; init; }

    public required string Method { get; init; }

    public required ParameterRegistryEntrySource Source { get; init; }

    public long ObservationCount { get; init; }

    public required DateTimeOffset ComputedAt { get; init; }

    public bool IsCurrent { get; init; } = true;
}
