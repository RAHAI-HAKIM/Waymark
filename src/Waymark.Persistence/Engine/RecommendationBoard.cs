using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Engine;
using Waymark.Domain.Enums;

namespace Waymark.Persistence.Engine;

/// <summary>
/// <see cref="IRecommendationBoard"/> over the store database (hop 7).
///
/// <para>
/// Store scoping is the context's global filter: <c>recommendations</c> and <c>staff</c>
/// carry a store, and <c>recommendation_options</c> is filtered through its card (D-062), so
/// nothing here names a store (CLAUDE.md §3.3). <c>roles</c> is the tenant's table and is not
/// scoped at all.
/// </para>
/// </summary>
public sealed class RecommendationBoard(WaymarkDbContext context) : IRecommendationBoard
{
    public async Task<StaffOnDuty?> StaffAsync(string staffId, CancellationToken cancellationToken = default)
    {
        var person = await context.Staff
            .Where(member => member.StaffId == staffId && member.Status == StaffStatus.Active)
            .Select(member => new { member.StaffId, member.StaffName, member.Role })
            .FirstOrDefaultAsync(cancellationToken);

        if (person is null)
        {
            return null;
        }

        // A staff member whose role is not an active row in roles has no rank, and a rank of
        // zero would quietly make them the most junior person in the shop rather than an
        // error. Absence is never zero (D-037).
        var rank = await context.Roles
            .Where(role => role.RoleCode == person.Role && role.IsActive)
            .Select(role => (long?)role.Rank)
            .FirstOrDefaultAsync(cancellationToken);

        return rank is { } found ? new StaffOnDuty(person.StaffId, person.StaffName, person.Role, found) : null;
    }

    public async Task<IReadOnlyList<CardOnTheBoard>> LiveCardsAsync(CancellationToken cancellationToken = default)
    {
        var cards = await context.Recommendations
            .Where(card => card.Status == RecommendationStatus.Pending || card.Status == RecommendationStatus.Delivered)
            .OrderByDescending(card => card.Urgency)
            .ThenByDescending(card => card.ComputedAt)
            .ToListAsync(cancellationToken);

        return await WithOptionsAndRanks(cards, cancellationToken);
    }

    public async Task<CardOnTheBoard?> CardAsync(
        string recommendationId, CancellationToken cancellationToken = default)
    {
        var card = await context.Recommendations
            .FirstOrDefaultAsync(row => row.RecommendationId == recommendationId, cancellationToken);

        return card is null ? null : (await WithOptionsAndRanks([card], cancellationToken)).FirstOrDefault();
    }

    public void MarkDecided(string recommendationId)
    {
        // The row was loaded (and is tracked) by CardAsync; the handler only names a card that
        // came from there. Init-only properties, so the change goes through the tracked entry.
        var card = context.Recommendations.Local.Single(row => row.RecommendationId == recommendationId);
        context.Entry(card).Property(row => row.Status).CurrentValue = RecommendationStatus.Decided;
    }

    /// <summary>
    /// Attaches each card's options and turns its <c>minimum_required_role</c> into a rank.
    /// <b>A card whose role is not an active row is dropped</b>, not shown to everybody: a
    /// card nobody can be measured against is a broken card, and the safe reading of a broken
    /// gate is shut (D-030).
    /// </summary>
    private async Task<List<CardOnTheBoard>> WithOptionsAndRanks(
        List<Domain.Engine.Recommendation> cards, CancellationToken cancellationToken)
    {
        if (cards.Count == 0)
        {
            return [];
        }

        var ids = cards.Select(card => card.RecommendationId).ToList();

        var options = (await context.RecommendationOptions
                .Where(option => ids.Contains(option.RecommendationId))
                .OrderBy(option => option.DisplayOrder)
                .ToListAsync(cancellationToken))
            .ToLookup(option => option.RecommendationId, StringComparer.Ordinal);

        var roles = cards.Select(card => card.MinimumRequiredRole).Distinct(StringComparer.Ordinal).ToList();
        var ranks = await context.Roles
            .Where(role => roles.Contains(role.RoleCode) && role.IsActive)
            .ToDictionaryAsync(role => role.RoleCode, role => role.Rank, cancellationToken);

        return
        [
            .. cards
                .Where(card => ranks.ContainsKey(card.MinimumRequiredRole))
                .Select(card => new CardOnTheBoard(
                    card,
                    ranks[card.MinimumRequiredRole],
                    [.. options[card.RecommendationId]]))
        ];
    }
}
