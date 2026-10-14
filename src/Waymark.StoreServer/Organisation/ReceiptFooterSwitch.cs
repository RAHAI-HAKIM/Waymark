using Waymark.Application.Commands;
using Waymark.Application.Organisation;

namespace Waymark.StoreServer.Organisation;

/// <summary>
/// <c>Waymark.StoreServer --receipt-footer="Merci de votre visite !"</c> (D-125): the store's own text
/// at the foot of its receipts, and exits, serving nothing; <c>--receipt-footer=</c> with nothing
/// clears it. A <c>|</c> breaks the line. The maintenance door until H2's store screen.
/// </summary>
public static class ReceiptFooterSwitch
{
    public const string Setting = "receipt-footer";

    /// <returns>The process exit code: 0 when set, 1 when refused and nothing was changed.</returns>
    public static async Task<int> RunAsync(
        string value, TextWriter output, CommandExecutor executor, SetReceiptFooterHandler handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(executor);

        try
        {
            var set = await executor.ExecuteAsync(handler, new SetReceiptFooter(value?.Replace('|', '\n')), cancellationToken);
            await output.WriteLineAsync(set is null
                ? "This store's receipts carry no text of its own, from the next one printed."
                : $"This store's receipts end with its text, from the next one printed: {set.Replace('\n', '|')}");
            return 0;
        }
        catch (ReceiptFooterRefusedException refusal)
        {
            await output.WriteLineAsync($"{refusal.Message} Nothing was changed.");
            return 1;
        }
    }
}
