using System.Text;

namespace Waymark.Generator.Randomness;

/// <summary>
/// Addressed randomness: every random value is a pure function of the master seed, a named
/// stream and its coordinates (D-046 §10).
///
/// <para>
/// <b>No shared sequential generator anywhere.</b> With one <see cref="Random"/> drawn in
/// sequence, adding a single draw early in a run — a new product, one extra customer —
/// shifts every value after it, and two runs that should differ in one respect differ in
/// all of them. Here <c>demand(variant 17, day 42)</c> is the same number whatever else the
/// run did, so a change to the ordering rule cannot move the demand it is judged against,
/// and the connectivity profile cannot move a single sale (D-046 §31).
/// </para>
/// <para>
/// <b>Not cryptography, deliberately.</b> FNV-1a names the stream and SplitMix64 mixes the
/// coordinates: fast, well distributed, and plain arithmetic. An architecture test forbids
/// <c>System.Security.Cryptography</c> in the generator, so it never becomes a second place
/// able to compute keyed hashes beside the one that holds the tenant key.
/// </para>
/// <para>
/// Deterministic across runs and machines: integer arithmetic only, and the uniform draw is
/// an exact function of 53 bits. Distributions built on it use <see cref="Math"/> functions,
/// whose last bits can in principle differ between operating systems, so golden files are
/// compared on the platform CI runs on.
/// </para>
/// </summary>
/// <param name="masterSeed">The run's seed.</param>
internal sealed class RandomSource(long masterSeed)
{
    /// <summary>The seed every stream derives from.</summary>
    public long MasterSeed { get; } = masterSeed;

    /// <summary>The stream with this name. Names are fixed strings; the same name is the same stream.</summary>
    public RandomStream Stream(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new RandomStream(name, Mixing.SplitMix64(unchecked((ulong)MasterSeed) ^ Mixing.Fnv1a(name)));
    }

    /// <summary>
    /// A 31-bit seed for a component that needs a plain integer, such as
    /// <c>SeededIdGenerator</c>. Derived from a named stream, so it moves with the master seed.
    /// </summary>
    public int DeriveSeed(string name) => (int)(Stream(name).Bits(0) & int.MaxValue);
}

/// <summary>
/// One named stream. A value type holding only its key, so taking one is free.
/// </summary>
internal readonly struct RandomStream
{
    private readonly ulong _key;

    internal RandomStream(string name, ulong key)
    {
        Name = name;
        _key = key;
    }

    /// <summary>The stream's name, for diagnostics.</summary>
    public string Name { get; }

    /// <summary>64 mixed bits for one coordinate.</summary>
    public ulong Bits(long a) => Mixing.SplitMix64(_key ^ unchecked((ulong)a));

    /// <summary>64 mixed bits for two coordinates. Order matters: (a, b) and (b, a) differ.</summary>
    public ulong Bits(long a, long b) => Mixing.SplitMix64(Bits(a) ^ unchecked((ulong)b));

    /// <summary>64 mixed bits for three coordinates.</summary>
    public ulong Bits(long a, long b, long c) => Mixing.SplitMix64(Bits(a, b) ^ unchecked((ulong)c));

    /// <summary>64 mixed bits for four coordinates.</summary>
    public ulong Bits(long a, long b, long c, long d) => Mixing.SplitMix64(Bits(a, b, c) ^ unchecked((ulong)d));

    /// <summary>A uniform value strictly between 0 and 1.</summary>
    public double Uniform(long a) => Mixing.ToOpenUnitInterval(Bits(a));

    /// <inheritdoc cref="Uniform(long)"/>
    public double Uniform(long a, long b) => Mixing.ToOpenUnitInterval(Bits(a, b));

    /// <inheritdoc cref="Uniform(long)"/>
    public double Uniform(long a, long b, long c) => Mixing.ToOpenUnitInterval(Bits(a, b, c));

    /// <inheritdoc cref="Uniform(long)"/>
    public double Uniform(long a, long b, long c, long d) => Mixing.ToOpenUnitInterval(Bits(a, b, c, d));
}

/// <summary>The two mixing functions and the map to the unit interval.</summary>
internal static class Mixing
{
    private const ulong FnvOffsetBasis = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;
    private const double TwoToThe53 = 9007199254740992.0;

    /// <summary>FNV-1a over the UTF-8 bytes: a stable name hash, unlike <c>string.GetHashCode</c>, which is randomised per process.</summary>
    public static ulong Fnv1a(string text) => Fnv1a(Encoding.UTF8.GetBytes(text));

    /// <inheritdoc cref="Fnv1a(string)"/>
    public static ulong Fnv1a(ReadOnlySpan<byte> bytes)
    {
        var hash = FnvOffsetBasis;
        foreach (var value in bytes)
        {
            hash = unchecked((hash ^ value) * FnvPrime);
        }

        return hash;
    }

    /// <summary>The SplitMix64 finaliser (Steele, Lea and Flood, 2014).</summary>
    public static ulong SplitMix64(ulong value)
    {
        unchecked
        {
            var z = value + 0x9E3779B97F4A7C15;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }
    }

    /// <summary>
    /// The top 53 bits as a value in (0, 1), never exactly 0 or 1 — so an inverse CDF never
    /// receives an infinity.
    /// </summary>
    public static double ToOpenUnitInterval(ulong bits) => ((bits >> 11) + 0.5) / TwoToThe53;
}
