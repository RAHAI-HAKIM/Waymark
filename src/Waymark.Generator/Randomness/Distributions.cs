namespace Waymark.Generator.Randomness;

/// <summary>
/// Distributions as pure functions of one uniform draw.
///
/// <para>
/// Each takes the <c>u</c> a <see cref="RandomStream"/> produced for a coordinate, so a
/// sampled value is as addressed as the draw it came from, and each is testable with no
/// stream at all. One draw per sample, always: an algorithm that needs a variable number of
/// draws would make the coordinates of every later sample depend on this one.
/// </para>
/// </summary>
internal static class Distributions
{
    /// <summary>Above this mean the Poisson sampler switches from exact inversion to a normal approximation.</summary>
    public const double PoissonInversionLimit = 60.0;

    /// <summary>True with probability <paramref name="probability"/>.</summary>
    public static bool Bernoulli(double u, double probability)
    {
        if (probability is < 0 or > 1 || double.IsNaN(probability))
        {
            throw new ArgumentOutOfRangeException(nameof(probability), probability, "A probability is between 0 and 1.");
        }

        return u < probability;
    }

    /// <summary>A whole number from <paramref name="minInclusive"/> to <paramref name="maxInclusive"/>, each equally likely.</summary>
    public static int UniformInt(double u, int minInclusive, int maxInclusive)
    {
        if (maxInclusive < minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInclusive), maxInclusive, "The range is empty.");
        }

        var span = (long)maxInclusive - minInclusive + 1;
        return (int)(minInclusive + Math.Min(span - 1, (long)Math.Floor(u * span)));
    }

    /// <summary>A real number uniformly between <paramref name="min"/> and <paramref name="max"/>.</summary>
    public static double Uniform(double u, double min, double max)
    {
        if (max < min)
        {
            throw new ArgumentOutOfRangeException(nameof(max), max, "The range is empty.");
        }

        return min + (u * (max - min));
    }

    /// <summary>
    /// A Poisson count with mean <paramref name="lambda"/>.
    ///
    /// <para>
    /// Exact inversion up to <see cref="PoissonInversionLimit"/>, which covers every
    /// per-variant daily demand in a shop. Above it, a normal approximation with continuity
    /// correction, sampled from the same single draw: its error is below a percent of the
    /// mean at that size, and it keeps one draw per sample.
    /// </para>
    /// </summary>
    public static int Poisson(double u, double lambda)
    {
        if (lambda < 0 || double.IsNaN(lambda) || double.IsInfinity(lambda))
        {
            throw new ArgumentOutOfRangeException(nameof(lambda), lambda, "A Poisson mean is a finite number of at least 0.");
        }

        if (lambda == 0)
        {
            return 0;
        }

        if (lambda > PoissonInversionLimit)
        {
            var approximate = Math.Floor(lambda + (Math.Sqrt(lambda) * StandardNormal(u)) + 0.5);
            return (int)Math.Max(0, approximate);
        }

        var probability = Math.Exp(-lambda);
        var cumulative = probability;
        var count = 0;
        var limit = (int)Math.Ceiling((10 * lambda) + 50);

        while (u > cumulative && count < limit)
        {
            count++;
            probability *= lambda / count;
            cumulative += probability;
        }

        return count;
    }

    /// <summary>
    /// The standard normal quantile of <paramref name="u"/>, by Acklam's rational
    /// approximation (relative error below 1.15e-9 across the range).
    /// </summary>
    public static double StandardNormal(double u)
    {
        if (u is <= 0 or >= 1 || double.IsNaN(u))
        {
            throw new ArgumentOutOfRangeException(nameof(u), u, "The draw must be strictly between 0 and 1.");
        }

        const double Low = 0.02425;

        if (u < Low)
        {
            var q = Math.Sqrt(-2 * Math.Log(u));
            return Horner(AcklamC, q) / Horner(AcklamD, q);
        }

        if (u > 1 - Low)
        {
            var q = Math.Sqrt(-2 * Math.Log(1 - u));
            return -Horner(AcklamC, q) / Horner(AcklamD, q);
        }

        var centred = u - 0.5;
        var r = centred * centred;
        return Horner(AcklamA, r) * centred / Horner(AcklamB, r);
    }

    // Acklam's coefficients, highest power first. B and D have an implicit final 1.
    private static readonly double[] AcklamA =
        [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00];

    private static readonly double[] AcklamB =
        [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01, 1.0];

    private static readonly double[] AcklamC =
        [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00];

    private static readonly double[] AcklamD =
        [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00, 1.0];

    /// <summary>Evaluates a polynomial with coefficients highest power first.</summary>
    private static double Horner(double[] coefficients, double x)
    {
        var value = 0.0;
        foreach (var coefficient in coefficients)
        {
            value = (value * x) + coefficient;
        }

        return value;
    }

    /// <summary>A normal value with the given mean and standard deviation.</summary>
    public static double Normal(double u, double mean, double standardDeviation)
    {
        if (standardDeviation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(standardDeviation), standardDeviation, "A standard deviation is at least 0.");
        }

        return mean + (standardDeviation * StandardNormal(u));
    }

    /// <summary>A triangular value: bounded, with a most likely value. Good for lead times.</summary>
    public static double Triangular(double u, double min, double mode, double max)
    {
        if (!(min <= mode && mode <= max) || min == max)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "A triangular distribution needs min <= mode <= max and min < max.");
        }

        var split = (mode - min) / (max - min);
        return u < split
            ? min + Math.Sqrt(u * (max - min) * (mode - min))
            : max - Math.Sqrt((1 - u) * (max - min) * (max - mode));
    }
}

/// <summary>
/// A weighted choice over a fixed list, built once and sampled many times: basket slots
/// choosing among hundreds of variants, a payment method, a basket size.
/// </summary>
internal sealed class WeightedTable
{
    private readonly double[] _cumulative;

    /// <param name="weights">Non-negative, at least one positive. Need not sum to 1.</param>
    public WeightedTable(IReadOnlyList<double> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        if (weights.Count == 0)
        {
            throw new ArgumentException("A weighted choice needs at least one option.", nameof(weights));
        }

        _cumulative = new double[weights.Count];
        var total = 0.0;
        for (var i = 0; i < weights.Count; i++)
        {
            if (weights[i] < 0 || double.IsNaN(weights[i]) || double.IsInfinity(weights[i]))
            {
                throw new ArgumentException($"Weight {i} is {weights[i]}; weights are finite and at least 0.", nameof(weights));
            }

            total += weights[i];
            _cumulative[i] = total;
        }

        if (total <= 0)
        {
            throw new ArgumentException("The weights sum to zero, so nothing can be chosen.", nameof(weights));
        }
    }

    /// <summary>How many options there are.</summary>
    public int Count => _cumulative.Length;

    /// <summary>The chosen index. An option of weight zero is never chosen.</summary>
    public int Pick(double u)
    {
        var target = u * _cumulative[^1];
        var index = Array.BinarySearch(_cumulative, target);
        index = index >= 0 ? index + 1 : ~index;

        // Skip forward past zero-weight options that share a cumulative value, and never
        // run off the end when the target lands exactly on the total.
        while (index < _cumulative.Length - 1 && _cumulative[index] <= target)
        {
            index++;
        }

        return Math.Min(index, _cumulative.Length - 1);
    }
}
