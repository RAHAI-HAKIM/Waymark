using System.Text.Json.Serialization;

namespace Waymark.Contracts.Sync;

/// <summary>
/// A basket, with nobody attached. The first of the two statistics streams
/// (D-043).
///
/// <para>
/// <b>There is no customer column at all</b> — not a nullable one, not an
/// omitted one. That is the point: there is nothing on this record to erase, so
/// it survives an erasure request untouched and a retailer who never buys the
/// customer module still gets basket affinity, replenishment, expiry and
/// spoilage analytics. None of those need a person.
/// </para>
/// <para>
/// It is emitted for every basket unconditionally, including for a customer who
/// has objected. <b>An objection downgrades rather than drops</b>: the retailer
/// keeps their analytics, the customer gets what they asked for, and nothing
/// disappears from the retailer's own books.
/// </para>
/// <para>
/// The coarsenings are deliberate and happen <b>at emit</b>, because after an
/// erasure it is no longer known which rows would need fixing. The test a field
/// has to pass is not "is it an identifier" but "would this still single nobody
/// out once the pseudonym is gone".
/// </para>
/// </summary>
/// <param name="BasketId">
/// Opaque and per-basket. Not derived from the transaction id, so it cannot be
/// joined back to the operational record.
/// </param>
/// <param name="StoreId">Which store.</param>
/// <param name="Date">The day. No time of day — see <see cref="HourBucket"/>.</param>
/// <param name="HourBucket">
/// The hour, 0 to 23. <b>Not a timestamp.</b> An exact second, with a basket's
/// contents, identifies a person to anyone who was in the shop; an hour does not.
/// </param>
/// <param name="DayOfWeek">0 for Sunday. Carried explicitly so the engine never re-derives it from a locale.</param>
/// <param name="Lines">What was bought. Product, not variant, and no free text.</param>
/// <param name="PaymentClass">
/// How it was paid, as a class rather than an instrument — no card reference,
/// no wallet identifier.
/// </param>
/// <param name="HasDiscount">Whether anything was discounted. A flag, not an amount.</param>
public sealed record AnonymousBasketRecord(
    [property: JsonPropertyName("basket_id")] string BasketId,
    [property: JsonPropertyName("store_id")] string StoreId,
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("hour_bucket")] int HourBucket,
    [property: JsonPropertyName("day_of_week")] int DayOfWeek,
    [property: JsonPropertyName("lines")] IReadOnlyList<BasketLine> Lines,
    [property: JsonPropertyName("payment_class")] string PaymentClass,
    [property: JsonPropertyName("has_discount")] bool HasDiscount);

/// <summary>
/// One line of a basket.
///
/// <para>
/// <b>Product, never a name.</b> D-043's standing rule is no free text in the
/// outbox, ever — no notes, no product names, ids only, resolved cloud-side. A
/// product name is the easiest thing in the world to put in a payload and the
/// hardest to get back out.
/// </para>
/// </summary>
/// <param name="ProductId">The product. Not the variant: one level coarser, deliberately.</param>
/// <param name="Quantity">How many, as exact decimal text in the selling unit.</param>
/// <param name="LineValue">What it came to, as exact decimal text.</param>
public sealed record BasketLine(
    [property: JsonPropertyName("product_id")] string ProductId,
    [property: JsonPropertyName("quantity")] string Quantity,
    [property: JsonPropertyName("line_value")] string LineValue);

/// <summary>
/// One customer's month. The second statistics stream (D-043).
///
/// <para>
/// <b>The grain is deliberately mismatched with
/// <see cref="AnonymousBasketRecord"/>.</b> Matching a month of banded spend
/// against a set of baskets is hard; emitting both at transaction grain would
/// let a join reconstruct the identified stream and make the whole separation
/// decorative. That mismatch is the control, not an accident of modelling.
/// </para>
/// <para>
/// Two independent gates gate this stream: the retailer's licence for the
/// customer module, and the individual's objection flag. Neither substitutes for
/// the other.
/// </para>
/// </summary>
/// <param name="CustomerPseudonym">
/// The keyed hash (D-039). Never a customer id, and unresolvable by anyone
/// without the tenant key — which never leaves the premises.
/// </param>
/// <param name="StoreId">Which store.</param>
/// <param name="Period">The month, <c>YYYY-MM</c>.</param>
/// <param name="VisitCount">How many baskets.</param>
/// <param name="TotalSpendBand">
/// <b>A band, not an amount.</b> An exact monthly total is close to unique in a
/// shop with a few hundred regulars, and would still single someone out after
/// the pseudonym had been nulled.
/// </param>
/// <param name="DistinctCategories">How many categories they bought across. A count, never the categories.</param>
/// <param name="RecencyDays">Days since the last visit, at the end of the period.</param>
/// <param name="FirstSeenPeriod">The month they first appeared, <c>YYYY-MM</c>. Months, not a date.</param>
/// <param name="ObjectionFlagAtEmit">
/// Whether the person had objected when this was emitted. Recorded on the record
/// rather than looked up later, because the cloud cannot see the flag and the
/// question "was this lawful to send" is asked about the moment of sending.
/// </param>
public sealed record CustomerPeriodRecord(
    [property: JsonPropertyName("customer_pseudonym")] string CustomerPseudonym,
    [property: JsonPropertyName("store_id")] string StoreId,
    [property: JsonPropertyName("period")] string Period,
    [property: JsonPropertyName("visit_count")] int VisitCount,
    [property: JsonPropertyName("total_spend_band")] string TotalSpendBand,
    [property: JsonPropertyName("distinct_categories")] int DistinctCategories,
    [property: JsonPropertyName("recency_days")] int RecencyDays,
    [property: JsonPropertyName("first_seen_period")] string FirstSeenPeriod,
    [property: JsonPropertyName("objection_flag_at_emit")] bool ObjectionFlagAtEmit);
