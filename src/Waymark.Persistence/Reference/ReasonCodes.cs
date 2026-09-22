using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Enums;
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Reference;

/// <summary>
/// <see cref="IReasonCodes"/> over the store database (session A3).
///
/// <para>
/// No store filter appears here and none is applied: <c>reason_codes</c> has no
/// <c>store_id</c> and is the tenant's vocabulary, which <c>ParentScopeTests</c> records with
/// its reason. That is the opposite of <c>prices</c>, where the global filter does the work
/// silently — worth saying out loud, because "no filter" and "filter I forgot" look identical
/// in a query.
/// </para>
/// </summary>
public sealed class ReasonCodes(WaymarkDbContext context) : IReasonCodes
{
    public async Task<IReadOnlyList<ReasonCodeChoice>> ForAsync(
        ReasonCodeAppliesTo appliesTo,
        CancellationToken cancellationToken = default)
    {
        // Inactive codes never cross. A shop retires a reason it no longer wants used, and
        // the rows that already reference it keep working because the foreign key still
        // resolves — but nobody may pick it again.
        //
        // Ordered by display_order, then by code. The second key is not decoration:
        // display_order defaults to 0, so a shop that never set one leaves every row tied,
        // and SQLite is free to return tied rows in any order it likes. A list of reasons
        // that reshuffles between two openings of the same dialog is the kind of thing a
        // cashier learns to distrust, and picking by position would then pick wrongly.
        return await context.ReasonCodes
            .Where(reason => reason.AppliesTo == appliesTo && reason.IsActive)
            .OrderBy(reason => reason.DisplayOrder)
            .ThenBy(reason => reason.ReasonCodeValue)
            .Select(reason => new ReasonCodeChoice(
                reason.ReasonCodeValue,
                reason.LabelAr,
                reason.LabelFr,
                reason.RequiresNote,
                reason.RequiresManager))
            .ToListAsync(cancellationToken);
    }
}
