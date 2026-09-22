using Waymark.Domain.Catalogue;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Which TVA rate a product is sold at when its categories are asked (session A1, D-075,
/// answering O-24).
///
/// <para>
/// Every failure here is silent. A rate that is one step wrong prints a receipt that adds up,
/// balances a drawer that reconciles, and files a TVA return that is short. The one to watch
/// is the first section: a rate the categories <i>agreed</i> on must come back untouched. If
/// the catch-all reaches a product whose catalogue was fine, every 9% product in the shop —
/// bread, milk, pharmacy — is taxed at 19% and nothing in the system says so.
/// </para>
///
/// <para>
/// No database, no clock, no store: the rule is pure, so these tests open nothing.
/// </para>
/// </summary>
public sealed class TvaRateTests
{
    private static readonly BasisPoints Standard = BasisPoints.StandardVat; // 19%
    private static readonly BasisPoints Reduced = BasisPoints.ReducedVat;   // 9%

    // ------------------------------------- the catalogue answered (case 1)

    [Fact]
    public void One_category_with_a_rate_is_that_rate()
    {
        var resolved = TvaRate.Resolve([Reduced]);

        Assert.Equal(Reduced, resolved.Rate);
        Assert.Equal(TvaRateSource.FromCategory, resolved.Source);
    }

    [Fact]
    public void Two_categories_that_state_the_same_rate_agree()
    {
        // Two rows, one rate. A product sits in several categories for merchandising
        // reasons; that is not a disagreement and must not reach the catch-all.
        var resolved = TvaRate.Resolve([Reduced, Reduced]);

        Assert.Equal(Reduced, resolved.Rate);
        Assert.Equal(TvaRateSource.FromCategory, resolved.Source);
    }

    [Fact]
    public void The_standard_rate_stated_by_a_category_is_not_the_fallback()
    {
        // The figure is the same either way, which is exactly why the source has to be
        // right: this is the test that fails when Source is derived from Rate.
        var resolved = TvaRate.Resolve([Standard]);

        Assert.Equal(Standard, resolved.Rate);
        Assert.Equal(TvaRateSource.FromCategory, resolved.Source);
    }

    // ------------------------------- the catalogue did not answer (2, 3, 4)

    [Fact]
    public void A_product_in_no_category_takes_the_standard_rate()
    {
        var resolved = TvaRate.Resolve([]);

        Assert.Equal(Standard, resolved.Rate);
        Assert.Equal(TvaRateSource.StandardFallback, resolved.Source);
    }

    [Fact]
    public void A_category_with_no_rate_takes_the_standard_rate()
    {
        var resolved = TvaRate.Resolve([null]);

        Assert.Equal(Standard, resolved.Rate);
        Assert.Equal(TvaRateSource.StandardFallback, resolved.Source);
    }

    [Fact]
    public void A_silent_category_beside_a_reduced_one_still_takes_the_standard_rate()
    {
        // D-075's words: "one potential category has no specified rate" is a catch-all case.
        // The silent category might have been the 19% one, so 9% cannot be assumed.
        // Dropping nulls before counting — which the skeleton did — answers 9% here.
        var resolved = TvaRate.Resolve([Reduced, null]);

        Assert.Equal(Standard, resolved.Rate);
        Assert.Equal(TvaRateSource.StandardFallback, resolved.Source);
    }

    [Fact]
    public void Categories_that_disagree_take_the_standard_rate()
    {
        // O-24's original question. The 9% list is restrictive (CTCA art. 21, 23): a product
        // that does not sit cleanly inside it is standard-rated.
        var resolved = TvaRate.Resolve([Reduced, Standard]);

        Assert.Equal(Standard, resolved.Rate);
        Assert.Equal(TvaRateSource.StandardFallback, resolved.Source);
    }

    [Fact]
    public void A_silent_category_beside_a_standard_one_is_still_the_fallback()
    {
        // 19% either way, and the catalogue is still broken. Without this, a product with a
        // missing rate is indistinguishable from a correctly classified one, and block E's
        // catalogue health list would never show it.
        var resolved = TvaRate.Resolve([Standard, null]);

        Assert.Equal(Standard, resolved.Rate);
        Assert.Equal(TvaRateSource.StandardFallback, resolved.Source);
    }

    [Fact]
    public void Order_does_not_matter()
    {
        // product_category has no ordering and the query imposes none, so the same set of
        // categories read in another order must not resolve differently.
        Assert.Equal(TvaRate.Resolve([Reduced, null]), TvaRate.Resolve([null, Reduced]));
        Assert.Equal(TvaRate.Resolve([Reduced, Standard]), TvaRate.Resolve([Standard, Reduced]));
    }

    [Fact]
    public void A_rate_that_is_neither_of_the_two_legal_ones_is_still_honoured()
    {
        // The rule is about agreement, not about which rates are legal. A zero-rated or
        // exempt category (0%) is a real case, and silently promoting it to 19% would charge
        // TVA on something the law exempts.
        var resolved = TvaRate.Resolve([BasisPoints.Zero]);

        Assert.Equal(BasisPoints.Zero, resolved.Rate);
        Assert.Equal(TvaRateSource.FromCategory, resolved.Source);
    }

    [Fact]
    public void A_null_list_is_not_an_empty_one()
    {
        // No categories is a fact about the product. No list is a bug in the caller, and the
        // two must not look alike.
        Assert.Throws<ArgumentNullException>(() => TvaRate.Resolve(null!));
    }
}
