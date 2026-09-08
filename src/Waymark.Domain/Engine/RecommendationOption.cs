// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Engine;

/// <summary>
/// Maps to <c>recommendation_options</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>RecommendationOptionConfiguration</c>.
/// </para>
/// </summary>
public sealed class RecommendationOption
{
    /// <summary>Primary key (<c>option_id</c>).</summary>
    public required string OptionId { get; init; }

    public required string RecommendationId { get; init; }

    public required string Label { get; init; }

    public long DisplayOrder { get; init; }

    public required string PayloadJson { get; init; }

    public long? ProjectedValue { get; init; }
}
