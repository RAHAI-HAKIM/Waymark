using System.Reflection;
using Waymark.Domain.Engine;
using Waymark.Domain.Organisation;

namespace Waymark.Domain.Tests;

/// <summary>
/// Who may do what, from <c>roles.rank</c> (session A2).
///
/// <para>
/// The same comparison was written backwards once already, in session D (D-074): a cashier
/// could decide a manager's card and an owner could not, and four tests caught it in seconds.
/// Inverted here it is worse than a wrong answer — <b>nothing refuses</b>. A cashier
/// discounts, overrides prices and voids sales all day, every receipt prints, every total
/// adds up, and the first sign is the month's takings.
/// </para>
///
/// <para>
/// These tests pin the <i>mechanism</i>, not the ladder. Which rank each capability needs is
/// Hakim's to set and a shop's to define, so nothing here asserts a particular number beyond
/// what the build plan already promises.
/// </para>
/// </summary>
public sealed class StaffPermissionsTests
{
    private static readonly Capability[] All = Enum.GetValues<Capability>();

    // ------------------------------------------------- the ladder itself

    [Fact]
    public void Every_capability_has_a_required_rank()
    {
        // An unmapped member must not fall through to a default. A capability that
        // accidentally required rank zero would be available to the whole shop, and nothing
        // would report it.
        foreach (var capability in All)
        {
            var rank = StaffPermissions.RequiredRank(capability);
            Assert.True(rank > 0, $"{capability} requires rank {rank}; roles.rank is always positive (ck_roles_rank).");
        }
    }

    [Fact]
    public void An_unknown_capability_throws_rather_than_defaulting()
    {
        // The value a new enum member would have before anyone maps it.
        var unmapped = (Capability)(All.Length + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => StaffPermissions.RequiredRank(unmapped));
    }

    [Fact]
    public void Nothing_outside_the_class_can_change_the_ladder()
    {
        // `readonly` on a field protects the reference, not the contents: a public readonly
        // Dictionary can still be written to from any project in the solution, and
        // `PermissionMapping[OverridePrice] = 1` would hand price overrides to every cashier
        // with no error anywhere. The only public way in is RequiredRank, which reads.
        var exposed = typeof(StaffPermissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.Name);

        Assert.Empty(exposed);
    }

    // -------------------------------------------------- the comparison

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Exactly_the_required_rank_is_allowed_and_so_is_anything_above_it(int above)
    {
        // "This rank and anything above it" — an owner locked out of what their manager may
        // do is not a shop anybody would run.
        foreach (var capability in All)
        {
            var required = StaffPermissions.RequiredRank(capability);

            Assert.True(
                StaffPermissions.May(required + above, capability),
                $"rank {required + above} was refused {capability}, which needs {required}.");
        }
    }

    [Fact]
    public void Below_the_required_rank_is_refused()
    {
        foreach (var capability in All)
        {
            var required = StaffPermissions.RequiredRank(capability);
            if (required <= 1)
            {
                continue; // nothing is below the most junior rank there is
            }

            Assert.False(
                StaffPermissions.May(required - 1, capability),
                $"rank {required - 1} was allowed {capability}, which needs {required}.");
        }
    }

    // ------------------------------------------------- absence (D-037)

    [Fact]
    public void No_rank_at_all_is_never_permission()
    {
        // Null is what RecommendationBoard.StaffAsync returns for somebody who is not active
        // staff, or whose role is not an active row in `roles`. Reading it as zero would make
        // them the most junior person in the shop rather than an error, and a junior person
        // is still somebody. Absence is never zero (D-037).
        foreach (var capability in All)
        {
            Assert.False(
                StaffPermissions.May(null, capability),
                $"somebody with no rank was allowed {capability}.");
        }
    }

    // ------------------------------------------- one rule, not two copies

    [Fact]
    public void Deciding_a_card_asks_the_same_question_CardAudience_does()
    {
        // D-074's check and this one are the same comparison. If they ever disagree, one of
        // them has been edited alone — which is exactly how the API, the till and Admin end
        // up with three different answers to "may this person do that".
        var required = StaffPermissions.RequiredRank(Capability.DecideRecommendation);

        for (var rank = 1; rank <= required + 2; rank++)
        {
            Assert.Equal(
                CardAudience.MayDecide(rank, required),
                StaffPermissions.May(rank, Capability.DecideRecommendation));
        }
    }

    // --------------------------------------- what the build plan promises

    [Fact]
    public void The_manager_gated_actions_ask_for_more_than_the_shop_floor()
    {
        // Not an invented ladder: B5 is "price override behind a manager PIN" and B8 gates
        // voids, so whatever the shop's ranks are, these two cannot sit at the bottom of
        // them. The lowest rank in a shop is 1 (ck_roles_rank).
        Assert.True(
            StaffPermissions.RequiredRank(Capability.OverridePrice) > 1,
            "A price override is behind a manager PIN (phase-1-plan B5).");

        Assert.True(
            StaffPermissions.RequiredRank(Capability.VoidTransaction) > 1,
            "Voids are audited and gated (phase-1-plan B8).");
    }

    [Fact]
    public void A_no_sale_is_gated_too()
    {
        // `ICashDrawer.NoSale` is documented as "permission-gated, and audited". Opening the
        // drawer with no sale behind it is the oldest way to take money out of one.
        Assert.True(
            StaffPermissions.RequiredRank(Capability.NoSale) > 1,
            "Opening the drawer with no sale behind it is permission-gated (ICashDrawer).");
    }
}
