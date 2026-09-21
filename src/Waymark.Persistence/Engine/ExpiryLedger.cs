using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Engine;
using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Persistence.Engine;

/// <summary>
/// <see cref="IExpiryLedger"/> over the store database (hop 6).
///
/// <para>
/// Store scoping is the context's global filter, on <c>batches</c>, <c>inventories</c> and
/// <c>recommendations</c> directly and on <c>batch_items</c> through its batch (D-062), so
/// nothing here names a store (CLAUDE.md §3.3). <c>parameter_registry</c> is the exception
/// and is not scoped at all: a parameter has its own scope columns, and the window in Phase
/// 0.5 is the global placeholder.
/// </para>
/// </summary>
public sealed class ExpiryLedger(WaymarkDbContext context) : IExpiryLedger
{
    public async Task<EngineParameter?> NearExpiryWindowAsync(CancellationToken cancellationToken = default)
    {
        // The current row, highest version first: is_current is the engine's own flag, and two
        // rows carrying it would be a registry bug rather than a reason to pick arbitrarily.
        var entry = await context.ParameterRegistry
            .Where(row => row.ParameterCode == NearExpiry.ParameterCode
                && row.ScopeType == ScopeType.Global
                && row.IsCurrent
                && row.ValueNumber != null)
            .OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return entry is null ? null : new EngineParameter(entry.ValueNumber!.Value, entry.Version);
    }

    public async Task<IReadOnlyList<BatchStock>> BatchesWithStockAsync(CancellationToken cancellationToken = default)
    {
        // Levels first, because that is what makes a batch worth looking at: a batch nothing is
        // left of is not near expiry, it is finished.
        var levels = await context.Inventories
            .Where(level => level.Quantity > 0)
            .Select(level => new { level.BatchId, level.VariantId, level.Quantity })
            .ToListAsync(cancellationToken);

        if (levels.Count == 0)
        {
            return [];
        }

        var batchIds = levels.Select(level => level.BatchId).Distinct(StringComparer.Ordinal).ToList();

        var batches = await context.Batches
            .Where(batch => batchIds.Contains(batch.BatchId) && batch.Status == BatchStatus.Active)
            .Select(batch => new { batch.BatchId, batch.ProductId, batch.ExpirationDate })
            .ToListAsync(cancellationToken);

        // The unit and the cost are the delivery's, not the catalogue's: what is at risk is
        // what this batch cost, at the price it was bought at.
        var items = await context.BatchItems
            .Where(item => batchIds.Contains(item.BatchId))
            .Select(item => new { item.BatchId, item.VariantId, item.UnitCode, item.UnitCost })
            .ToListAsync(cancellationToken);

        var costs = items.ToDictionary(item => (item.BatchId, item.VariantId));

        var names = await context.Products
            .Where(product => batches.Select(batch => batch.ProductId).Contains(product.ProductId))
            .ToDictionaryAsync(product => product.ProductId, product => product.ProductName, cancellationToken);

        var byBatch = levels.ToLookup(level => level.BatchId, StringComparer.Ordinal);

        return
        [
            .. batches
                .Select(batch => new BatchStock(
                    batch.BatchId,
                    batch.ProductId,
                    names.TryGetValue(batch.ProductId, out var name) ? name : batch.ProductId,
                    batch.ExpirationDate,
                    [
                        .. byBatch[batch.BatchId]
                            // A level whose delivery line is missing has no unit and no cost, and
                            // there is nothing honest to say about it. It belongs to a broken
                            // import, not to this rule.
                            .Where(level => costs.ContainsKey((batch.BatchId, level.VariantId)))
                            .Select(level =>
                            {
                                var item = costs[(batch.BatchId, level.VariantId)];
                                return new BatchStockLine(
                                    level.VariantId,
                                    Quantity.FromThousandths(level.Quantity, item.UnitCode),
                                    item.UnitCost);
                            })
                    ]))
                .Where(batch => batch.Lines.Count > 0)
        ];
    }

    public async Task<IReadOnlyList<LiveRecommendation>> LiveRecommendationsAsync(
        string recommendationType, CancellationToken cancellationToken = default)
    {
        // Tracked, not projected: Supersede changes these same rows, and a row read through the
        // store filter is this store's by construction — which is also what keeps the write
        // guard satisfied (D-071).
        var live = await context.Recommendations
            .Where(card => card.RecommendationType == recommendationType
                && (card.Status == RecommendationStatus.Pending || card.Status == RecommendationStatus.Delivered))
            .ToListAsync(cancellationToken);

        return [.. live.Select(card => new LiveRecommendation(card.RecommendationId, card.SubjectId))];
    }

    public async Task<string?> DecidingRoleAsync(CancellationToken cancellationToken = default)
    {
        // roles is the tenant's table, not a store's, so no filter applies here.
        var ranks = await context.Roles
            .Where(role => role.IsActive)
            .OrderBy(role => role.Rank)
            .ThenBy(role => role.RoleCode)
            .Select(role => new { role.RoleCode, role.Rank })
            .ToListAsync(cancellationToken);

        if (ranks.Count == 0)
        {
            return null;
        }

        // The second rung, or the only one there is: a shop whose staff all share one role has
        // nobody to escalate to, and a card nobody may see is worse than one everybody may.
        var floor = ranks[0].Rank;
        var deciding = ranks.FirstOrDefault(role => role.Rank > floor) ?? ranks[0];

        return deciding.RoleCode;
    }

    public void Supersede(string recommendationId)
    {
        // The row was loaded (and is tracked) by LiveRecommendationsAsync; the evaluator only
        // names cards that came from there. Its properties are init-only, as every row in Domain
        // is, so the change goes through the tracked entry — the same way a batch level does.
        var card = context.Recommendations.Local.Single(row => row.RecommendationId == recommendationId);
        var entry = context.Entry(card);
        entry.Property(row => row.Status).CurrentValue = RecommendationStatus.Superseded;
    }
}
