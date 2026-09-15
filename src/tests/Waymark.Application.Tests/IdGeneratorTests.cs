using Waymark.Application.IdGenerator;
using Waymark.Domain.Ids;

namespace Waymark.Application.Tests;

/// <summary>
/// Ids come from a port so that one implementation can be reproducible
/// (decisions.md D-038). These tests are what says the reproducible one
/// actually is.
/// </summary>
public sealed class IdGeneratorTests
{
    private static readonly DateTimeOffset Start = new(2024, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static List<string> Take(IIdGenerator generator, int count) =>
        [.. Enumerable.Range(0, count).Select(_ => generator.NewId())];

    // ------------------------------------------------------------ the real one

    [Fact]
    public void A_real_id_is_a_26_character_ULID()
    {
        var id = new UlidGenerator().NewId();

        Assert.Equal(26, id.Length);
        Assert.True(Ulid.TryParse(id, out _), $"'{id}' is not a ULID.");
    }

    [Fact]
    public void Real_ids_do_not_collide()
    {
        // 80 bits of randomness per millisecond. This would not catch a weak
        // generator on its own, but it does catch one that has stopped
        // generating — a constant, or a counter that was reset.
        var generator = new UlidGenerator();

        var ids = Take(generator, 100_000);

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Real_ids_sort_by_creation_time_to_the_millisecond()
    {
        // The reason for ULID over UUIDv4: inserts land at the end of an index
        // rather than scattered through it (CLAUDE.md §3.2). The resolution of
        // that claim is a millisecond — see the test below.
        var generator = new UlidGenerator();

        var stamps = Take(generator, 5_000).Select(id => Ulid.Parse(id).Time).ToList();

        for (var i = 1; i < stamps.Count; i++)
        {
            Assert.True(stamps[i] >= stamps[i - 1], "a ULID timestamp went backwards");
        }
    }

    [Fact]
    public void Real_ids_minted_in_the_same_millisecond_are_not_ordered_among_themselves()
    {
        // Documented rather than fixed. Cysharp's Ulid.NewUlid() draws fresh
        // randomness every call and is not monotonic within a tick, so a handler
        // that mints a transaction, its items, its movements and its outbox row
        // in one go produces ids that share a timestamp and sort arbitrarily
        // inside it.
        //
        // That is fine here — index locality comes from the timestamp prefix,
        // and where order matters the schema says so explicitly
        // (transaction_payments.sequence, outbox.sequence_number). This test
        // exists so nobody quietly starts relying on the stronger property.
        var generator = new UlidGenerator();

        var ids = Take(generator, 2_000);
        var sameMillisecond = ids
            .GroupBy(id => Ulid.Parse(id).Time)
            .First(group => group.Count() > 1)
            .ToList();

        Assert.NotEqual(sameMillisecond.Order(StringComparer.Ordinal), sameMillisecond);
    }

    // ------------------------------------------------------ the seeded one

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void The_same_seed_and_clock_reproduce_the_same_ids()
    {
        // The claim the synthetic store generator rests on. Without it there is
        // no golden-file test over generated data and no reproducing a reported
        // bug from its seed.
        static List<string> Run()
        {
            var clock = new ManualClock(Start);
            var generator = new SeededIdGenerator(seed: 42, clock);
            var ids = new List<string>();
            for (var i = 0; i < 100; i++)
            {
                ids.Add(generator.NewId());
                if (i % 7 == 0)
                {
                    clock.Now = clock.Now.AddSeconds(3);
                }
            }

            return ids;
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void A_different_seed_produces_different_ids()
    {
        var first = Take(new SeededIdGenerator(seed: 42, new ManualClock(Start)), 100);
        var second = Take(new SeededIdGenerator(seed: 43, new ManualClock(Start)), 100);

        Assert.Empty(first.Intersect(second, StringComparer.Ordinal));
    }

    [Fact]
    public void Seeded_ids_carry_the_clocks_time_not_a_counters()
    {
        // F-3: a sale simulated for 14:02 on 5 March 2025 must get an id stamped
        // then, or a generated year packs its ids into its first minutes and ULID
        // order stops meaning business chronology.
        var clock = new ManualClock(new DateTimeOffset(2025, 3, 5, 14, 2, 3, 123, TimeSpan.Zero));
        var generator = new SeededIdGenerator(seed: 7, clock);

        Assert.Equal(clock.Now, Ulid.Parse(generator.NewId()).Time);

        clock.Now = new DateTimeOffset(2025, 11, 20, 8, 30, 0, TimeSpan.Zero);
        Assert.Equal(clock.Now, Ulid.Parse(generator.NewId()).Time);
    }

    [Fact]
    public void Ids_minted_within_one_millisecond_keep_its_timestamp_and_stay_in_order()
    {
        // A transaction, its items and its movements are minted while the
        // simulated clock stands still. They share the timestamp and still sort
        // in the order they were minted — the monotonic rule of the ULID spec.
        var clock = new ManualClock(Start);
        var ids = Take(new SeededIdGenerator(seed: 7, clock), 1_000);

        Assert.All(ids, id => Assert.Equal(Start, Ulid.Parse(id).Time));
        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_clock_that_goes_backwards_does_not_break_the_order()
    {
        var clock = new ManualClock(Start.AddMinutes(5));
        var generator = new SeededIdGenerator(seed: 7, clock);

        var before = generator.NewId();
        clock.Now = Start;
        var after = generator.NewId();

        Assert.True(
            string.CompareOrdinal(before, after) < 0,
            "An id minted after a clock step backwards sorted before the previous one.");
    }

    [Fact]
    public void Seeded_ids_do_not_collide_within_a_run()
    {
        var clock = new ManualClock(Start);
        var generator = new SeededIdGenerator(seed: 1, clock);
        var ids = new List<string>();
        for (var i = 0; i < 50_000; i++)
        {
            ids.Add(generator.NewId());
            if (i % 100 == 0)
            {
                clock.Now = clock.Now.AddMilliseconds(1);
            }
        }

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Both_implementations_satisfy_the_same_port()
    {
        // Nothing downstream should be able to tell which one it was given —
        // that is what makes substituting the seeded one in tests honest.
        IIdGenerator[] generators = [new UlidGenerator(), new SeededIdGenerator(seed: 3, new ManualClock(Start))];

        foreach (var generator in generators)
        {
            var id = generator.NewId();

            Assert.Equal(26, id.Length);
            Assert.True(Ulid.TryParse(id, out _));
        }
    }
}
