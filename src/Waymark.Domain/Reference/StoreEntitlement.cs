// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Reference;

/// <summary>
/// Maps to <c>store_entitlements</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>StoreEntitlementConfiguration</c>.
/// </para>
/// </summary>
public sealed class StoreEntitlement
{
    /// <summary>Primary key (<c>entitlement_code</c>).</summary>
    public required string EntitlementCode { get; init; }

    public bool IsEnabled { get; init; }

    public Tier? Tier { get; init; }

    public string? ValidFrom { get; init; }

    public string? ValidTo { get; init; }

    public required DateTimeOffset SyncedAt { get; init; }
}
