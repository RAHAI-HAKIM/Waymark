// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Reference;

/// <summary>
/// Maps to <c>notice_versions</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>NoticeVersionConfiguration</c>.
/// </para>
/// </summary>
public sealed class NoticeVersion
{
    /// <summary>Primary key (<c>version_code</c>).</summary>
    public required string VersionCode { get; init; }

    public required NoticeType NoticeType { get; init; }

    public required Language Language { get; init; }

    public required string BodyText { get; init; }

    public required string EffectiveFrom { get; init; }

    public string? EffectiveTo { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }
}
