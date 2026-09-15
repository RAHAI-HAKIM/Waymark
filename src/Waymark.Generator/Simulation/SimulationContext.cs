using Waymark.Domain.Ids;
using Waymark.Generator.Calendar;
using Waymark.Generator.Catalogues;
using Waymark.Generator.Configuration;
using Waymark.Generator.Randomness;
using Waymark.Generator.Writing;

namespace Waymark.Generator.Simulation;

/// <summary>
/// Everything the day loop and its collaborators share: where rows go, what the store is, how
/// it behaves, the clock, the ids and the randomness.
/// </summary>
internal sealed class SimulationContext(
    StoreDatabase database,
    GeneratedStore store,
    GeneratorConfig config,
    CalendarModel calendar,
    SimulatedClock clock,
    IIdGenerator ids,
    RandomSource random,
    StoreState state)
{
    public StoreDatabase Database { get; } = database;

    public GeneratedStore Store { get; } = store;

    public GeneratorConfig Config { get; } = config;

    public CalendarModel Calendar { get; } = calendar;

    public SimulatedClock Clock { get; } = clock;

    public IIdGenerator Ids { get; } = ids;

    public RandomSource Random { get; } = random;

    public StoreState State { get; } = state;

    /// <summary>
    /// Who is on a till: staff take the day's opening intervals in turn, in store.json order,
    /// rotating by one each day. The owner of a small épicerie tills too.
    /// </summary>
    public GeneratedStaff OnDuty(DateOnly date, int interval, int terminal) =>
        Store.Staff[(date.DayNumber + interval + terminal) % Store.Staff.Count];

    /// <summary>A store-local date and second of the day as a UTC instant.</summary>
    public DateTimeOffset At(DateOnly date, int secondOfDay) => Calendar.ToUtc(date, 0).AddSeconds(secondOfDay);

    /// <summary>One of <paramref name="codes"/>, uniformly by <paramref name="u"/>, with its definition.</summary>
    public ReasonCodeDefinition Reason(IReadOnlyList<string> codes, double u) =>
        Store.ReasonCodes[codes[Distributions.UniformInt(u, 0, codes.Count - 1)]];

    /// <summary>The staff member who authorises an action under <paramref name="reason"/>: the manager if it requires one.</summary>
    public string? Authoriser(ReasonCodeDefinition reason) => reason.RequiresManager ? Store.Manager.StaffId : null;
}
