using System.Text.Json;
using System.Text.Json.Serialization;

namespace Waymark.Generator.Configuration;

/// <summary>Where a behavioural number came from (D-046 §3).</summary>
internal enum ParameterSource
{
    /// <summary>Our best estimate, not yet checked against a shop.</summary>
    Guess,

    /// <summary>Published data, or a documented fact such as a calendar date.</summary>
    Literature,

    /// <summary>Stated by épiciers in the field.</summary>
    Interview,
}

/// <summary>
/// A behavioural parameter and its provenance: <c>{ "value": …, "source": …, "note": … }</c>.
///
/// <para>
/// <b>All three are required, and the note may not be empty.</b> D-046 asks that the review
/// read the sources, not only the values. A number with no source cannot be weighed, and a
/// source with no reason cannot be challenged — the two things a reviewer needs to do with a
/// guess.
/// </para>
/// </summary>
[JsonConverter(typeof(SourcedConverterFactory))]
internal sealed record Sourced<T>(T Value, ParameterSource Source, string Note) : ISourced;

/// <summary>Any sourced parameter, whatever its value type: what the manifest counts.</summary>
internal interface ISourced
{
    ParameterSource Source { get; }
}

/// <summary>Builds a <see cref="SourcedConverter{T}"/> for each <see cref="Sourced{T}"/>.</summary>
internal sealed class SourcedConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Sourced<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(
            typeof(SourcedConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

/// <summary>Reads and writes the object form, refusing anything incomplete.</summary>
internal sealed class SourcedConverter<T> : JsonConverter<Sourced<T>>
{
    public override Sourced<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(
                "A behavioural parameter is an object { \"value\", \"source\", \"note\" }, not a bare value (D-046 §3).");
        }

        var hasValue = false;
        T? value = default;
        ParameterSource? source = null;
        string? note = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            var name = reader.GetString();
            reader.Read();

            switch (name)
            {
                case "value":
                    value = JsonSerializer.Deserialize<T>(ref reader, options);
                    hasValue = value is not null;
                    break;

                case "source":
                    source = reader.TokenType == JsonTokenType.String ? ParseSource(reader.GetString()) : null;
                    if (source is null)
                    {
                        throw new JsonException("\"source\" must be one of guess, literature or interview.");
                    }

                    break;

                case "note":
                    note = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    break;

                default:
                    throw new JsonException($"Unknown field \"{name}\" in a behavioural parameter; expected value, source and note.");
            }
        }

        if (!hasValue)
        {
            throw new JsonException("A behavioural parameter is missing its \"value\".");
        }

        if (source is null)
        {
            throw new JsonException("A behavioural parameter is missing its \"source\" (guess, literature or interview).");
        }

        if (string.IsNullOrWhiteSpace(note))
        {
            throw new JsonException("A behavioural parameter is missing its \"note\": say why the value is what it is.");
        }

        return new Sourced<T>(value!, source.Value, note);
    }

    public override void Write(Utf8JsonWriter writer, Sourced<T> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value.Value, options);
        writer.WriteString("source", value.Source switch
        {
            ParameterSource.Guess => "guess",
            ParameterSource.Literature => "literature",
            _ => "interview",
        });
        writer.WriteString("note", value.Note);
        writer.WriteEndObject();
    }

    private static ParameterSource? ParseSource(string? text) => text switch
    {
        "guess" => ParameterSource.Guess,
        "literature" => ParameterSource.Literature,
        "interview" => ParameterSource.Interview,
        _ => null,
    };
}
