using Waymark.Generator.Randomness;

namespace Waymark.Generator.Tests;

/// <summary>
/// Addressed randomness (D-046 §10). The property the whole generator rests on is that a
/// value depends on its address and nothing else — not on what was drawn before it.
/// </summary>
public sealed class RandomnessTests
{
    private const int Draws = 100_000;

    [Fact]
    public void The_same_address_always_gives_the_same_value()
    {
        var first = new RandomSource(42).Stream("demand");
        var second = new RandomSource(42).Stream("demand");

        // Drawing other addresses in between must change nothing: there is no sequence.
        _ = first.Uniform(99, 99);
        _ = new RandomSource(42).Stream("arrivals").Uniform(17, 3);

        Assert.Equal(first.Uniform(17, 3), second.Uniform(17, 3));
    }

    [Fact]
    public void The_algorithm_is_pinned()
    {
        // A golden value, on purpose. Changing the mixing changes every generated store for
        // every seed, which is a decision to record, not a refactor to slip in. The value was
        // recomputed independently from the published FNV-1a and SplitMix64 definitions.
        Assert.Equal(0x2C18374A8D811272UL, new RandomSource(42).Stream("demand").Bits(17, 3));

        // SplitMix64's reference output for a zero state, from the algorithm's authors.
        Assert.Equal(0xE220A8397B1DCDAFUL, Mixing.SplitMix64(0));
    }

    [Fact]
    public void Seed_stream_and_coordinates_each_change_the_value()
    {
        var baseline = new RandomSource(42).Stream("demand").Bits(17, 3);

        Assert.NotEqual(baseline, new RandomSource(43).Stream("demand").Bits(17, 3));
        Assert.NotEqual(baseline, new RandomSource(42).Stream("arrivals").Bits(17, 3));
        Assert.NotEqual(baseline, new RandomSource(42).Stream("demand").Bits(3, 17));
        Assert.NotEqual(baseline, new RandomSource(42).Stream("demand").Bits(17, 4));
        Assert.NotEqual(new RandomSource(42).Stream("demand").Bits(17), new RandomSource(42).Stream("demand").Bits(17, 0));
    }

    [Fact]
    public void Uniform_draws_are_strictly_inside_the_unit_interval_and_uniform()
    {
        var stream = new RandomSource(7).Stream("uniformity");
        var values = Enumerable.Range(0, Draws).Select(i => stream.Uniform(i)).ToList();

        Assert.All(values, value => Assert.InRange(value, double.Epsilon, 1 - 1e-17));
        Assert.InRange(values.Average(), 0.497, 0.503);
        Assert.InRange(values.Select(v => (v - 0.5) * (v - 0.5)).Average(), (1.0 / 12) - 0.002, (1.0 / 12) + 0.002);

        // Ten equal bins, each within 5% of its expected tenth.
        var bins = values.GroupBy(v => (int)(v * 10)).ToDictionary(g => g.Key, g => g.Count());
        Assert.All(Enumerable.Range(0, 10), bin => Assert.InRange(bins[bin], Draws / 10 * 0.95, Draws / 10 * 1.05));
    }

    [Fact]
    public void Streams_and_neighbouring_coordinates_are_uncorrelated()
    {
        var source = new RandomSource(7);
        var demand = source.Stream("demand");
        var arrivals = source.Stream("arrivals");

        var a = Enumerable.Range(0, Draws).Select(i => demand.Uniform(i)).ToArray();
        var b = Enumerable.Range(0, Draws).Select(i => arrivals.Uniform(i)).ToArray();
        var next = Enumerable.Range(1, Draws).Select(i => demand.Uniform(i)).ToArray();

        Assert.InRange(Correlation(a, b), -0.02, 0.02);
        Assert.InRange(Correlation(a, next), -0.02, 0.02);
    }

    [Fact]
    public void A_derived_seed_is_non_negative_and_follows_the_master_seed()
    {
        Assert.InRange(new RandomSource(42).DeriveSeed("ids"), 0, int.MaxValue);
        Assert.Equal(new RandomSource(42).DeriveSeed("ids"), new RandomSource(42).DeriveSeed("ids"));
        Assert.NotEqual(new RandomSource(42).DeriveSeed("ids"), new RandomSource(43).DeriveSeed("ids"));
    }

    // ------------------------------------------------------------ distributions

    [Theory]
    [InlineData(0.03)]
    [InlineData(0.5)]
    public void Bernoulli_hits_its_probability(double probability)
    {
        var stream = new RandomSource(1).Stream("bernoulli");
        var rate = Enumerable.Range(0, Draws).Count(i => Distributions.Bernoulli(stream.Uniform(i), probability)) / (double)Draws;

        Assert.InRange(rate, probability - 0.006, probability + 0.006);
    }

    [Fact]
    public void Uniform_integers_cover_both_ends_evenly()
    {
        var stream = new RandomSource(1).Stream("int");
        var counts = Enumerable.Range(0, Draws)
            .GroupBy(i => Distributions.UniformInt(stream.Uniform(i), 3, 7))
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal([3, 4, 5, 6, 7], counts.Keys.Order());
        Assert.All(counts.Values, count => Assert.InRange(count, Draws / 5 * 0.95, Draws / 5 * 1.05));
        Assert.Equal(7, Distributions.UniformInt(1 - 1e-16, 3, 7));
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(4.0)]
    [InlineData(25.0)]
    [InlineData(59.0)]
    [InlineData(120.0)]
    public void Poisson_has_the_right_mean_and_variance(double lambda)
    {
        // 59 is the last mean sampled by exact inversion and 120 is on the normal
        // approximation, so both samplers are held to the same two moments.
        var stream = new RandomSource(1).Stream("poisson");
        var samples = Enumerable.Range(0, Draws).Select(i => (double)Distributions.Poisson(stream.Uniform(i), lambda)).ToList();

        var mean = samples.Average();
        var variance = samples.Select(s => (s - mean) * (s - mean)).Average();

        Assert.InRange(mean, lambda * 0.98 - 0.01, lambda * 1.02 + 0.01);
        Assert.InRange(variance, lambda * 0.95 - 0.01, lambda * 1.05 + 0.01);
        Assert.All(samples, s => Assert.True(s >= 0));
    }

    [Fact]
    public void Poisson_of_zero_is_zero_and_a_negative_mean_is_refused()
    {
        Assert.Equal(0, Distributions.Poisson(0.999, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Distributions.Poisson(0.5, -1));
    }

    [Fact]
    public void The_normal_quantile_matches_known_values()
    {
        Assert.Equal(0.0, Distributions.StandardNormal(0.5), 9);
        Assert.Equal(1.959963985, Distributions.StandardNormal(0.975), 6);
        Assert.Equal(-2.326347874, Distributions.StandardNormal(0.01), 6);
        Assert.Equal(3.090232306, Distributions.StandardNormal(0.999), 6);
    }

    [Fact]
    public void Normal_samples_have_the_requested_mean_and_spread()
    {
        var stream = new RandomSource(1).Stream("normal");
        var samples = Enumerable.Range(0, Draws).Select(i => Distributions.Normal(stream.Uniform(i), 10, 2)).ToList();
        var mean = samples.Average();
        var sd = Math.Sqrt(samples.Select(s => (s - mean) * (s - mean)).Average());

        Assert.InRange(mean, 9.97, 10.03);
        Assert.InRange(sd, 1.97, 2.03);
    }

    [Fact]
    public void Triangular_stays_in_bounds_with_the_right_mean()
    {
        var stream = new RandomSource(1).Stream("triangular");
        var samples = Enumerable.Range(0, Draws).Select(i => Distributions.Triangular(stream.Uniform(i), 1, 2, 6)).ToList();

        Assert.All(samples, s => Assert.InRange(s, 1, 6));
        Assert.InRange(samples.Average(), 3.0 - 0.03, 3.0 + 0.03);
    }

    [Fact]
    public void A_weighted_table_picks_in_proportion_and_never_picks_a_zero_weight()
    {
        var table = new WeightedTable([1, 0, 3, 0]);
        var stream = new RandomSource(1).Stream("weighted");
        var counts = new int[4];
        for (var i = 0; i < Draws; i++)
        {
            counts[table.Pick(stream.Uniform(i))]++;
        }

        Assert.Equal(0, counts[1]);
        Assert.Equal(0, counts[3]);
        Assert.InRange(counts[2] / (double)Draws, 0.74, 0.76);
        Assert.Equal(2, table.Pick(1 - 1e-16));
    }

    [Fact]
    public void A_weighted_table_refuses_weights_that_cannot_be_chosen_from()
    {
        Assert.Throws<ArgumentException>(() => new WeightedTable([]));
        Assert.Throws<ArgumentException>(() => new WeightedTable([0, 0]));
        Assert.Throws<ArgumentException>(() => new WeightedTable([1, -1]));
    }

    private static double Correlation(double[] x, double[] y)
    {
        var mx = x.Average();
        var my = y.Average();
        var covariance = x.Zip(y, (a, b) => (a - mx) * (b - my)).Sum();
        return covariance / Math.Sqrt(x.Sum(a => (a - mx) * (a - mx)) * y.Sum(b => (b - my) * (b - my)));
    }
}
