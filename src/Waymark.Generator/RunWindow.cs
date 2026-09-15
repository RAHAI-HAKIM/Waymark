using Waymark.Generator.Configuration;

namespace Waymark.Generator;

/// <summary>The dates a run covers, all store-local.</summary>
/// <param name="CommissioningDate">When the store went live on Waymark: reference data and first customers.</param>
/// <param name="FirstDay">The first simulated trading day.</param>
/// <param name="Days">How many trading days are simulated.</param>
internal sealed record RunWindow(DateOnly CommissioningDate, DateOnly FirstDay, int Days)
{
    /// <summary>The last simulated trading day, inclusive.</summary>
    public DateOnly LastDay => FirstDay.AddDays(Days - 1);

    /// <summary>The window a configuration asks for, with the command line's <c>--days</c> taking precedence.</summary>
    public static RunWindow From(RunSettings run, int? daysOverride)
    {
        ArgumentNullException.ThrowIfNull(run);

        var days = daysOverride ?? (run.StartDate.AddMonths(run.HistoryMonths).DayNumber - run.StartDate.DayNumber);
        return new RunWindow(run.StartDate.AddDays(-run.CommissioningDaysBeforeStart), run.StartDate, days);
    }
}
