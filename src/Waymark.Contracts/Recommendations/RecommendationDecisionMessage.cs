using System.Text.Json.Serialization;

namespace Waymark.Contracts.Recommendations;

/// <summary>
/// What a human did about a recommendation. Mirrors
/// <c>recommendation_decisions</c>.
///
/// <para>
/// Every recommendation ends in one of these. **Nothing decides automatically**
/// (CLAUDE.md §4), and credit and tier outputs are informational with no
/// accept/decline loop at all — so there is no member here for a machine having
/// acted, and no field recording that one did.
/// </para>
/// <para>
/// Append-only in storage, guarded by a trigger, and immutable once applied: a
/// correction is a new decision, never an edit to this one.
/// </para>
/// </summary>
/// <param name="DecisionId">ULID.</param>
/// <param name="RecommendationId">What was decided about.</param>
/// <param name="Decision">Accept, adjust, dismiss or snooze.</param>
/// <param name="ChosenOptionId">
/// Which option was taken. Null for a dismissal, and for a snooze.
/// </param>
/// <param name="AdjustedPayload">
/// The amended intent when the shopkeeper changed the numbers. **Required when
/// <see cref="Decision"/> is <see cref="DecisionKind.Adjust"/>** and forbidden
/// otherwise — the database enforces both halves with a CHECK. This is also why
/// D-044 rejected a <c>quantified</c> action type: an adjusted quantity is
/// already expressible, and the enforcement already exists.
/// </param>
/// <param name="SnoozeUntil">
/// When to ask again. **Required when <see cref="Decision"/> is
/// <see cref="DecisionKind.Snooze"/>**, by CHECK.
/// </param>
/// <param name="Origin">
/// Store or cloud. A cloud decision arrives as an intent and is re-evaluated
/// against current data before it applies.
/// </param>
/// <param name="DecidedBy">
/// The staff member. Null for a cloud decision.
///
/// <para>
/// There is no cloud-user field here, and there was one until the structural
/// check found <c>recommendation_decisions</c> has no column for it.
/// <see cref="Origin"/> already says a cloud user decided, and <i>which</i> one
/// is on the intent that produced the decision — so the answer is a join away
/// rather than a column the store cannot write.
/// </para>
/// </param>
/// <param name="DecidedAt">When the person chose.</param>
/// <param name="TerminalId">Which till, where the decision was taken at one.</param>
/// <param name="AppliedAt">
/// When the store acted on it. Null while a decision is taken but not yet
/// applied — and the moment this is set the decision becomes immutable.
/// </param>
/// <param name="ResultingEntityType">
/// What the decision produced — a purchase order, a price, a promotion. With
/// <see cref="ResultingEntityId"/> this closes the loop from a suggestion to the
/// thing it actually created, which is what makes the option payload an
/// executable intent rather than a description (D-044).
/// </param>
/// <param name="ResultingEntityId">The id of that thing.</param>
public sealed record RecommendationDecisionMessage(
    [property: JsonPropertyName("decision_id")] string DecisionId,
    [property: JsonPropertyName("recommendation_id")] string RecommendationId,
    [property: JsonPropertyName("decision")] DecisionKind Decision,
    [property: JsonPropertyName("chosen_option_id")] string? ChosenOptionId,
    [property: JsonPropertyName("adjusted_payload")] IntentPayload? AdjustedPayload,
    [property: JsonPropertyName("snooze_until")] DateTimeOffset? SnoozeUntil,
    [property: JsonPropertyName("origin")] DecisionOrigin Origin,
    [property: JsonPropertyName("decided_by")] string? DecidedBy,
    [property: JsonPropertyName("decided_at")] DateTimeOffset DecidedAt,
    [property: JsonPropertyName("terminal_id")] string? TerminalId,
    [property: JsonPropertyName("applied_at")] DateTimeOffset? AppliedAt,
    [property: JsonPropertyName("resulting_entity_type")] string? ResultingEntityType,
    [property: JsonPropertyName("resulting_entity_id")] string? ResultingEntityId);
