using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// A rate in hundredths of a percent, matching how the schema stores every
/// percentage. Wrapping it stops a raw 19 being passed where 1900 was meant —
/// a mistake no test would catch, because both are plausible integers.
/// </summary>
public sealed class BasisPointsTests
{
    [Fact]
    public void The_named_rates_are_the_Algerian_TVA_rates()
    {
        Assert.Equal(1_900, BasisPoints.StandardVat.Value);
        Assert.Equal(900, BasisPoints.ReducedVat.Value);
        Assert.Equal(0, BasisPoints.Zero.Value);
        Assert.True(BasisPoints.Zero.IsZero);
    }

    [Fact]
    public void The_range_is_the_one_the_schema_already_enforces()
    {
        // CHECK (… BETWEEN 0 AND 10000) appears on categories.tax_rate,
        // promotions.promotion_value and customer_tiers.discount. Failing here
        // rather than at SaveChanges is the only difference.
        Assert.Equal(0, new BasisPoints(0).Value);
        Assert.Equal(10_000, new BasisPoints(10_000).Value);

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new BasisPoints(-1); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new BasisPoints(10_001); });
    }

    [Fact]
    public void Rates_compare_and_sort()
    {
        Assert.True(BasisPoints.ReducedVat < BasisPoints.StandardVat);
        Assert.True(BasisPoints.StandardVat > BasisPoints.ReducedVat);
        Assert.True(BasisPoints.Zero <= BasisPoints.Zero);
        Assert.True(BasisPoints.Zero >= BasisPoints.Zero);
        Assert.Equal(BasisPoints.StandardVat, new BasisPoints(1_900));
        Assert.NotEqual(BasisPoints.StandardVat, BasisPoints.ReducedVat);
    }

    [Fact]
    public void ToString_reads_as_a_percentage()
    {
        Assert.Equal("19%", BasisPoints.StandardVat.ToString());
        Assert.Equal("9%", BasisPoints.ReducedVat.ToString());
        Assert.Equal("0%", BasisPoints.Zero.ToString());
        Assert.Equal("19.25%", new BasisPoints(1_925).ToString());
    }

    [Fact]
    public void The_scale_matches_the_schemas_bound()
    {
        Assert.Equal(10_000, BasisPoints.Scale);
    }
}
