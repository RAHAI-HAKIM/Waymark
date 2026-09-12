using System.Text.Json.Serialization;

namespace Waymark.Contracts.Recommendations;

/// <summary>
/// The surface a recommendation belongs to. Mirrors
/// <c>ck_recommendations_department</c>.
/// </summary>
/// <remarks>
/// A department is <b>not</b> a kind of recommendation. Two different
/// suggestions about the same variant in the same department are different
/// recommendations, which is why
/// <see cref="RecommendationEnvelope.RecommendationType"/> exists (D-044).
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<Department>))]
public enum Department
{
    [JsonStringEnumMemberName("inventory")] Inventory,
    [JsonStringEnumMemberName("sales_demand")] SalesDemand,
    [JsonStringEnumMemberName("supply")] Supply,
    [JsonStringEnumMemberName("planning")] Planning,
    [JsonStringEnumMemberName("customer")] Customer,
}

/// <summary>
/// How loudly a recommendation asks to be seen. Mirrors
/// <c>ck_recommendations_urgency</c>.
/// </summary>
/// <remarks>
/// There is no positive state, deliberately: a shelf that is fine gets no card
/// at all (CLAUDE.md §6). <c>Quiet</c> is the lowest rung of "something is
/// worth saying", not "everything is well".
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<Urgency>))]
public enum Urgency
{
    [JsonStringEnumMemberName("quiet")] Quiet,
    [JsonStringEnumMemberName("standard")] Standard,
    [JsonStringEnumMemberName("warning")] Warning,
    [JsonStringEnumMemberName("critical")] Critical,
}

/// <summary>
/// Whether the shopkeeper is offered one action or a menu. Mirrors
/// <c>ck_recommendations_action_type</c>.
/// </summary>
/// <remarks>
/// Only two, and D-044 rejected adding more. <c>informational</c> is
/// <see cref="Binary"/> with a single acknowledge option; <c>quantified</c> is
/// already a decision of <c>adjust</c> carrying an adjusted payload. Both would
/// have cost an amended CHECK, and amending a CHECK is a table rebuild that
/// silently drops triggers, indexes and other CHECKs (D-022).
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ActionType>))]
public enum ActionType
{
    [JsonStringEnumMemberName("binary")] Binary,
    [JsonStringEnumMemberName("menu")] Menu,
}

/// <summary>
/// What a recommendation is about. Mirrors
/// <c>ck_recommendations_subject_type</c>.
/// </summary>
/// <remarks>
/// <see cref="Customer"/> is the one that forces the Integration Layer path:
/// the subject id is a pseudonym, and resolving it to a person happens locally,
/// once, and is logged. Together with <c>minimum_required_role</c> this is why
/// D-044 rejected a separate <c>requires_reidentification</c> flag — it would
/// have been derivable from these two and free to get out of step.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RecommendationSubject>))]
public enum RecommendationSubject
{
    [JsonStringEnumMemberName("variant")] Variant,
    [JsonStringEnumMemberName("batch")] Batch,
    [JsonStringEnumMemberName("product")] Product,
    [JsonStringEnumMemberName("supplier")] Supplier,
    [JsonStringEnumMemberName("customer")] Customer,
    [JsonStringEnumMemberName("store")] Store,
}

/// <summary>
/// Where a recommendation is in its life. Mirrors
/// <c>ck_recommendations_status</c>.
/// </summary>
/// <remarks>
/// <see cref="Superseded"/> is what makes re-emission safe: the engine updates
/// the live row and writes a superseded one, rather than inserting a second
/// recommendation the shopkeeper would see twice. The row it matches on is found
/// by the derived dedupe key — store, type, subject type, subject — indexed as
/// <c>ix_recs_dedupe</c> (D-044).
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RecommendationStatus>))]
public enum RecommendationStatus
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("delivered")] Delivered,
    [JsonStringEnumMemberName("decided")] Decided,
    [JsonStringEnumMemberName("expired")] Expired,
    [JsonStringEnumMemberName("superseded")] Superseded,
}

/// <summary>
/// What the human did. Mirrors <c>ck_recommendation_decisions_decision</c>.
/// </summary>
/// <remarks>
/// Every recommendation ends here. Nothing decides automatically (CLAUDE.md §4),
/// and the absence of an <c>auto_applied</c> member is that rule expressed in a
/// type rather than a policy.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DecisionKind>))]
public enum DecisionKind
{
    [JsonStringEnumMemberName("accept")] Accept,
    [JsonStringEnumMemberName("adjust")] Adjust,
    [JsonStringEnumMemberName("dismiss")] Dismiss,
    [JsonStringEnumMemberName("snooze")] Snooze,
}

/// <summary>
/// Which side of the boundary the decision was taken on. Mirrors
/// <c>ck_recommendation_decisions_origin</c>.
/// </summary>
/// <remarks>
/// A <see cref="Cloud"/> decision does not act directly: it arrives as an intent
/// and is re-evaluated against current store data before anything happens, which
/// is what stops a decision taken against a stale picture from being applied.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DecisionOrigin>))]
public enum DecisionOrigin
{
    [JsonStringEnumMemberName("store")] Store,
    [JsonStringEnumMemberName("cloud")] Cloud,
}
