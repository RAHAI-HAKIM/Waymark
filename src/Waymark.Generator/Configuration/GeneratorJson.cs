using System.Text.Json;
using System.Text.Json.Serialization;

namespace Waymark.Generator.Configuration;

/// <summary>The one set of JSON rules for every generator input file.</summary>
internal static class GeneratorJson
{
    /// <summary>
    /// snake_case names, comments allowed, and strict about everything else: an unknown field,
    /// a missing required field, a null where a value is required, and an enum given as a
    /// number are all errors rather than defaults.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    /// <summary>Reads <paramref name="path"/> as a <typeparamref name="T"/>, or explains where it is wrong.</summary>
    public static T Read<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new GeneratorInputException($"{path} does not exist.");
        }

        try
        {
            var bytes = InputFile.ReadAllBytes(path);
            return JsonSerializer.Deserialize<T>(bytes, Options)
                ?? throw new GeneratorInputException($"{path} is empty.");
        }
        catch (JsonException error)
        {
            var where = error.LineNumber is { } line ? $" (line {line + 1}, at {error.Path})" : string.Empty;
            throw new GeneratorInputException($"{path}{where}: {error.Message}", error);
        }
    }
}
