// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Reference;

/// <summary>
/// Maps to <c>reason_codes</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>ReasonCodeConfiguration</c>.
/// </para>
/// </summary>
public sealed class ReasonCode
{
    /// <summary>Primary key (<c>reason_code</c>).</summary>
    public required string ReasonCodeValue { get; init; }

    public required ReasonCodeAppliesTo AppliesTo { get; init; }

    public required string LabelAr { get; init; }

    public required string LabelFr { get; init; }

    public bool RequiresNote { get; init; }

    public bool RequiresManager { get; init; }

    public long DisplayOrder { get; init; }

    public bool IsActive { get; init; } = true;

    public required DateTimeOffset CreatedAt { get; init; }
}
