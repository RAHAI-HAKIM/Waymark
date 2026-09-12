// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Domain.Customers;

/// <summary>
/// Maps to <c>customers</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>CustomerConfiguration</c>.
/// </para>
/// </summary>
public sealed class Customer
{
    /// <summary>Primary key (<c>customer_id</c>).</summary>
    public required string CustomerId { get; init; }

    public required string CustomerName { get; init; }

    public string? ContactPhone { get; init; }

    public string? Email { get; init; }

    public PreferredLanguage PreferredLanguage { get; init; } = PreferredLanguage.Ar;

    public required DateOnly JoinDate { get; init; }

    public DateOnly? LastOrderDate { get; init; }

    public long Points { get; init; }

    public Money Credit { get; init; }

    public long Discount { get; init; }

    public string? TierRanking { get; init; }

    public bool EcommerceFlag { get; init; }

    public LegalBasis LegalBasis { get; init; } = LegalBasis.Contract;

    public bool ConsentProfiling { get; init; }

    public DateTimeOffset? ConsentProfilingAt { get; init; }

    public string? ConsentProfilingNoticeVersion { get; init; }

    public bool ConsentMarketing { get; init; }

    public DateTimeOffset? ConsentMarketingAt { get; init; }

    public string? ConsentMarketingNoticeVersion { get; init; }

    public bool ObjectionFlag { get; init; }

    public DateTimeOffset? DeletionRequestedAt { get; init; }

    public CustomerStatus Status { get; init; } = CustomerStatus.Active;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
