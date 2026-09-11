// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

using Waymark.Domain;

namespace Waymark.Domain.Organisation;

/// <summary>
/// Maps to <c>stores</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>StoreConfiguration</c>.
/// </para>
/// </summary>
public sealed class Store : IStoreScoped
{
    /// <summary>Primary key (<c>store_id</c>).</summary>
    public required string StoreId { get; init; }

    public required string StoreCode { get; init; }

    public required string StoreName { get; init; }

    public string? ContactPhone { get; init; }

    public string? Email { get; init; }

    public string? Address { get; init; }

    public string? Latitude { get; init; }

    public string? Longitude { get; init; }

    public required string StoreType { get; init; }

    public string Currency { get; init; } = "DZD";

    public string Timezone { get; init; } = "Africa/Algiers";

    public string? TaxRegistrationNumber { get; init; }

    public string FiscalYearStart { get; init; } = "01-01";

    public string? ManagerId { get; init; }

    public StoreStatus Status { get; init; } = StoreStatus.Active;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
