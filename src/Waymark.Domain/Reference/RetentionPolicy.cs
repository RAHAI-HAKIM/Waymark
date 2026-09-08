// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Reference;

/// <summary>
/// Maps to <c>retention_policies</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>RetentionPolicyConfiguration</c>.
/// </para>
/// </summary>
public sealed class RetentionPolicy
{
    /// <summary>Primary key (<c>policy_code</c>).</summary>
    public required string PolicyCode { get; init; }

    public required EntityType EntityType { get; init; }

    public required long RetentionDays { get; init; }

    public required string LegalBasisReference { get; init; }

    public required ActionOnExpiry ActionOnExpiry { get; init; }

    public bool IsActive { get; init; } = true;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
