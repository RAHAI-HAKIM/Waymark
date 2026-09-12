using System.Text.Json.Serialization;

namespace Waymark.Contracts.Recommendations;

/// <summary>
/// Why the engine is suggesting this — the interpretability artifact, and the
/// reason the product exists in the form it does.
///
/// <para>
/// <b>Structured, not a sentence</b> (D-044). The UI is Arabic, French and
/// English; a pre-rendered explanation would make the engine locale-aware and
/// turn every wording change into an engine deploy.
/// <see cref="RecommendationEnvelope.Headline"/> is a rendering in the store's
/// configured language; this is the reasoning, and only this is auditable.
/// </para>
/// <para>
/// A judge asking "why does the safety-stock formula use that z-score" is
/// answered from <see cref="Factors"/>, not from the headline (CLAUDE.md §7.4).
/// </para>
/// </summary>
/// <param name="Key">
/// Names the reasoning template — what kind of argument this is. The UI resolves
/// it to a localised sentence.
/// </param>
/// <param name="Params">
/// Values substituted into that template, by name. Free-form because templates
/// differ; every value is a figure or a label, never prose.
/// </param>
/// <param name="Factors">
/// The reasons themselves, each carrying a figure. **At most
/// <see cref="MaxFactors"/>** — CLAUDE.md §5 caps a Because block at three, on
/// the grounds that a fourth reason is a sign the model is being explained
/// rather than the decision.
/// </param>
public sealed record BecauseBlock(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("params")] IReadOnlyDictionary<string, string> Params,
    [property: JsonPropertyName("factors")] IReadOnlyList<BecauseFactor> Factors)
{
    /// <summary>
    /// Three, from CLAUDE.md §5. Stated as a constant rather than enforced here:
    /// these types are serialisable and free of behaviour, so the producer and
    /// the Application-side validator enforce it and this is what they compare
    /// against.
    /// </summary>
    public const int MaxFactors = 3;
}

/// <summary>
/// One reason, with its number. A reason without a figure is an opinion.
/// </summary>
/// <param name="LabelKey">
/// What the figure is, as a key the UI localises — never a rendered label.
/// </param>
/// <param name="Value">
/// The figure, as its exact decimal text. A string rather than a number because
/// this crosses to Python and TypeScript, and a JSON number is a double in both:
/// money and quantity would lose their scale in transit, which is precisely the
/// silent error CLAUDE.md §3.1 exists to prevent.
/// </param>
/// <param name="Unit">
/// What the figure is counted in — a currency code, a unit code, <c>percent</c>,
/// <c>days</c>. Never omitted: a bare number is not a fact.
/// </param>
/// <param name="Direction">
/// Which way this factor pushed. Lets the UI colour or order the reasons without
/// parsing the value.
/// </param>
public sealed record BecauseFactor(
    [property: JsonPropertyName("label_key")] string LabelKey,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("direction")] FactorDirection Direction);

/// <summary>Which way a factor argued.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FactorDirection>))]
public enum FactorDirection
{
    /// <summary>Pushed toward the suggested action.</summary>
    [JsonStringEnumMemberName("supports")] Supports,

    /// <summary>Pushed against it, and is shown anyway. Hiding it would be the dishonest kind of summary.</summary>
    [JsonStringEnumMemberName("opposes")] Opposes,

    /// <summary>Context the shopkeeper needs to read the other two.</summary>
    [JsonStringEnumMemberName("context")] Context,
}
