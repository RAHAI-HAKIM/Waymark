using Waymark.Domain.Values;

namespace Waymark.Domain.Customers;

/// <summary>What happened to a customer's tab limit (B7, D-096).</summary>
public enum CreditLimitEventType
{
    /// <summary>Stored as <c>set</c>: a limit given, raised, lowered, or taken away (null: no tab).</summary>
    Set,

    /// <summary>Stored as <c>frozen</c>: no new charge, the limit kept.</summary>
    Frozen,

    /// <summary>Stored as <c>unfrozen</c>.</summary>
    Unfrozen,
}

/// <summary>
/// Maps to <c>credit_limit_events</c>, append-only (B7, D-096): every change to a customer's tab
/// limit, who made it and when. <c>customers.credit_limit</c> is the limit in force; this is how it
/// got there, so an owner asked "who gave him 20 000?" has an answer.
///
/// <para>
/// The tenant's, like the customer: no <c>store_id</c>, since a limit holds in every store of the
/// chain. <c>ParentScopeTests</c> names it among the tenant's tables.
/// </para>
/// </summary>
public sealed class CreditLimitEvent
{
    public required string EventId { get; init; }

    public required string CustomerId { get; init; }

    public required CreditLimitEventType EventType { get; init; }

    /// <summary>The limit before a <see cref="CreditLimitEventType.Set"/>; null for no tab, and on a freeze.</summary>
    public Money? PreviousLimit { get; init; }

    /// <summary>The limit after a <see cref="CreditLimitEventType.Set"/>; null for no tab, and on a freeze.</summary>
    public Money? NewLimit { get; init; }

    /// <summary>Who did it: a person with <c>ManageCredit</c>, from the session, never the till's say-so.</summary>
    public required string StaffId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}
