using System.Text.Json.Serialization;
using Waymark.Contracts.Recommendations;

namespace Waymark.Contracts.Intents;

/// <summary>
/// A decision taken in the cloud, asking the store to do something. Mirrors
/// <c>intents</c>.
///
/// <para>
/// <b>An intent is a request, not a command.</b> The cloud Admin the retailer
/// was looking at may already have been stale when they clicked; the store holds
/// the authoritative data, so it re-evaluates
/// <see cref="Preconditions"/> against current values and rejects the intent if
/// they no longer hold. This is why the field exists and why it is required
/// rather than optional.
/// </para>
/// <para>
/// Re-confirmation was rejected as a safeguard: if the store is offline the
/// picture the operator is looking at is already stale, so asking them to
/// confirm gives them a second chance to decide on the same stale picture. An
/// expired or rejected intent instead becomes a fresh decision request at the
/// store, computed against current data —
/// <see cref="FreshRequestId"/> points at it.
/// </para>
/// </summary>
/// <param name="IntentId">ULID, minted in the cloud.</param>
/// <param name="IntentType">What is being asked for.</param>
/// <param name="StoreId">Which store is being asked.</param>
/// <param name="Payload">The action, in the same shape a recommendation option carries.</param>
/// <param name="Preconditions">
/// What must still be true for this to be safe to apply. Evaluated at the store,
/// against store data, at the moment of application — never in the cloud, and
/// never at the moment of sending.
/// </param>
/// <param name="CreatedAtCloud">When the operator decided. The age the preconditions are judged against.</param>
/// <param name="ExpiresAt">
/// After which the store will not apply it however the preconditions look. Null
/// where the action has no window — windows are tabulated by cost of applying
/// late.
/// </param>
/// <param name="ReceivedAt">When the store took delivery.</param>
/// <param name="EvaluatedAt">When the store decided whether to apply it.</param>
/// <param name="Status">The outcome.</param>
/// <param name="RejectionReason">
/// Why it was refused. **Required when <see cref="Status"/> is either rejected
/// state**, by CHECK — a rejection with no reason is indistinguishable from a
/// bug, and this one reaches an operator who is owed an explanation.
/// </param>
/// <param name="FreshRequestId">
/// The recommendation raised in place of a rejected intent, so the decision
/// comes back to a human against current data rather than being dropped.
/// </param>
/// <param name="DecidedByCloudUser">Who asked.</param>
public sealed record IntentMessage(
    [property: JsonPropertyName("intent_id")] string IntentId,
    [property: JsonPropertyName("intent_type")] IntentType IntentType,
    [property: JsonPropertyName("store_id")] string StoreId,
    [property: JsonPropertyName("payload")] IntentPayload Payload,
    [property: JsonPropertyName("preconditions")] IReadOnlyList<Precondition> Preconditions,
    [property: JsonPropertyName("created_at_cloud")] DateTimeOffset CreatedAtCloud,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("received_at")] DateTimeOffset? ReceivedAt,
    [property: JsonPropertyName("evaluated_at")] DateTimeOffset? EvaluatedAt,
    [property: JsonPropertyName("status")] IntentStatus Status,
    [property: JsonPropertyName("rejection_reason")] string? RejectionReason,
    [property: JsonPropertyName("fresh_request_id")] string? FreshRequestId,
    [property: JsonPropertyName("decided_by_cloud_user")] string? DecidedByCloudUser);

/// <summary>
/// One thing that must still be true at the store for an intent to apply.
///
/// <para>
/// Deliberately a comparison and nothing more. The store-side evaluator may read
/// local data, compare values and do arithmetic on a couple of quantities; it
/// may not fit, aggregate over history, iterate or optimise (CLAUDE.md §5). A
/// precondition that needed any of those would be the engine running inside a
/// transaction, which is the one thing the engine never does.
/// </para>
/// </summary>
/// <param name="Subject">What to look at — a named local quantity the evaluator knows how to read.</param>
/// <param name="Comparison">How to compare it.</param>
/// <param name="Value">
/// What to compare against, as exact decimal text. Text for the same reason
/// every other figure here is: a JSON number is a double on both sides of this
/// boundary.
/// </param>
/// <param name="Unit">What the value is counted in, so a comparison cannot silently cross units.</param>
public sealed record Precondition(
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("comparison")] Comparison Comparison,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("unit")] string Unit);

/// <summary>The comparisons a precondition may use. There are no others, on purpose.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Comparison>))]
public enum Comparison
{
    [JsonStringEnumMemberName("equals")] Equals,
    [JsonStringEnumMemberName("not_equals")] NotEquals,
    [JsonStringEnumMemberName("less_than")] LessThan,
    [JsonStringEnumMemberName("less_than_or_equal")] LessThanOrEqual,
    [JsonStringEnumMemberName("greater_than")] GreaterThan,
    [JsonStringEnumMemberName("greater_than_or_equal")] GreaterThanOrEqual,
}

/// <summary>What is being asked for. Mirrors <c>ck_intents_intent_type</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IntentType>))]
public enum IntentType
{
    [JsonStringEnumMemberName("accept_reorder")] AcceptReorder,
    [JsonStringEnumMemberName("markdown_stage")] MarkdownStage,
    [JsonStringEnumMemberName("adjust_quantity")] AdjustQuantity,
    [JsonStringEnumMemberName("dismiss")] Dismiss,
    [JsonStringEnumMemberName("price_change")] PriceChange,
    [JsonStringEnumMemberName("promotion")] Promotion,
    [JsonStringEnumMemberName("catalogue_edit")] CatalogueEdit,
    [JsonStringEnumMemberName("supplier_edit")] SupplierEdit,
    [JsonStringEnumMemberName("rights_action")] RightsAction,
}

/// <summary>
/// What became of an intent. Mirrors <c>ck_intents_status</c>.
///
/// <para>
/// The two rejections are kept apart because they mean different things to the
/// operator: <see cref="RejectedStale"/> is "you were right, but not any more",
/// and <see cref="RejectedInvalid"/> is "this could not have been applied".
/// Collapsing them would lose the distinction at exactly the moment somebody
/// needs it.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IntentStatus>))]
public enum IntentStatus
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("applied")] Applied,
    [JsonStringEnumMemberName("rejected_stale")] RejectedStale,
    [JsonStringEnumMemberName("rejected_invalid")] RejectedInvalid,
}
