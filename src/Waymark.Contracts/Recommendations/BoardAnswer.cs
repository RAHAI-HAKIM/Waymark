using System.Text.Json.Serialization;

namespace Waymark.Contracts.Recommendations;

/// <summary>
/// What the board answers with: who asked, what they may act on, and what was held back
/// (D-074). Read by Admin and, since A4, by the till's Almanac slot. Named in snake_case like
/// everything else on this wire, so one payload does not make a TypeScript reader switch
/// conventions halfway down (D-044).
/// </summary>
/// <param name="Outcome">One of <see cref="BoardOutcome"/>.</param>
/// <param name="StaffName">Who asked, when the store employs them.</param>
/// <param name="RoleCode">Their role, as <c>roles.role_code</c>.</param>
/// <param name="Withheld">
/// Cards above their rank: <b>counted, never listed</b>. An empty board that hid how many cards
/// exist would tell a cashier the engine found nothing.
/// </param>
/// <param name="Cards">What they may act on.</param>
public sealed record BoardAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("staff_name")] string? StaffName,
    [property: JsonPropertyName("role_code")] string? RoleCode,
    [property: JsonPropertyName("withheld")] int Withheld,
    [property: JsonPropertyName("cards")] IReadOnlyList<RecommendationEnvelope> Cards);

/// <summary>What a decision answers with. A refusal is an answer, not an error (D-066, D-070).</summary>
/// <param name="Outcome">One of <see cref="DecisionOutcome"/>.</param>
/// <param name="DecisionId">The recorded decision, when it was recorded.</param>
/// <param name="Reason">Why it was refused, in words, when it was refused.</param>
public sealed record DecisionAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("decision_id")] string? DecisionId,
    [property: JsonPropertyName("reason")] string? Reason);

/// <summary>The values of <see cref="BoardAnswer.Outcome"/>.</summary>
public static class BoardOutcome
{
    public const string Answered = "answered";

    /// <summary>The staff member named is not active staff of this store.</summary>
    public const string UnknownStaff = "unknown_staff";
}

/// <summary>The values of <see cref="DecisionAnswer.Outcome"/>.</summary>
public static class DecisionOutcome
{
    public const string Recorded = "recorded";
    public const string Refused = "refused";
}
