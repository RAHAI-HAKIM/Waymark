using System.Numerics;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Money's rounding primitives against an independent reference, over many generated cases
/// (Phase 0 final test). The example tests pin the cases someone thought of; these check the
/// rules hold for the ones nobody did: negative amounts, huge quantities, exact halves, odd
/// weights.
///
/// <para>
/// The reference is <see cref="BigInteger"/> arithmetic written from the rule's definition, not
/// from the implementation, and the generator is seeded so a failure reproduces.
/// </para>
/// </summary>
public sealed class MoneyPropertyTests
{
    private const int Cases = 20_000;

    private static Money Dzd(long minorUnits) => new(minorUnits, Currency.Dzd);

    /// <summary>Amounts biased toward the interesting: small, around the cash step, and very large.</summary>
    private static long Amount(Random random) => random.Next(6) switch
    {
        0 => random.NextInt64(-1_000, 1_000),
        1 => (random.NextInt64(-2_000, 2_000) * 250) + random.Next(-1, 2),
        2 => random.NextInt64(-1_000_000_000_000, 1_000_000_000_000),
        3 => random.NextInt64(long.MinValue / 4, long.MaxValue / 4),
        _ => random.NextInt64(-100_000_000, 100_000_000),
    };

    /// <summary>The rule, from its definition: the nearest integer to n/d, with ties by policy.</summary>
    private static BigInteger RoundReference(BigInteger numerator, BigInteger denominator, Rounding rounding)
    {
        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var floor = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (remainder.Sign < 0)
        {
            floor -= 1;
            remainder += denominator;
        }

        // numerator / denominator = floor + remainder / denominator, 0 <= remainder < denominator.
        var twice = remainder * 2;
        if (twice < denominator)
        {
            return floor;
        }

        if (twice > denominator)
        {
            return floor + 1;
        }

        // An exact half: HalfUp goes away from zero, HalfEven to the even neighbour.
        return rounding == Rounding.HalfUp
            ? (numerator.Sign >= 0 ? floor + 1 : floor)
            : (floor.IsEven ? floor : floor + 1);
    }

    [Fact]
    public void Times_rounds_once_to_the_nearest_with_the_policy_breaking_ties()
    {
        var random = new Random(20260917);

        for (var i = 0; i < Cases; i++)
        {
            var amount = Amount(random);
            var numerator = random.Next(3) == 0 ? random.NextInt64(-1_000_000_000, 1_000_000_000) : random.NextInt64(-5_000, 5_000);
            var denominator = random.Next(4) == 0 ? random.NextInt64(1, 3) * (random.Next(2) == 0 ? 1 : -1) : random.NextInt64(1, 100_000);
            var rounding = random.Next(2) == 0 ? Rounding.HalfUp : Rounding.HalfEven;

            var expected = RoundReference((BigInteger)amount * numerator, denominator, rounding);

            if (expected > long.MaxValue || expected < long.MinValue)
            {
                Assert.Throws<OverflowException>(() => Dzd(amount).Times(numerator, denominator, rounding));
                continue;
            }

            var actual = Dzd(amount).Times(numerator, denominator, rounding).MinorUnits;
            Assert.True(
                (BigInteger)actual == expected,
                $"{amount} × {numerator}/{denominator} under {rounding}: got {actual}, the rule gives {expected}.");
        }
    }

    [Fact]
    public void The_two_policies_disagree_only_on_an_exact_half()
    {
        var random = new Random(17);

        for (var i = 0; i < Cases; i++)
        {
            var amount = random.NextInt64(-10_000_000, 10_000_000);
            var denominator = random.NextInt64(1, 1_000);
            var up = Dzd(amount).Times(1, denominator, Rounding.HalfUp).MinorUnits;
            var even = Dzd(amount).Times(1, denominator, Rounding.HalfEven).MinorUnits;

            var exactHalf = BigInteger.Remainder(BigInteger.Abs(amount) * 2, denominator * 2) == denominator;
            if (!exactHalf)
            {
                Assert.Equal(up, even);
            }
            else
            {
                // On a half, HalfEven lands on the even neighbour and HalfUp away from zero; they
                // agree when away from zero is already even (3.5 is 4 either way).
                Assert.Equal(0, even % 2);
                Assert.Equal(Math.Abs(up), Math.Abs(amount) / denominator + 1);
                Assert.True(Math.Abs(up - even) <= 1);
            }
        }
    }

    [Fact]
    public void Allocation_is_exact_proportional_within_one_unit_and_monotonic_in_the_weights()
    {
        var random = new Random(4242);

        for (var i = 0; i < Cases / 4; i++)
        {
            var amount = random.Next(3) == 0 ? random.NextInt64(-1_000_000_000_000, 1_000_000_000_000) : random.NextInt64(-100_000, 100_000);
            var weights = Enumerable.Range(0, random.Next(1, 12))
                .Select(_ => random.Next(4) == 0 ? 0L : random.Next(3) == 0 ? random.NextInt64(1, long.MaxValue / 64) : random.NextInt64(1, 1_000))
                .ToArray();
            if (weights.All(w => w == 0))
            {
                weights[0] = 1;
            }

            var parts = Dzd(amount).Allocate(weights).Select(p => p.MinorUnits).ToArray();
            var total = weights.Aggregate(BigInteger.Zero, (sum, w) => sum + w);

            Assert.Equal((BigInteger)amount, parts.Aggregate(BigInteger.Zero, (sum, p) => sum + p));

            for (var k = 0; k < weights.Length; k++)
            {
                // Within one unit of the exact share, and nothing at all for a zero weight.
                var exactTimesTotal = (BigInteger)amount * weights[k];
                var deviation = BigInteger.Abs(((BigInteger)parts[k] * total) - exactTimesTotal);
                Assert.True(deviation < total, $"Part {k} of {amount} by [{string.Join(",", weights)}] is {parts[k]}, more than a unit from its share.");

                if (weights[k] == 0)
                {
                    Assert.Equal(0, parts[k]);
                }

                // A larger weight never gets less (more, for a negative amount).
                for (var m = 0; m < weights.Length; m++)
                {
                    if (weights[k] > weights[m])
                    {
                        Assert.True(amount >= 0 ? parts[k] >= parts[m] : parts[k] <= parts[m],
                            $"{amount} by [{string.Join(",", weights)}]: weight {weights[k]} got {parts[k]}, weight {weights[m]} got {parts[m]}.");
                    }
                }
            }

            // Deterministic, and a negated amount splits into the negated parts.
            Assert.Equal(parts, Dzd(amount).Allocate(weights).Select(p => p.MinorUnits));
            if (amount != long.MinValue)
            {
                Assert.Equal(parts.Select(p => -p), Dzd(-amount).Allocate(weights).Select(p => p.MinorUnits));
            }
        }
    }

    [Fact]
    public void A_tax_inclusive_split_always_sums_back_and_its_net_is_the_rounded_exact_net()
    {
        var random = new Random(1900);
        int[] rates = [0, 900, 1900, 1, 9999, 10_000];

        for (var i = 0; i < Cases; i++)
        {
            var ttc = random.NextInt64(-100_000_000_000, 100_000_000_000);
            var rate = new BasisPoints(random.Next(3) == 0 ? random.Next(0, 10_001) : rates[random.Next(rates.Length)]);
            var rounding = random.Next(2) == 0 ? Rounding.HalfUp : Rounding.HalfEven;

            var split = Dzd(ttc).SplitTaxInclusive(rate, rounding);

            Assert.Equal(ttc, split.Net.MinorUnits + split.Tax.MinorUnits);
            Assert.Equal(RoundReference((BigInteger)ttc * 10_000, 10_000 + rate.Value, rounding), (BigInteger)split.Net.MinorUnits);

            // The tax has the amount's sign (or is zero), and is never more than the exact tax plus half a unit.
            Assert.True(split.Tax.MinorUnits == 0 || Math.Sign(split.Tax.MinorUnits) == Math.Sign(ttc));
            var exactTaxTimesDenominator = (BigInteger)ttc * rate.Value;
            var taxDeviation = BigInteger.Abs(((BigInteger)split.Tax.MinorUnits * (10_000 + rate.Value)) - exactTaxTimesDenominator);
            Assert.True(taxDeviation * 2 <= 10_000 + rate.Value, $"{ttc} at {rate}: tax {split.Tax.MinorUnits} is more than half a unit off.");
        }
    }

    [Fact]
    public void Cash_tender_is_the_nearest_step_ties_away_from_zero_and_symmetric()
    {
        var random = new Random(500);
        var step = Currency.Dzd.CashRoundingStep;

        for (var i = 0; i < Cases; i++)
        {
            var amount = random.Next(2) == 0 ? (random.NextInt64(-400_000, 400_000) * 250) + random.Next(-3, 4) : random.NextInt64(-1_000_000_000_000, 1_000_000_000_000);
            var tender = Dzd(amount).ToCashTender();

            Assert.Equal(0, tender.Tendered.MinorUnits % step);
            Assert.Equal(amount, tender.Tendered.MinorUnits - tender.Variance.MinorUnits);
            Assert.True(Math.Abs(tender.Variance.MinorUnits) * 2 <= step);
            Assert.Equal(tender.Variance.MinorUnits != 0, tender.HasVariance);

            var expected = RoundReference(amount, step, Rounding.HalfUp) * step;
            Assert.Equal(expected, (BigInteger)tender.Tendered.MinorUnits);

            var mirrored = Dzd(-amount).ToCashTender();
            Assert.Equal(-tender.Tendered.MinorUnits, mirrored.Tendered.MinorUnits);
            Assert.Equal(-tender.Variance.MinorUnits, mirrored.Variance.MinorUnits);
        }
    }

    [Fact]
    public void Arithmetic_at_the_edge_of_the_range_throws_instead_of_wrapping()
    {
        var max = Dzd(long.MaxValue);
        var min = Dzd(long.MinValue);

        Assert.Throws<OverflowException>(() => max + Dzd(1));
        Assert.Throws<OverflowException>(() => min - Dzd(1));
        Assert.Throws<OverflowException>(() => -min);
        Assert.Throws<OverflowException>(() => min.Abs());
        Assert.Throws<OverflowException>(() => max * 2);
        Assert.Throws<OverflowException>(() => max.Times(3, 2, Rounding.HalfUp));
        Assert.Throws<OverflowException>(() => max.ToCashTender());
        Assert.Throws<OverflowException>(() => min.ToCashTender());
        Assert.Equal(long.MaxValue, max.Times(long.MaxValue, long.MaxValue, Rounding.HalfEven).MinorUnits);
        Assert.Equal([long.MinValue, 0], min.Allocate([1, 0]).Select(p => p.MinorUnits));
    }
}
