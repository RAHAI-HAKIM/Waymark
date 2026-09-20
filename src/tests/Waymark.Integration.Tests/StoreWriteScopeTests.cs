using Microsoft.EntityFrameworkCore;
using Waymark.Domain;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;
using Waymark.Domain.Values;

namespace Waymark.Integration.Tests;

/// <summary>
/// The write side of store scoping (D-071, F-21). The filter keeps a context from reading
/// another store's rows; this keeps it from writing one. The failure it prevents is silent:
/// the row is invisible to the store that made it and counted by the store it names, and
/// cross-tenant leakage is DPIA risk R9.
/// </summary>
public sealed class StoreWriteScopeTests(MigratedDatabaseFixture database) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly DateTimeOffset Moment = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];

    private string Mine => $"mine-{_suffix}";

    private string Theirs => $"theirs-{_suffix}";

    private static Store NewStore(string storeId) => new()
    {
        StoreId = storeId,
        StoreCode = storeId,
        StoreName = storeId,
        StoreType = "grocery",
        RoundingPolicy = Rounding.HalfUp,
        CreatedAt = Moment,
        UpdatedAt = Moment,
    };

    private static Promotion NewPromotion(string promotionId, string? storeId) => new()
    {
        PromotionId = promotionId,
        StoreId = storeId,
        PromotionName = promotionId,
        PromotionType = PromotionType.Discount,
        CreatedAt = Moment,
        UpdatedAt = Moment,
    };

    private void SeedBothStores()
    {
        using var context = database.NewContext();
        context.Stores.Add(NewStore(Mine));
        context.Stores.Add(NewStore(Theirs));
        context.SaveChanges();
    }

    [Fact]
    public void A_row_for_another_store_is_refused()
    {
        SeedBothStores();

        using var context = database.NewContext(storeId: Mine);
        context.Promotions.Add(NewPromotion($"promo-{_suffix}", Theirs));

        var refusal = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());

        Assert.Contains(Theirs, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("D-071", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_row_for_another_store_is_refused_when_saving_asynchronously()
    {
        // The whole application saves through the async path: a check on the sync one only
        // would never run in StoreServer.
        SeedBothStores();

        await using var context = database.NewContext(storeId: Mine);
        context.Promotions.Add(NewPromotion($"promo-async-{_suffix}", Theirs));

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void Moving_an_existing_row_to_another_store_is_refused()
    {
        SeedBothStores();
        var promotionId = $"promo-move-{_suffix}";
        using (var seed = database.NewContext(storeId: Mine))
        {
            seed.Promotions.Add(NewPromotion(promotionId, Mine));
            seed.SaveChanges();
        }

        using var context = database.NewContext(storeId: Mine);
        var promotion = context.Promotions.Single(p => p.PromotionId == promotionId);
        context.Entry(promotion).Property(p => p.StoreId).CurrentValue = Theirs;

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void This_stores_own_rows_are_written()
    {
        SeedBothStores();
        var promotionId = $"promo-mine-{_suffix}";

        using var context = database.NewContext(storeId: Mine);
        context.Promotions.Add(NewPromotion(promotionId, Mine));
        context.SaveChanges();

        Assert.Equal(1, context.Promotions.Count(p => p.PromotionId == promotionId));
    }

    [Fact]
    public void A_row_belonging_to_no_store_is_written()
    {
        // A promotion with no store is every store's, and the filter reads it here (D-030).
        SeedBothStores();
        var promotionId = $"promo-all-{_suffix}";

        using var context = database.NewContext(storeId: Mine);
        context.Promotions.Add(NewPromotion(promotionId, storeId: null));
        context.SaveChanges();

        Assert.Equal(1, context.Promotions.Count(p => p.PromotionId == promotionId));
    }

    [Fact]
    public void A_context_with_no_store_configured_writes_as_before()
    {
        // How the generator commissions a store and how tests seed one: there is no current
        // store to compare against, and such a context reads nothing that belongs to a store.
        using var context = database.NewContext();
        context.Stores.Add(NewStore($"unscoped-{_suffix}"));
        context.Promotions.Add(NewPromotion($"promo-unscoped-{_suffix}", $"unscoped-{_suffix}"));

        context.SaveChanges();

        Assert.Equal(1, context.Promotions.IgnoreQueryFilters().AsQueryable().Count(p => p.PromotionId == $"promo-unscoped-{_suffix}"));
    }
}
