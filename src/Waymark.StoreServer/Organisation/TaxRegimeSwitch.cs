using Waymark.Application.Commands;
using Waymark.Application.Organisation;
using Waymark.Domain.Enums;

namespace Waymark.StoreServer.Organisation;

/// <summary>
/// <c>Waymark.StoreServer --tax-regime=subject|not-subject</c> (D-122): says whether this store
/// charges TVA, and exits, serving nothing. The maintenance door until H2's store screen, like
/// <c>--scale-format</c>. A store is not subject until this says otherwise.
/// </summary>
public static class TaxRegimeSwitch
{
    public const string Setting = "tax-regime";

    /// <returns>The process exit code: 0 when set, 1 when refused and nothing was changed.</returns>
    public static async Task<int> RunAsync(
        string value, TextWriter output, CommandExecutor executor, SetTaxRegimeHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(executor);

        TaxRegime? regime = value?.Trim().ToLowerInvariant() switch
        {
            "subject" => TaxRegime.Subject,
            "not-subject" or "not_subject" => TaxRegime.NotSubject,
            _ => null,
        };
        if (regime is null)
        {
            await output.WriteLineAsync($"--{Setting}: subject or not-subject. Nothing was changed.");
            return 1;
        }

        try
        {
            var set = await executor.ExecuteAsync(handler, new SetTaxRegime(regime.Value), cancellationToken);
            await output.WriteLineAsync(set == TaxRegime.Subject
                ? "This store is subject to TVA: it is extracted from each price by its category's rate, from the next sale."
                : "This store is not subject to TVA: no tax is recorded or shown, from the next sale.");
            return 0;
        }
        catch (TaxRegimeRefusedException refusal)
        {
            await output.WriteLineAsync($"{refusal.Message} Nothing was changed.");
            return 1;
        }
    }
}
