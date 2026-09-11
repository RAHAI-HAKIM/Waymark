using Waymark.Domain.Ids;

namespace Waymark.Application.IdGenerator;

/// <summary>
/// The reproducible one. Same seed, same clock, same ids — every run.
///
/// <para>
/// This is what CLAUDE.md §8's "the synthetic store generator is deterministic
/// from a seed, and tests depend on that" actually requires. A ULID is 48 bits
/// of timestamp and 80 bits of randomness, so fixing the clock and seeding the
/// randomness reproduces the whole id while keeping the two properties that
/// matter: uniqueness within a run, and correct sort order.
/// </para>
/// <para>
/// It also gives something the real generator cannot: a store generated for
/// 2024 gets ids whose <i>embedded</i> timestamps are in 2024, so ULID sort
/// order matches business chronology in the fake data too.
/// </para>
/// <para>
/// <b>Not thread-safe</b>, deliberately. Both the clock and the
/// <see cref="Random"/> are mutable state, and two threads drawing from them
/// would interleave differently on each run — which is exactly the determinism
/// this class exists to provide. Give each thread its own instance with its own
/// seed, or call it from one.
/// </para>
/// <para>
/// Determinism holds for a given .NET version: the seeded <see cref="Random"/>
/// uses the legacy algorithm precisely so it stays stable. A framework upgrade
/// is still worth re-running the generator's golden files against.
/// </para>
/// </summary>
/// <param name="seed">Anything. The same value reproduces the same sequence.</param>
/// <param name="clockStart">
/// The instant the first id is dated from. Ids advance one millisecond each, so
/// a run of them sorts in the order they were minted.
/// </param>
public sealed class SeededIdGenerator(int seed, DateTimeOffset clockStart) : IIdGenerator
{
    private readonly Random _random = new(seed);
    private DateTimeOffset _now = clockStart;

    public string NewId()
    {
        Span<byte> randomness = stackalloc byte[10];
        _random.NextBytes(randomness);

        // Advance first, so two ids minted in the same tick of a real clock
        // still differ in their timestamp and keep a stable sort order.
        _now = _now.AddMilliseconds(1);

        return Ulid.NewUlid(_now, randomness).ToString();
    }
}
