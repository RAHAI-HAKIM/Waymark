using System.Text.Json.Serialization;

namespace Waymark.Contracts.Recommendations;

/// <summary>
/// One recommendation, as it crosses between the engine, the store and the UI.
///
/// <para>
/// <b>It mirrors the <c>recommendations</c> table rather than inventing a
/// shape</b> (D-044). The engine writes those rows and the UI reads them; a
/// contract that diverged from the storage would be a translation layer nobody
/// asked for, and the first place the two got out of step would be a bug with no
/// obvious owner.
/// </para>
/// <para>
/// Three of CLAUDE.md §5's locked rules are carried here as required fields
/// rather than as conventions: the interval
/// (<see cref="IntervalLow"/>/<see cref="IntervalHigh"/>), the Because block,
/// and the computed-at age. A figure without a range is a bug, and an output
/// that cannot say how old it is gets shown as fresh.
/// </para>
/// </summary>
/// <param name="RecommendationId">ULID, minted by whoever writes the row.</param>
/// <param name="StoreId">The store this belongs to. Part of the dedupe key.</param>
/// <param name="RecommendationType">
/// What kind of suggestion this is — added by D-044 because <c>department</c>
/// names a surface, not a recommendation. With store, subject type and subject
/// it forms the derived dedupe key that supersession matches on. Not a closed
/// set: the engine that emits these defines it, and pinning it in a CHECK now
/// would cost a table rebuild to change later.
/// </param>
/// <param name="Department">Which surface shows it.</param>
/// <param name="Urgency">How loudly it asks to be seen.</param>
/// <param name="ActionType">One action, or a menu.</param>
/// <param name="SubjectType">What it is about.</param>
/// <param name="SubjectId">
/// The subject's id — and for <see cref="RecommendationSubject.Customer"/> a
/// <b>pseudonym</b>, never a customer id. Resolution to a person happens
/// locally, at delivery, and is logged.
/// </param>
/// <param name="Headline">
/// The rendered sentence, in the store's configured language. A rendering of
/// <see cref="Because"/>, never a substitute for it.
/// </param>
/// <param name="Because">The reasoning. At most three factors, each with a figure.</param>
/// <param name="IntervalLow">
/// Low end of the estimate, as exact decimal text. Null only where the figure is
/// a count rather than an estimate — never to mean "we did not compute one".
/// </param>
/// <param name="IntervalHigh">High end. Null under the same condition, and never independently of the low end.</param>
/// <param name="ComputedAt">
/// When the engine computed this, not when it was delivered. What the UI ages
/// the card against, so stale output is shown as stale rather than hidden.
/// </param>
/// <param name="ParameterVersion">
/// Which version of the parameter registry produced it. Null for a rule that
/// takes no fitted parameter.
/// </param>
/// <param name="Source">
/// What produced it — the engine, a cold-start rule, a fallback. Free text: the
/// column carries no CHECK, and the set belongs to the engine.
/// </param>
/// <param name="MinimumRequiredRole">
/// The lowest role permitted to see it. Enforced at delivery, not in the UI.
/// </param>
/// <param name="Status">Where it is in its life.</param>
/// <param name="IssuedAt">When it was written.</param>
/// <param name="DeliveredAt">When it reached a surface, if it has.</param>
/// <param name="ExpiresAt">
/// When it stops being worth showing. An expired recommendation does not vanish
/// silently — it becomes a fresh decision request computed against current data.
/// </param>
/// <param name="Options">
/// What the shopkeeper may choose. Exactly one for
/// <see cref="ActionType.Binary"/>, two or more for
/// <see cref="ActionType.Menu"/>.
/// </param>
public sealed record RecommendationEnvelope(
    [property: JsonPropertyName("recommendation_id")] string RecommendationId,
    [property: JsonPropertyName("store_id")] string StoreId,
    [property: JsonPropertyName("recommendation_type")] string RecommendationType,
    [property: JsonPropertyName("department")] Department Department,
    [property: JsonPropertyName("urgency")] Urgency Urgency,
    [property: JsonPropertyName("action_type")] ActionType ActionType,
    [property: JsonPropertyName("subject_type")] RecommendationSubject SubjectType,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("because")] BecauseBlock Because,
    [property: JsonPropertyName("interval_low")] string? IntervalLow,
    [property: JsonPropertyName("interval_high")] string? IntervalHigh,
    [property: JsonPropertyName("computed_at")] DateTimeOffset ComputedAt,
    [property: JsonPropertyName("parameter_version")] int? ParameterVersion,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("minimum_required_role")] string MinimumRequiredRole,
    [property: JsonPropertyName("status")] RecommendationStatus Status,
    [property: JsonPropertyName("issued_at")] DateTimeOffset IssuedAt,
    [property: JsonPropertyName("delivered_at")] DateTimeOffset? DeliveredAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("options")] IReadOnlyList<RecommendationOption> Options);

/// <summary>
/// One thing the shopkeeper can choose. Mirrors <c>recommendation_options</c>.
/// </summary>
/// <param name="OptionId">ULID.</param>
/// <param name="Label">Rendered text for the button.</param>
/// <param name="DisplayOrder">Left to right, or top to bottom. Unique per recommendation.</param>
/// <param name="Payload">
/// <b>An executable intent, not a description</b> (D-044). It maps to an
/// Application command — create a purchase order, apply a markdown, flag a
/// batch — and <c>recommendation_decisions.resulting_entity_type</c> and
/// <c>_id</c> close the loop back to what it produced. Without this rule the
/// whole surface is a suggestion box.
/// </param>
/// <param name="ProjectedValue">
/// What choosing this is expected to be worth, as exact decimal text in the
/// store's ledger currency. Null where the option has no modelled outcome — an
/// acknowledge, or a dismissal.
///
/// <para>
/// It carried a unit until the structural check found <c>recommendation_options</c>
/// has no column to put one in. The projection is money, the store has one ledger
/// currency, and inventing a field the store cannot persist is worse than saying
/// so here.
/// </para>
/// </param>
public sealed record RecommendationOption(
    [property: JsonPropertyName("option_id")] string OptionId,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("display_order")] int DisplayOrder,
    [property: JsonPropertyName("payload")] IntentPayload Payload,
    [property: JsonPropertyName("projected_value")] string? ProjectedValue);

/// <summary>
/// What an option does if chosen, in the form the Application layer can act on.
/// </summary>
/// <param name="Command">
/// Names the Application command. The one place the contract and the handlers
/// have to agree, so it is a single named field rather than a shape that varies.
/// </param>
/// <param name="Arguments">
/// Its arguments, by name, as text. Text rather than typed JSON for the same
/// reason <see cref="BecauseFactor.Value"/> is: a JSON number is a double in
/// Python and TypeScript, and a quantity that arrives as 2.9999999 has already
/// failed.
/// </param>
public sealed record IntentPayload(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("arguments")] IReadOnlyDictionary<string, string> Arguments);
