using Waymark.Domain.Ids;

namespace Waymark.Application.IdGenerator;

/// <summary>
/// The reproducible one. Same seed, same clock readings, same ids — every run.
///
/// <para>
/// This is what CLAUDE.md §8's "the synthetic store generator is deterministic
/// from a seed, and tests depend on that" actually requires. A ULID is 48 bits
/// of timestamp and 80 bits of randomness, so reading the timestamp from an
/// injected clock and seeding the randomness reproduces the whole id while
/// keeping the two properties that matter: uniqueness within a run, and correct
/// sort order.
/// </para>
/// <para>
/// <b>The timestamp is the clock's, not a counter's.</b> The synthetic generator
/// passes its simulated clock, so a sale simulated for 14:02 on 5 March 2025
/// gets an id stamped 14:02 on 5 March 2025, and ULID order is business
/// chronology in the fake data. An earlier version advanced its own counter one
/// millisecond per id from a fixed start, which packed a simulated year into the
/// first few minutes of it (review finding F-3, D-046).
/// </para>
/// <para>
/// <b>Monotonic within a millisecond</b>, as the ULID specification describes:
/// ids minted while the clock reads the same millisecond — a transaction, its
/// items, its movements — keep that timestamp and increment the random part by
/// one, so they still sort in the order they were minted. A clock that goes
/// backwards is treated as standing still, for the same reason. Real
/// <see cref="UlidGenerator"/> ids make no such promise.
/// </para>
/// <para>
/// <b>Not thread-safe</b>, deliberately. The last timestamp and the
/// <see cref="Random"/> are mutable state, and two threads drawing from them
/// would interleave differently on each run — which is exactly the determinism
/// this class exists to provide. Give each thread its own instance, or call it
/// from one.
/// </para>
/// <para>
/// Determinism holds for a given .NET version: the seeded <see cref="Random"/>
/// uses the legacy algorithm precisely so it stays stable. A framework upgrade
/// is still worth re-running the generator's golden files against.
/// </para>
/// </summary>
/// <param name="seed">Anything. The same value reproduces the same sequence.</param>
/// <param name="clock">
/// Where the timestamp comes from. The synthetic generator passes its simulated
/// clock; a test passes one it controls.
/// </param>
public sealed class SeededIdGenerator(int seed, TimeProvider clock) : IIdGenerator
{
    private const int RandomnessBytes = 10;

    private readonly Random _random = new(seed);
    private readonly byte[] _randomness = new byte[RandomnessBytes];
    private long _lastMillisecond = long.MinValue;

    public string NewId()
    {
        var millisecond = clock.GetUtcNow().ToUnixTimeMilliseconds();

        if (millisecond > _lastMillisecond)
        {
            _lastMillisecond = millisecond;
            _random.NextBytes(_randomness);
        }
        else if (!Increment(_randomness))
        {
            // 2^80 ids in one millisecond cannot happen; if the arithmetic ever
            // says otherwise, move to the next millisecond rather than repeat.
            _lastMillisecond++;
            _random.NextBytes(_randomness);
        }

        return Ulid.NewUlid(DateTimeOffset.FromUnixTimeMilliseconds(_lastMillisecond), _randomness).ToString();
    }

    /// <summary>Adds one to a big-endian number. False when it wrapped to zero.</summary>
    private static bool Increment(byte[] value)
    {
        for (var i = value.Length - 1; i >= 0; i--)
        {
            if (++value[i] != 0)
            {
                return true;
            }
        }

        return false;
    }
}
