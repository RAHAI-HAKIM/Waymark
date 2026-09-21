using System.Text.Json.Serialization;

namespace Waymark.Contracts.Recommendations;

/// <summary>
/// What Local Admin posts when somebody answers a card (hop 7).
///
/// <para>
/// Not a row and not a mirror of one: <see cref="RecommendationDecisionMessage"/> is what the
/// store <i>wrote</i>, and this is what a person <i>asked for</i>. The difference matters —
/// the request names no decision id, no timestamp and no origin, because the store mints all
/// three. A request that could name them could also backdate a decision.
/// </para>
/// <para>
/// Every field is nullable on purpose. A request with a field missing is a refusal with a
/// reason the person can read, not a 400 with a framework's model-binding message.
/// </para>
/// </summary>
/// <param name="RecommendationId">Which card is being answered.</param>
/// <param name="StaffId">Who is answering. Named on the request; no login until Phase 1 (D-069).</param>
/// <param name="Decision"><c>accept</c> or <c>dismiss</c>. Adjust and snooze are Phase 1.</param>
/// <param name="OptionId">Which option was taken. Required for an acceptance, forbidden for a dismissal.</param>
public sealed record DecisionRequest(
    [property: JsonPropertyName("recommendation_id")] string? RecommendationId,
    [property: JsonPropertyName("staff_id")] string? StaffId,
    [property: JsonPropertyName("decision")] string? Decision,
    [property: JsonPropertyName("option_id")] string? OptionId);
