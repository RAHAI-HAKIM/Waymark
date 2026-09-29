using Waymark.Application.Commands;
using Waymark.Application.Organisation;
using Waymark.Domain.Catalogue;

namespace Waymark.StoreServer.Catalogue;

/// <summary>
/// <c>Waymark.StoreServer --scale-format=&lt;preset or mask&gt;</c> (session B3, D-090): sets how this
/// store's scale labels are read, and exits, serving nothing. The maintenance door until H2's store
/// screen, like <c>--set-pin</c>. <c>--scale-format=list</c> prints the presets and changes nothing.
/// </summary>
public static class ScaleFormatSwitch
{
    /// <summary>The configuration key <c>--scale-format=</c> binds to.</summary>
    public const string Setting = "scale-format";

    /// <returns>The process exit code: 0 when set or listed, 1 when refused.</returns>
    public static async Task<int> RunAsync(
        string format,
        TextWriter output,
        CommandExecutor executor,
        SetScaleLabelFormatHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(executor);

        if (string.Equals(format.Trim(), "list", StringComparison.OrdinalIgnoreCase))
        {
            await WritePresetsAsync(output);
            return 0;
        }

        ScaleLabelFormat set;
        try
        {
            set = await executor.ExecuteAsync(handler, new SetScaleLabelFormat(format), cancellationToken);
        }
        catch (ScaleFormatRefusedException refusal)
        {
            await output.WriteLineAsync($"{refusal.Message} Nothing was changed.");
            await WritePresetsAsync(output);
            return 1;
        }

        await output.WriteLineAsync(
            $"Scale labels are now read as {set.Name}: {set.Mask}, prefixes {string.Join(",", set.Prefixes)}, "
            + $"a price label in {set.PriceUnit.ToString().ToLowerInvariant()}.");
        return 0;
    }

    private static async Task WritePresetsAsync(TextWriter output)
    {
        await output.WriteLineAsync("Presets (P prefix, I item code, V value, X read past, C check digit):");
        foreach (var preset in ScaleLabelFormat.Presets)
        {
            await output.WriteLineAsync($"  {preset.Name,-18} {preset.Mask}  price in {preset.PriceUnit.ToString().ToLowerInvariant()}");
        }

        await output.WriteLineAsync("Or a custom mask: --scale-format=\"PPIIIIIVVVVVC;prefixes=27;price=dinars\"");
    }
}
