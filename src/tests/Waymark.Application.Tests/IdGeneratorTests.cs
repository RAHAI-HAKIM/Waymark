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

    [Fact]
    public void The_same_seed_reproduces_the_same_ids()
    {
        // The claim the synthetic store generator rests on. Without it there is
        // no golden-file test over generated data and no reproducing a reported
        // bug from its seed.
        var first = Take(new SeededIdGenerator(seed: 42, Start), 100);
        var second = Take(new SeededIdGenerator(seed: 42, Start), 100);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_different_seed_produces_different_ids()
    {
        var first = Take(new SeededIdGenerator(seed: 42, Start), 100);
        var second = Take(new SeededIdGenerator(seed: 43, Start), 100);

        Assert.Empty(first.Intersect(second, StringComparer.Ordinal));
    }

    [Fact]
    public void A_different_start_produces_different_ids()
    {
        // Same randomness, different timestamps. If the clock were ignored,
        // these would be identical and the 2024-store property below would be
        // a coincidence rather than a guarantee.
        var first = Take(new SeededIdGenerator(seed: 42, Start), 20);
        var second = Take(new SeededIdGenerator(seed: 42, Start.AddYears(1)), 20);

        Assert.Empty(first.Intersect(second, StringComparer.Ordinal));
    }

    [Fact]
    public void Seeded_ids_carry_the_seeded_clock_not_the_real_one()
    {
        // A store generated for 2024 gets ids whose embedded timestamps are in
        // 2024, so ULID sort order matches business chronology in the fake data.
        var generator = new SeededIdGenerator(seed: 7, Start);

        foreach (var id in Take(generator, 200))
        {
            var stamp = Ulid.Parse(id).Time;

            Assert.Equal(2024, stamp.Year);
            Assert.InRange(stamp, Start, Start.AddMinutes(1));
        }
    }

    [Fact]
    public void Seeded_ids_advance_one_millisecond_at_a_time_and_stay_sorted()
    {
        var generator = new SeededIdGenerator(seed: 7, Start);

        var ids = Take(generator, 50);
        var stamps = ids.Select(id => Ulid.Parse(id).Time).ToList();

        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
        Assert.Equal(Start.AddMilliseconds(1), stamps[0]);
        Assert.Equal(Start.AddMilliseconds(50), stamps[^1]);
    }

    [Fact]
    public void Seeded_ids_do_not_collide_within_a_run()
    {
        var ids = Take(new SeededIdGenerator(seed: 1, Start), 50_000);

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Both_implementations_satisfy_the_same_port()
    {
        // Nothing downstream should be able to tell which one it was given —
        // that is what makes substituting the seeded one in tests honest.
        IIdGenerator[] generators = [new UlidGenerator(), new SeededIdGenerator(seed: 3, Start)];

        foreach (var generator in generators)
        {
            var id = generator.NewId();

            Assert.Equal(26, id.Length);
            Assert.True(Ulid.TryParse(id, out _));
        }
    }
}
