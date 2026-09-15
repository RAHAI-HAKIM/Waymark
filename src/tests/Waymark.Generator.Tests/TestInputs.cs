using Microsoft.Data.Sqlite;
using Waymark.Generator.Catalogues;
using Waymark.Generator.Configuration;

namespace Waymark.Generator.Tests;

/// <summary>Where the test inputs are, and scratch space that cleans up after itself.</summary>
internal static class TestInputs
{
    /// <summary>The shipped grocery configuration, copied beside the tests by the generator project.</summary>
    public static string GroceryConfig => Path.Combine(AppContext.BaseDirectory, "inputs", "configs", "grocery-dz.json");

    /// <summary>The mini hardware-shop fixture: a second catalogue with different categories.</summary>
    public static string MiniDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "mini");

    public static string MiniConfig => Path.Combine(MiniDirectory, "configs", "mini.json");

    /// <summary>Loads a configuration and its catalogue the way a run does.</summary>
    public static (GeneratorConfig Config, Catalogue Catalogue, RunWindow Window) Load(string configPath, int? days = null)
    {
        var config = GeneratorJson.Read<GeneratorConfig>(configPath);
        var directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configPath)!, config.Catalogue));
        var catalogue = new CsvCatalogueSource(directory).Load();
        return (config, catalogue, RunWindow.From(config.Run, days));
    }

    /// <summary>Every problem the validators report for a loaded input.</summary>
    public static List<string> Problems(GeneratorConfig config, Catalogue catalogue, RunWindow window)
    {
        var problems = ConfigValidator.Validate(config, catalogue.Store, window);
        problems.AddRange(CatalogueValidator.Validate(catalogue, config, window));
        return problems;
    }
}

/// <summary>A temporary directory, deleted on dispose.</summary>
internal sealed class ScratchDirectory : IDisposable
{
    public ScratchDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "waymark-generator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>A copy of the mini fixture inside this directory, to break one file at a time.</summary>
    public string CopyMini()
    {
        var target = System.IO.Path.Combine(Path, "mini");
        foreach (var file in Directory.GetFiles(TestInputs.MiniDirectory, "*", SearchOption.AllDirectories))
        {
            var destination = System.IO.Path.Combine(target, System.IO.Path.GetRelativePath(TestInputs.MiniDirectory, file));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        return System.IO.Path.Combine(target, "configs", "mini.json");
    }

    /// <summary>Replaces text in a file of the copied fixture, failing if the text is not there.</summary>
    public void Edit(string relativePath, string find, string replace)
    {
        var file = System.IO.Path.Combine(Path, "mini", relativePath);
        var text = File.ReadAllText(file);
        Assert.True(text.Contains(find, StringComparison.Ordinal), $"The fixture {relativePath} no longer contains '{find}'; the test is editing nothing.");
        File.WriteAllText(file, text.Replace(find, replace, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A scanner holding a file open is not a test failure; the OS temp cleaner will get it.
        }
    }
}
