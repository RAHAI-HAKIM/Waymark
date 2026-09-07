using System.Reflection;

namespace Waymark.Persistence;

/// <summary>
/// The v1 operational schema, embedded in the assembly rather than read from
/// disk.
///
/// <para>
/// This is a mitigation, not a convenience. Under decisions.md D-016 the
/// hand-written schema creates the database and EF migrations evolve it, which
/// means a store install depends on the script being present and being the
/// same version as the code that stamps the migration baseline. A file beside
/// the executable can go missing during an upgrade, or be left behind at an
/// older version, and either produces a database that is subtly wrong rather
/// than absent. Embedding removes both possibilities.
/// </para>
/// </summary>
public static class SchemaScript
{
    /// <summary>Logical name of the embedded resource.</summary>
    public const string ResourceName = "Waymark.Persistence.schema_v7_1.sql";

    /// <summary>
    /// The full schema DDL. Throws if the resource is missing, which can only
    /// happen if the build stopped embedding it.
    /// </summary>
    public static string Read()
    {
        var assembly = typeof(SchemaScript).GetTypeInfo().Assembly;

        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The schema resource '{ResourceName}' is not embedded in "
                + $"{assembly.GetName().Name}. Check the EmbeddedResource item "
                + "in Waymark.Persistence.csproj.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
