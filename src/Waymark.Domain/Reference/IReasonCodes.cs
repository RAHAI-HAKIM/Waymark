using Waymark.Domain.Enums;

namespace Waymark.Domain.Reference;

/// <summary>
/// The reasons a shop accepts for one kind of action, as the till and Admin offer them
/// (session A3).
///
/// <para>
/// <c>reason_codes</c> has been in the schema and seeded by the generator since Phase 0, and
/// read by nothing. Several columns that record <i>why</i> something happened point at it —
/// <c>transaction_items.discount_reason_code</c>, <c>cash_movements.reason_code</c>,
/// <c>stock_movements</c>, voids, no-sales, write-offs — and every one of those is a foreign
/// key. So the list a cashier is shown is not decoration: it is the set of values the
/// database will accept, and offering anything else produces a constraint failure at the
/// moment of sale.
/// </para>
/// <para>
/// A read: it stages nothing and needs no unit of work. The vocabulary belongs to the tenant
/// rather than to a store — <c>reason_codes</c> carries no <c>store_id</c> and
/// <c>ParentScopeTests</c> lists it as vocabulary — so no store filter applies and every
/// store is offered the same reasons.
/// </para>
/// </summary>
public interface IReasonCodes
{
    /// <summary>
    /// The active reasons for this kind of action, in the order they should be shown.
    /// Empty is a legitimate answer: a shop that has not defined a reason for something has
    /// none, and that is not an error here (whether the action may then proceed is the
    /// caller's rule, not this list's).
    /// </summary>
    Task<IReadOnlyList<ReasonCodeChoice>> ForAsync(
        ReasonCodeAppliesTo appliesTo,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One reason, as it is offered.
/// </summary>
/// <param name="Code">
/// What gets written to the row that records the action: the value of the foreign key, not a
/// label.
/// </param>
/// <param name="LabelAr">The Arabic label, as shown.</param>
/// <param name="LabelFr">The French label, as shown.</param>
/// <param name="RequiresNote">
/// The shop wants words as well as a code. Enforcing it is the caller's job — this says what
/// the shop asked for, not what the till did about it.
/// </param>
/// <param name="RequiresManager">
/// The shop wants this reason authorised by somebody senior. <b>A3 carries this flag and
/// enforces nothing</b>: the rank comparison is <c>StaffPermissions</c> (A2), and having two
/// places that decide who may do what is exactly how the copies drift.
/// </param>
public sealed record ReasonCodeChoice(
    string Code,
    string LabelAr,
    string LabelFr,
    bool RequiresNote,
    bool RequiresManager);
