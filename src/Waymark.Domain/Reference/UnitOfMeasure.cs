// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Reference;

/// <summary>
/// Maps to <c>units_of_measure</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>UnitOfMeasureConfiguration</c>.
/// </para>
/// </summary>
public sealed class UnitOfMeasure
{
    /// <summary>Primary key (<c>unit_code</c>).</summary>
    public required string UnitCode { get; init; }

    public required string NameAr { get; init; }

    public required string NameFr { get; init; }

    public required Dimension Dimension { get; init; }

    public string? BaseUnitCode { get; init; }

    public long FactorToBase { get; init; } = 1000000L;

    public long DecimalPlaces { get; init; }

    public bool IsActive { get; init; } = true;

    public required DateTimeOffset CreatedAt { get; init; }
}
