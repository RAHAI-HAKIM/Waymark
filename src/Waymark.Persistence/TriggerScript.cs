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
    public static IReadOnlyList<string> DeclaredNames() =>
        [.. DeclaredTriggers().Select(trigger => trigger.Name)];

    /// <summary>
    /// Each trigger, the table it guards, and the statements that install it.
    ///
    /// <para>
    /// The table matters because a database can legitimately be part-way
    /// through its migrations — the baseline fixture builds exactly that — and
    /// <c>CREATE TRIGGER … ON a_table_that_does_not_exist_yet</c> is an error
    /// rather than a no-op. Knowing which table each trigger belongs to is what
    /// lets <see cref="WaymarkDatabaseExtensions.ApplyTriggers"/> install the
    /// ones that apply and leave the rest for the migration that creates their
    /// table.
    /// </para>
    /// <para>
    /// The parse assumes what the script does: one <c>BEGIN … END;</c> per
    /// trigger, never nested. <c>TriggerScriptTests</c> asserts the inventory
    /// matches the file, so a shape the regex cannot read shows up as a missing
    /// trigger rather than as silence.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TriggerDefinition> DeclaredTriggers() =>
        [.. TriggerBlock().Matches(Read()).Select(match => new TriggerDefinition(
            match.Groups["name"].Value,
            match.Groups["table"].Value,
            match.Value))];

    // Optional DROP, then CREATE … ON <table> … through the terminating END;.
    [GeneratedRegex(
        @"(?:DROP\s+TRIGGER\s+IF\s+EXISTS\s+\w+\s*;\s*)?"
        + @"CREATE\s+TRIGGER\s+(?<name>\w+)\s+"
        + @"(?:BEFORE|AFTER|INSTEAD\s+OF)\s+\w+(?:\s+OF\s+[\w\s,]+?)?\s+"
        + @"ON\s+(?<table>\w+).*?END\s*;",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TriggerBlock();
}

/// <summary>One trigger from <c>triggers.sql</c>.</summary>
/// <param name="Name">The trigger's name, as <c>sqlite_schema</c> records it.</param>
/// <param name="Table">The table it guards.</param>
/// <param name="Sql">The drop-then-create pair that installs it, idempotently.</param>
public sealed record TriggerDefinition(string Name, string Table, string Sql);
