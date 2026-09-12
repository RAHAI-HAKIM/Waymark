using System.Text.Json;
using System.Text.Json.Serialization;

namespace Waymark.Contracts.Sync;

/// <summary>
/// A message the store sends. Mirrors the wire-bearing columns of
/// <c>outbox</c>.
///
/// <para>
/// <b>Separate from <see cref="InboundEnvelope"/> on purpose.</b> One envelope
/// for both directions had to carry its channel as a bare string, because the
/// two directions have disjoint channel sets — and it then permitted an inbound
/// message on <c>A_statistics</c>, which cannot exist. Two types cost a few
/// lines and make that unrepresentable.
/// </para>
/// <para>
/// The store initiates every exchange, in both directions, in one round trip.
/// <see cref="SequenceNumber"/> is gapless, monotonic and store-assigned, which
/// is what lets the cloud tell a missing message from a late one — and what
/// makes replay safe: the same sequence arriving twice is a duplicate, not a
/// second event.
/// </para>
/// </summary>
/// <param name="MessageId">
/// ULID. The store's <c>outbox_id</c> on this side and the receiver's
/// <c>message_id</c> on the other: the same value, and the idempotency key at
/// both ends.
/// </param>
/// <param name="Channel">Which stream this belongs to. Only the three a store sends on.</param>
/// <param name="MessageType">
/// What the payload is. Free text rather than an enum: types are added far more
/// often than channels, and a closed set here would make every new message type
/// a breaking change for a peer running an older build.
/// </param>
/// <param name="SequenceNumber">Gapless and monotonic. Store-assigned.</param>
/// <param name="EntityType">
/// What the message is about, where it is about one thing. Deliberately open:
/// <c>outbox</c> carries everything that ever syncs, so its set is the union of
/// every other type vocabulary in the schema and grows every phase.
/// </param>
/// <param name="EntityId">Its id. A pseudonym where the entity is a person.</param>
/// <param name="CreatedAt">When the message was written, not when it was sent.</param>
/// <param name="IsPriority">
/// Whether this jumps the queue on the next drain. Decisions and rights actions
/// do; statistics never do.
/// </param>
/// <param name="Payload">
/// The message itself, still as JSON. Left unparsed so the transport can route,
/// count, sequence and replay without knowing every payload shape that exists —
/// which is what keeps adding a message type out of the transport.
/// </param>
public sealed record OutboundEnvelope(
    [property: JsonPropertyName("message_id")] string MessageId,
    [property: JsonPropertyName("channel")] OutboundChannel Channel,
    [property: JsonPropertyName("message_type")] string MessageType,
    [property: JsonPropertyName("sequence_number")] long SequenceNumber,
    [property: JsonPropertyName("entity_type")] string? EntityType,
    [property: JsonPropertyName("entity_id")] string? EntityId,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("is_priority")] bool IsPriority,
    [property: JsonPropertyName("payload")] JsonElement Payload);

/// <summary>
/// A message the store receives. Mirrors the wire-bearing columns of
/// <c>inbox</c>.
///
/// <para>
/// Carries only what crossed. What the store then does about it —
/// <c>received_at</c>, <c>applied_at</c>, <c>status</c>,
/// <c>rejection_reason</c> — is local bookkeeping the sender never sees, and
/// putting it on the wire shape would invite somebody to trust a status the
/// other side wrote.
/// </para>
/// </summary>
/// <param name="MessageId">The sender's id for this message. The idempotency key.</param>
/// <param name="Channel">Which stream. Only the four a store receives on.</param>
/// <param name="MessageType">What the payload is.</param>
/// <param name="CloudSequence">Gapless and monotonic, cloud-assigned.</param>
/// <param name="Payload">The message itself, still as JSON.</param>
public sealed record InboundEnvelope(
    [property: JsonPropertyName("message_id")] string MessageId,
    [property: JsonPropertyName("channel")] InboundChannel Channel,
    [property: JsonPropertyName("message_type")] string MessageType,
    [property: JsonPropertyName("cloud_sequence")] long CloudSequence,
    [property: JsonPropertyName("payload")] JsonElement Payload);

/// <summary>
/// The channels a store sends on. Mirrors <c>ck_outbox_channel</c>.
///
/// <para>
/// The letters are part of the value, not decoration — they order the drain,
/// and they are why these cannot be spelled by a mechanical naming rule.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<OutboundChannel>))]
public enum OutboundChannel
{
    /// <summary>The two statistics streams of D-043. Never priority.</summary>
    [JsonStringEnumMemberName("A_statistics")] Statistics,

    /// <summary>Catalogue, stock, purchasing — no personal data.</summary>
    [JsonStringEnumMemberName("B_operational")] Operational,

    /// <summary>What the shopkeeper decided about a recommendation.</summary>
    [JsonStringEnumMemberName("D_decisions")] Decisions,
}

/// <summary>The channels a store receives on. Mirrors <c>ck_inbox_channel</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InboundChannel>))]
public enum InboundChannel
{
    /// <summary>Engine output, keyed by pseudonym where it concerns a person.</summary>
    [JsonStringEnumMemberName("C_recommendations")] Recommendations,

    /// <summary>Cloud decisions asking the store to act, subject to preconditions.</summary>
    [JsonStringEnumMemberName("D_intents")] Intents,

    /// <summary>Entitlements, configuration, operational control.</summary>
    [JsonStringEnumMemberName("E_control")] Control,

    /// <summary>Fitted parameters, each with a version and a computed-at.</summary>
    [JsonStringEnumMemberName("F_parameters")] Parameters,
}

/// <summary>
/// What became of a received message. Mirrors <c>ck_inbox_status</c>.
///
/// <para>
/// Local to the store, which is why it is not on <see cref="InboundEnvelope"/>.
/// <see cref="Duplicate"/> is a success rather than a failure: it is what
/// idempotent replay looks like from the inside, and a sync design that could
/// not say it would have to guess.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<InboundStatus>))]
public enum InboundStatus
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("applied")] Applied,
    [JsonStringEnumMemberName("duplicate")] Duplicate,
    [JsonStringEnumMemberName("rejected")] Rejected,
}
