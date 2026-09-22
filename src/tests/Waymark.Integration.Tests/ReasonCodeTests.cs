using Waymark.Domain;
using Waymark.Domain.Enums;
using Waymark.Domain.Reference;
using Waymark.Domain.Values;
using Waymark.Persistence.Reference;

namespace Waymark.Integration.Tests;

/// <summary>
/// The reasons a shop offers for an action (session A3).
///
/// <para>
/// These matter because every column recording <i>why</i> something happened is a foreign key
/// into <c>reason_codes</c>. A list that offers a retired code, or a code belonging to a
/// different kind of action, does not look wrong on screen — it fails at
/// <c>SaveChanges</c>, in the middle of a sale, in front of a customer.
/// </para>
/// </summary>
public sealed class ReasonCodeTests(MigratedDatabaseFixture database) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly DateTimeOffset Moment = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Codes carry a unique suffix because the fixture's database is shared by the whole
    /// class, and every query here filters on a kind rather than on a store: the vocabulary
    /// is the tenant's, so there is no store to separate one test's rows from another's.
    /// </summary>
    private sealed class Vocabulary
    {
        private readonly MigratedDatabaseFixture _database;
        private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

        public Vocabulary(MigratedDatabaseFixture database) => _database = database;

        public string Add(
            ReasonCodeAppliesTo appliesTo,
            string name,
            long displayOrder = 0,
            bool active = true,
            bool requiresNote = false,
            bool requiresManager = false)
        {
            var code = $"{name}-{_suffix}";

            using var context = _database.NewContext();
            context.ReasonCodes.Add(new ReasonCode
            {
                ReasonCodeValue = code,
                AppliesTo = appliesTo,
                LabelAr = $"ar {name}",
                LabelFr = $"fr {name}",
                DisplayOrder = displayOrder,
                IsActive = active,
                RequiresNote = requiresNote,
                RequiresManager = requiresManager,
                CreatedAt = Moment,
            });
            context.SaveChanges();

            return code;
        }

        /// <summary>Only the codes this instance created, in the order the reader returned them.</summary>
        public async Task<IReadOnlyList<ReasonCodeChoice>> Offered(ReasonCodeAppliesTo appliesTo)
        {
            using var context = _database.NewContext();
            var all = await new ReasonCodes(context).ForAsync(appliesTo);

            return [.. all.Where(choice => choice.Code.EndsWith(_suffix, StringComparison.Ordinal))];
        }
    }

    [Fact]
    public async Task A_shops_reasons_come_back_for_the_kind_they_belong_to()
    {
        var vocabulary = new Vocabulary(database);
        var discount = vocabulary.Add(ReasonCodeAppliesTo.Discount, "DISC");
        vocabulary.Add(ReasonCodeAppliesTo.Void, "ANNUL");

        var offered = await vocabulary.Offered(ReasonCodeAppliesTo.Discount);

        Assert.Equal(discount, Assert.Single(offered).Code);
    }

    [Fact]
    public async Task A_reason_for_another_kind_never_appears()
    {
        // The failure this prevents: a void reason offered in the discount dialog writes a
        // code the column's foreign key accepts — reason_codes has one primary key across
        // every kind — so nothing refuses it, and the sale is recorded with a reason that
        // means something else entirely.
        var vocabulary = new Vocabulary(database);
        vocabulary.Add(ReasonCodeAppliesTo.Void, "ANNUL");

        Assert.Empty(await vocabulary.Offered(ReasonCodeAppliesTo.Discount));
    }

    [Fact]
    public async Task A_retired_reason_is_not_offered()
    {
        // is_active is how a shop stops a reason being used without breaking the rows that
        // already reference it.
        var vocabulary = new Vocabulary(database);
        vocabulary.Add(ReasonCodeAppliesTo.Discount, "OLD", active: false);
        var live = vocabulary.Add(ReasonCodeAppliesTo.Discount, "LIVE");

        var offered = await vocabulary.Offered(ReasonCodeAppliesTo.Discount);

        Assert.Equal(live, Assert.Single(offered).Code);
    }

    [Fact]
    public async Task Reasons_come_back_in_display_order()
    {
        var vocabulary = new Vocabulary(database);
        var third = vocabulary.Add(ReasonCodeAppliesTo.Discount, "C", displayOrder: 30);
        var first = vocabulary.Add(ReasonCodeAppliesTo.Discount, "A", displayOrder: 10);
        var second = vocabulary.Add(ReasonCodeAppliesTo.Discount, "B", displayOrder: 20);

        var offered = await vocabulary.Offered(ReasonCodeAppliesTo.Discount);

        Assert.Equal([first, second, third], offered.Select(choice => choice.Code));
    }

    [Fact]
    public async Task Reasons_that_share_a_display_order_are_still_in_a_fixed_order()
    {
        // display_order defaults to 0, so a shop that never set one leaves every row tied and
        // SQLite may return ties in any order it likes. A dialog that reshuffles between two
        // openings is one a cashier stops reading, and picking by position then picks wrongly.
        var vocabulary = new Vocabulary(database);
        var zebra = vocabulary.Add(ReasonCodeAppliesTo.Return, "ZEBRA");
        var alpha = vocabulary.Add(ReasonCodeAppliesTo.Return, "ALPHA");

        var once = await vocabulary.Offered(ReasonCodeAppliesTo.Return);
        var twice = await vocabulary.Offered(ReasonCodeAppliesTo.Return);

        Assert.Equal([alpha, zebra], once.Select(choice => choice.Code));
        Assert.Equal(once.Select(choice => choice.Code), twice.Select(choice => choice.Code));
    }

    [Fact]
    public async Task A_kind_the_shop_has_no_reasons_for_is_empty_and_not_an_error()
    {
        // A shop that never configured a write-off reason has none. Whether a write-off may
        // then go ahead is the caller's rule; this list only reports what the shop said.
        var vocabulary = new Vocabulary(database);
        vocabulary.Add(ReasonCodeAppliesTo.Discount, "DISC");

        Assert.Empty(await vocabulary.Offered(ReasonCodeAppliesTo.WriteOff));
    }

    [Fact]
    public async Task Every_field_the_caller_needs_crosses()
    {
        var vocabulary = new Vocabulary(database);
        var code = vocabulary.Add(
            ReasonCodeAppliesTo.PriceOverride, "GESTE", requiresNote: true, requiresManager: true);

        var choice = Assert.Single(await vocabulary.Offered(ReasonCodeAppliesTo.PriceOverride));

        Assert.Equal(code, choice.Code);
        Assert.Equal("ar GESTE", choice.LabelAr);
        Assert.Equal("fr GESTE", choice.LabelFr);
        Assert.True(choice.RequiresNote);
        Assert.True(choice.RequiresManager);
    }

    [Fact]
    public async Task The_flags_are_carried_as_the_shop_set_them()
    {
        // Both default to false, and defaulting them to true "to be safe" would put a manager
        // in front of every discount in the shop. A3 reports; it does not decide.
        var vocabulary = new Vocabulary(database);
        vocabulary.Add(ReasonCodeAppliesTo.Adjustment, "PLAIN");

        var choice = Assert.Single(await vocabulary.Offered(ReasonCodeAppliesTo.Adjustment));

        Assert.False(choice.RequiresNote);
        Assert.False(choice.RequiresManager);
    }

    [Fact]
    public async Task The_vocabulary_is_the_tenants_and_not_a_stores()
    {
        // reason_codes has no store_id (ParentScopeTests calls it vocabulary), so a context
        // built for one store sees the same reasons as a context built for another. This is
        // the opposite of `prices`, where the global filter silently narrows the answer — and
        // "no filter" and "a filter somebody forgot" look identical in a query.
        var vocabulary = new Vocabulary(database);
        var code = vocabulary.Add(ReasonCodeAppliesTo.CashMovement, "CAISSE");

        using var one = database.NewContext(storeId: "store-one");
        using var other = database.NewContext(storeId: "store-two");

        var seenByOne = await new ReasonCodes(one).ForAsync(ReasonCodeAppliesTo.CashMovement);
        var seenByOther = await new ReasonCodes(other).ForAsync(ReasonCodeAppliesTo.CashMovement);

        Assert.Contains(seenByOne, choice => choice.Code == code);
        Assert.Contains(seenByOther, choice => choice.Code == code);
    }
}
