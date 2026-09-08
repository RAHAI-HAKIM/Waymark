// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

namespace Waymark.Domain.Catalogue;

/// <summary>
/// Maps to <c>product_category</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>ProductCategoryConfiguration</c>.
/// </para>
/// </summary>
public sealed class ProductCategory
{
    /// <summary>Part of the primary key (<c>product_id</c>).</summary>
    public required string ProductId { get; init; }

    /// <summary>Part of the primary key (<c>category_id</c>).</summary>
    public required string CategoryId { get; init; }

    public bool IsPrimary { get; init; }

    public required DateTimeOffset AddedAt { get; init; }
}
