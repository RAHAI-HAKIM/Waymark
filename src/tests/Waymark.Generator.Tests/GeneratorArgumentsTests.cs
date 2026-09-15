namespace Waymark.Generator.Tests;

/// <summary>
/// The command line. Small, but a run that silently ignored a mistyped option
/// would generate a store under a seed nobody asked for.
/// </summary>
public sealed class GeneratorArgumentsTests
{
    private static GeneratorArguments? Parse(out string? error, params string[] args) =>
        GeneratorArguments.Parse(args, out error);

    [Fact]
    public void Config_and_out_are_enough()
    {
        var parsed = Parse(out var error, "--config", "grocery-dz.json", "--out", "run");

        Assert.Null(error);
        Assert.Equal(new GeneratorArguments("grocery-dz.json", "run", null, null), parsed);
    }

    [Fact]
    public void Seed_and_days_override_the_configuration()
    {
        var parsed = Parse(out var error, "--config", "c.json", "--out", "o", "--seed", "-7", "--days", "60");

        Assert.Null(error);
        Assert.Equal(-7, parsed!.Seed);
        Assert.Equal(60, parsed.Days);
    }

    [Theory]
    [InlineData("always_on", Configuration.ConnectivityProfile.AlwaysOn)]
    [InlineData("flaky", Configuration.ConnectivityProfile.Flaky)]
    [InlineData("offline_stretch", Configuration.ConnectivityProfile.OfflineStretch)]
    internal void Connectivity_overrides_the_configurations_profile(string value, Configuration.ConnectivityProfile expected)
    {
        var parsed = Parse(out var error, "--config", "c.json", "--out", "o", "--connectivity", value);

        Assert.Null(error);
        Assert.Equal(expected, parsed!.Connectivity);
    }

    [Fact]
    public void An_unknown_connectivity_profile_is_refused()
    {
        Assert.Null(Parse(out var error, "--config", "c.json", "--out", "o", "--connectivity", "offline"));
        Assert.Contains("always_on, flaky or offline_stretch", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--config", "c.json")]
    [InlineData("--out", "o")]
    public void Both_required_options_must_be_present(params string[] args)
    {
        Assert.Null(GeneratorArguments.Parse(args, out var error));
        Assert.Contains("required", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_option_is_refused_rather_than_ignored()
    {
        Assert.Null(Parse(out var error, "--config", "c.json", "--out", "o", "--sede", "42"));
        Assert.Contains("--sede", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--seed", "forty-two")]
    [InlineData("--days", "0")]
    [InlineData("--days", "-5")]
    [InlineData("--days", "1.5")]
    public void A_malformed_number_is_refused(string option, string value)
    {
        Assert.Null(Parse(out var error, "--config", "c.json", "--out", "o", option, value));
        Assert.Contains(option, error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_option_with_no_value_is_refused()
    {
        Assert.Null(Parse(out var error, "--config", "c.json", "--out"));
        Assert.Contains("needs a value", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_returns_nothing_and_no_error()
    {
        Assert.Null(Parse(out var error, "--help"));
        Assert.Null(error);
    }
}
