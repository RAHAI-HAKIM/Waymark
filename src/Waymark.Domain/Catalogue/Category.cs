// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Catalogue;

/// <summary>
/// Maps to <c>categories</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>CategoryConfiguration</c>.
/// </para>
/// </summary>
public sealed class Category
{
    /// <summary>Primary key (<c>category_id</c>).</summary>
    public required string CategoryId { get; init; }

    public required string CategoryName { get; init; }

    public string? ParentId { get; init; }

    public required string Slug { get; init; }

    public string? Description { get; init; }

    public string? Image { get; init; }

    public long? TaxRate { get; init; }

    public bool SensitiveFlag { get; init; }

    public string? SensitiveReason { get; init; }

    public CategoryStatus Status { get; init; } = CategoryStatus.Active;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
