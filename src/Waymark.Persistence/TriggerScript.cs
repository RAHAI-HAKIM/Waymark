using System.Reflection;
using System.Text.RegularExpressions;

namespace Waymark.Persistence;

/// <summary>
/// The append-only triggers, embedded in the assembly.
///
/// <para>
/// They are not in the EF Core model and cannot be: EF does not know triggers
/// exist, and its table rebuild issues <c>DROP TABLE</c>, which takes the
/// table's triggers with it. Reproduced during the D-016 work — a table went
/// through one rebuild migration and came out with its trigger gone while EF
/// reported success. So they live in <c>triggers.sql</c> and are re-applied
/// after every <c>Migrate()</c>.
/// </para>
/// </summary>
public static partial class TriggerScript
{
    /// <summary>Logical name of the embedded resource.</summary>
    public const string ResourceName = "Waymark.Persistence.triggers.sql";

    /// <summary>
    /// The script. Every statement is idempotent — <c>DROP TRIGGER IF
    /// EXISTS</c> then <c>CREATE TRIGGER</c> — so applying it twice is a no-op
    /// and applying it after a rebuild restores what the rebuild dropped.
    /// </summary>
    public static string Read()
    {
        var assembly = typeof(TriggerScript).GetTypeInfo().Assembly;

        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The trigger resource '{ResourceName}' is not embedded in "
                + $"{assembly.GetName().Name}. Check the EmbeddedResource item "
                + "in Waymark.Persistence.csproj.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Every trigger the script defines, in the order it defines them. This is
    /// the expected inventory: what a correctly initialised database must
    /// contain, and what <c>FindMissingTriggers</c> checks against.
    /// </summary>
    public static IReadOnlyList<string> DeclaredNames()
    {
        return CreateTrigger().Matches(Read())
            .Select(match => match.Groups["name"].Value)
            .ToList();
    }

    [GeneratedRegex(@"CREATE\s+TRIGGER\s+(?<name>\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex CreateTrigger();
}
