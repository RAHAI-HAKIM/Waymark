using Waymark.Application.Commands;
using Waymark.Application.Organisation;

namespace Waymark.StoreServer.Security;

/// <summary>
/// <c>Waymark.StoreServer --set-pin=&lt;staffId&gt;</c> (session A5, D-083): sets one person's PIN on
/// this till's store and exits, serving nothing. The maintenance door until H1's staff screens.
///
/// <para>
/// It runs after the startup checks, so it only ever writes to an encrypted, migrated store, and
/// it asks for the PIN twice, unseen, on the console. <b>A PIN is never a command-line
/// argument</b>: arguments land in the shell's history and in the process list.
/// </para>
/// </summary>
public static class SetPinSwitch
{
    /// <summary>The configuration key <c>--set-pin=</c> binds to.</summary>
    public const string Setting = "set-pin";

    /// <summary>Asks for the PIN twice and sets it.</summary>
    /// <param name="readPin">Reads one PIN, unseen; null when there is no more input.</param>
    /// <returns>The process exit code: 0 when set, 1 when not.</returns>
    public static async Task<int> RunAsync(
        string staffId,
        Func<string, string?> readPin,
        TextWriter output,
        CommandExecutor executor,
        SetStaffPinHandler handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readPin);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(executor);

        var first = readPin($"New PIN for {staffId}: ");
        var second = readPin("The same PIN again: ");

        if (first is null || second is null || !string.Equals(first, second, StringComparison.Ordinal))
        {
            await output.WriteLineAsync("The two PINs differ. Nothing was changed.");
            return 1;
        }

        try
        {
            await executor.ExecuteAsync(handler, new SetStaffPin(staffId, first), cancellationToken);
        }
        catch (PinRefusedException refusal)
        {
            await output.WriteLineAsync($"{refusal.Message} Nothing was changed.");
            return 1;
        }

        await output.WriteLineAsync($"PIN set for {staffId}. It works at the next sign-in; nobody signed in is affected.");
        return 0;
    }

    /// <summary>Reads a line from the console without echoing it.</summary>
    public static string? ReadHidden(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            var line = Console.ReadLine();
            Console.WriteLine();
            return line;
        }

        var typed = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return typed.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (typed.Length > 0)
                {
                    typed.Length--;
                }
            }
            else if (key.KeyChar != '\0')
            {
                typed.Append(key.KeyChar);
            }
        }
    }
}
