using Waymark.Generator.Calendar;
using Waymark.Generator.Configuration;
using Waymark.Generator.Randomness;

namespace Waymark.Generator.Simulation;

/// <summary>
/// Latent demand: how many units of a variant customers want on a day, whether or not the
/// shelf can supply them (D-046 §14).
///
/// <para>
/// <c>latent = Poisson(base_rate × weekday × month × Ramadan phase × events × pay cycle)</c>,
/// drawn from <c>demand(variant, day)</c>. <b>Nothing about stock, footfall or the ordering
/// rule enters it</b>, so the demand a policy is judged against cannot move when the policy
/// changes. What stock refuses is recorded only in <c>latent-demand.csv</c>, never in the
/// database (D-046 §34).
/// </para>
/// <para>
/// A day the store is closed has no demand: its customers shop elsewhere, and a closure is
/// not a stockout. Promotions have no factor yet because the catalogue has none.
/// </para>
/// </summary>
internal sealed class DemandModel(IReadOnlyDictionary<string, Sourced<SeasonalityProfile>> profiles, RandomSource random)
{
    private readonly RandomStream _demand = random.Stream("demand");

    /// <summary>The expected units of <paramref name="variant"/> wanted on <paramref name="day"/>.</summary>
    public double Mean(GeneratedVariant variant, DayContext day)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(day);

        if (!day.IsOpen)
        {
            return 0;
        }

        var profile = profiles[variant.Catalogue.SeasonalityProfile].Value;
        var mean = variant.Catalogue.BaseDailyRate
            * day.WeekdayFactor
            * profile.Months[day.Date.Month - 1]
            * day.Payday.Demand;

        if (CalendarModel.RamadanKey(day.RamadanPhase) is { } phase)
        {
            mean *= profile.Ramadan[phase];
        }

        foreach (var name in day.Events)
        {
            if (profile.Events.TryGetValue(name, out var factor))
            {
                mean *= factor;
            }
        }

        return mean;
    }

    /// <summary>The units of <paramref name="variant"/> wanted on <paramref name="day"/>: one Poisson draw at its own address.</summary>
    public int Latent(GeneratedVariant variant, DayContext day) =>
        Distributions.Poisson(_demand.Uniform(variant.Index, day.Date.DayNumber), Mean(variant, day));
}
