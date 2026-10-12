using Waymark.Domain.Organisation;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// The block C review's two small rules (D-119, D-121). The silent failures: a blind count that
/// still stands after cash has moved, so the close records a sale as a shortage; one that no longer
/// stands though nothing moved, so the close can be asked again with another figure; a note of any
/// length stored and sent back with every read.
/// </summary>
public sealed class BlockCReviewRulesTests
{
    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    [Fact]
    public void A_blind_count_stands_while_cash_has_not_moved_since_it_was_kept()
    {
        Assert.Equal(Dzd(4_800_000), Drawer.StandingCount(Dzd(4_800_000), 12, 12));
        Assert.Equal(Dzd(0), Drawer.StandingCount(Dzd(0), 0, 0)); // an empty drawer counted is a count
    }

    [Theory]
    [InlineData(13L)] // one more cash sale
    [InlineData(11L)] // or one fewer row than there was: not the same drawer either
    public void It_no_longer_stands_once_cash_has_moved(long movesNow)
    {
        Assert.Null(Drawer.StandingCount(Dzd(4_800_000), 12, movesNow));
    }

    [Fact]
    public void No_count_kept_is_no_count_standing()
    {
        Assert.Null(Drawer.StandingCount(null, null, 0));
        Assert.Null(Drawer.StandingCount(null, 12, 12));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void A_note_is_five_hundred_characters_at_most(int length, bool fits)
    {
        Assert.Equal(fits, SaleLimits.NoteFits(new string('x', length)));
    }

    [Fact]
    public void No_note_fits_and_the_spaces_around_one_are_not_counted()
    {
        Assert.True(SaleLimits.NoteFits(null));
        Assert.True(SaleLimits.NoteFits("  " + new string('x', 500) + "  "));
    }
}
