using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Engine;
using Waymark.Domain.Enums;

namespace Waymark.Persistence.Engine;

/// <summary>
/// The engine parameters a store needs before the engine has ever run for it.
///
/// <para>
/// A store on its first day has no fitted parameter and no history to fit one from, and the
/// alternative to a stated cold-start default is a number hidden in the code (D-037: absence
/// is never zero). So the value goes in <c>parameter_registry</c> like any other parameter,
/// with <c>source = 'cold_start_default'</c> and a method that says in words that nobody
/// measured it. Every card it decides carries its version, so a card written against the
/// placeholder can always be told apart from one written against a fitted window.
/// </para>
/// <para>
/// Installed at start, like the schema and the triggers, and never repaired: once the engine
/// or a shopkeeper has written a current row, this leaves it alone. It is the installer's
/// job, until there is an installer.
/// </para>
/// </summary>
public static class ColdStartParameters
{
    /// <summary>
    /// Seven days for every category (D-069), a placeholder in the plainest sense: it was
    /// chosen to make the path real, not because seven is right for both yoghurt and flour.
    /// The per-category windows are an engine decision, in Phase 2.
    /// </summary>
    public const long NearExpiryWindowDays = 7;

    /// <summary>How it was arrived at. Read by a human looking at the card and asking why seven.</summary>
    public const string PlaceholderMethod = "placeholder:phase-0.5:one window for every category (D-069)";

    /// <summary>
    /// Writes the near-expiry window if the registry has no current one.
    /// </summary>
    /// <returns><c>true</c> when it installed the placeholder, <c>false</c> when a value was already there.</returns>
    public static bool EnsureNearExpiryWindow(WaymarkDbContext context, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(context);

        var exists = context.ParameterRegistry.Any(row =>
            row.ParameterCode == NearExpiry.ParameterCode && row.ScopeType == ScopeType.Global && row.IsCurrent);

        if (exists)
        {
            return false;
        }

        context.ParameterRegistry.Add(new ParameterRegistryEntry
        {
            ParameterCode = NearExpiry.ParameterCode,
            ScopeType = ScopeType.Global,
            ScopeId = "",
            Version = 1,
            ValueNumber = NearExpiryWindowDays,

            // No unit code: unit_code points at units_of_measure, which is the catalogue's
            // units — pieces and kilogrammes — and a day is not one of them. The Because block
            // states the unit where it matters, to a reader.
            UnitCode = null,
            Method = PlaceholderMethod,
            Source = ParameterRegistryEntrySource.ColdStartDefault,
            ObservationCount = 0,
            ComputedAt = now,
            IsCurrent = true,
        });

        context.SaveChanges();
        return true;
    }
}
