using System.Globalization;

namespace Waymark.Generator;

/// <summary>
/// What a run was asked to do, parsed from the command line.
/// </summary>
/// <param name="ConfigPath">The generator configuration file.</param>
/// <param name="OutputDirectory">Where the database, the latent-demand CSV, the manifest and the report go.</param>
/// <param name="Seed">
/// Overrides the configuration's master seed, so the same config can be run under
/// several seeds without editing it. Null means use the config's.
/// </param>
/// <param name="Days">
/// Overrides the configured history length, for a short run. Null means use the config's.
/// </param>
internal sealed record GeneratorArguments(string ConfigPath, string OutputDirectory, int? Seed, int? Days)
{
    /// <summary>Overrides the configuration's connectivity profile. Null means use the config's.</summary>
    public Configuration.ConnectivityProfile? Connectivity { get; init; }

    public const string Usage = """
        Waymark synthetic store generator (decisions.md D-046)

        Usage:
            Waymark.Generator --config <file> --out <directory> [--seed <n>] [--days <n>]
                              [--connectivity always_on|flaky|offline_stretch]

        Options:
            --config   Generator configuration (JSON). Required.
            --out      Output directory. Created if missing; must not already hold a
                        waymark-store.db, so a previous run is never overwritten. Required.
            --seed     Master seed, overriding the configuration's.
            --days     History length in days, overriding the configuration's.
            --connectivity
                       Connectivity profile, overriding the configuration's. Changes the
                        outbox and sync_state only, never a sale.
            --help     Show this text.
        """;

    /// <summary>
    /// Parses the arguments, or explains exactly what is wrong with them.
    /// </summary>
    /// <returns>
    /// The parsed arguments, or null with <paramref name="error"/> set. Null with no
    /// error means help was asked for.
    /// </returns>
    public static GeneratorArguments? Parse(IReadOnlyList<string> args, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        error = null;
        string? config = null;
        string? output = null;
        int? seed = null;
        int? days = null;
        Configuration.ConnectivityProfile? connectivity = null;

        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];

            if (name is "--help" or "-h" or "/?")
            {
                return null;
            }

            if (i + 1 >= args.Count)
            {
                error = $"'{name}' needs a value.";
                return null;
            }

            var value = args[++i];

            switch (name)
            {
                case "--config":
                    config = value;
                    break;

                case "--out":
                    output = value;
                    break;

                case "--seed":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSeed))
                    {
                        error = $"--seed must be a whole number; got '{value}'.";
                        return null;
                    }

                    seed = parsedSeed;
                    break;

                case "--days":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedDays) || parsedDays < 1)
                    {
                        error = $"--days must be a positive whole number; got '{value}'.";
                        return null;
                    }

                    days = parsedDays;
                    break;

                case "--connectivity":
                    connectivity = value switch
                    {
                        "always_on" => Configuration.ConnectivityProfile.AlwaysOn,
                        "flaky" => Configuration.ConnectivityProfile.Flaky,
                        "offline_stretch" => Configuration.ConnectivityProfile.OfflineStretch,
                        _ => null,
                    };

                    if (connectivity is null)
                    {
                        error = $"--connectivity must be always_on, flaky or offline_stretch; got '{value}'.";
                        return null;
                    }

                    break;

                default:
                    error = $"Unknown option '{name}'.";
                    return null;
            }
        }

        if (config is null || output is null)
        {
            error = "Both --config and --out are required.";
            return null;
        }

        return new GeneratorArguments(config, output, seed, days) { Connectivity = connectivity };
    }
}
