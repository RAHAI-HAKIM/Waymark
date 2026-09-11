using Microsoft.EntityFrameworkCore;
using Waymark.Domain;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;

namespace Waymark.Integration.Tests;

/// <summary>
/// A store sees its own rows and no others.
///
/// <para>
/// CLAUDE.md §3.3 requires this as a global query filter rather than a
/// <c>where</c> clause, because a <c>where</c> clause can be forgotten and
/// cross-tenant leakage is DPIA risk R9. These tests are what says the filter
/// is actually attached — a filter that is configured but not applied looks
/// exactly like one that is.
/// </para>
/// </summary>
public sealed class StoreScopingTests : IClassFixture<MigratedDatabaseFixture>
{
    private const string StoreA = "store-a";
    private const string StoreB = "store-b";

    private readonly MigratedDatabaseFixture _database;

    public StoreScopingTests(MigratedDatabaseFixture database)
    {
        _database = database;
        Seed();
    }

    private static readonly DateTimeOffset Moment = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Terminal TerminalFor(string storeId) => new()
    {
        TerminalId = $"terminal-{storeId}",
        StoreId = storeId,
        TerminalName = $"Till at {storeId}",
        CreatedAt = Moment,
        UpdatedAt = Moment
    };

    private void Seed()
    {
        // Written with no store filter in play, because seeding is not a read.
        using var context = _database.NewContext(enforceForeignKeys: false);

        if (context.Terminals.IgnoreQueryFilters().Any(t => t.StoreId == StoreA))
        {
            return;
        }

        context.Terminals.AddRange(TerminalFor(StoreA), TerminalFor(StoreB));

        context.Promotions.AddRange(
            new Promotion
            {
                PromotionId = "promo-store-a",
                PromotionName = "Store A only",
                PromotionType = PromotionType.Discount,
                StoreId = StoreA,
                CreatedAt = Moment,
                UpdatedAt = Moment
            },
            new Promotion
            {
                PromotionId = "promo-every-store",
                PromotionName = "Every store",
                PromotionType = PromotionType.Discount,
                StoreId = null,
                CreatedAt = Moment,
                UpdatedAt = Moment
            });

        context.SaveChanges();
    }

    [Fact]
    public void A_store_sees_only_its_own_rows()
    {
        using var context = _database.NewContext(enforceForeignKeys: false, storeId: StoreA);

        var terminals = context.Terminals.Select(t => t.TerminalId).ToList();

        Assert.Equal([$"terminal-{StoreA}"], terminals);
    }

    [Fact]
    public void The_other_stores_rows_are_not_merely_reordered_but_absent()
    {
        // Fetching by primary key must not slip past the filter either: a
        // caller who knows the id of another store's row still gets nothing.
        using var context = _database.NewContext(enforceForeignKeys: false, storeId: StoreA);

        var theirs = context.Terminals.SingleOrDefault(t => t.TerminalId == $"terminal-{StoreB}");

        Assert.Null(theirs);
    }

    [Fact]
    public void No_current_store_means_no_store_scoped_rows()
    {
        // Fail closed. A filter that opened up when unconfigured would leak
        // every store's data the first time someone forgot to set it, and it
        // would do so silently.
        using var context = _database.NewContext(enforceForeignKeys: false);

        Assert.Empty(context.Terminals);
    }

    [Fact]
    public void A_row_belonging_to_no_store_is_visible_to_every_store()
    {
        // promotions and processing_log allow a null store, and those rows mean
        // "every store" rather than "no store".
        using var contextA = _database.NewContext(enforceForeignKeys: false, storeId: StoreA);
        using var contextB = _database.NewContext(enforceForeignKeys: false, storeId: StoreB);

        // Materialised before ordering: a StringComparer cannot be translated
        // to SQL, and the order is only here to make the assertion stable.
        var seenByA = contextA.Promotions.Select(p => p.PromotionId).ToList()
            .Order(StringComparer.Ordinal).ToList();
        var seenByB = contextB.Promotions.Select(p => p.PromotionId).ToList();

        Assert.Equal(["promo-every-store", "promo-store-a"], seenByA);
        Assert.Equal(["promo-every-store"], seenByB);
    }

    [Fact]
    public void Every_table_with_a_store_id_column_is_actually_filtered()
    {
        // The mistake this catches: a new table gains store_id and nobody marks
        // its entity IStoreScoped. Nothing else would notice — the entity maps,
        // the tests pass, and the table is readable by every store.
        using var context = _database.NewContext(enforceForeignKeys: false);

        var unfiltered = context.Model.GetEntityTypes()
            .Where(entityType => entityType.GetProperties()
                .Any(property => property.GetColumnName() == "store_id"))
            .Where(entityType => entityType.GetDeclaredQueryFilters().Count == 0)
            .Select(entityType => entityType.ClrType.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unfiltered.Count == 0,
            "These entities have a store_id column but no store filter, so every store "
            + "can read every row of them. Mark them IStoreScoped:\n  "
            + string.Join("\n  ", unfiltered));
    }

    [Fact]
    public void The_filter_covers_every_store_scoped_entity()
    {
        using var context = _database.NewContext(enforceForeignKeys: false);

        var filtered = context.Model.GetEntityTypes()
            .Count(entityType => typeof(IStoreScoped).IsAssignableFrom(entityType.ClrType)
                                 && entityType.GetDeclaredQueryFilters().Count > 0);

        Assert.Equal(18, filtered);
    }
}
