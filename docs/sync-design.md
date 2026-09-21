# Sync design

How the store and the cloud stay in agreement, and what happens when they cannot talk.
Decided 01/09/2026, and updated for D-039, D-043 and D-045. Section numbers match the
original `Waymark_Sync_Design`, so older references still resolve. Built mostly in Phase 4;
the outbox write and intent shape are needed in Phase 0.5.

---

## 1. Vocabulary

| Term | Meaning here |
| :---- | :---- |
| Store-authoritative | When copies disagree, the store is right (Operating Rules). Everything below rests on it |
| Outbox / inbox | Store tables of messages not yet sent / not yet applied |
| Event | A fact that happened. Never edited; a return is a new event |
| Intent | Something someone *asked for* in Cloud Admin. It can be refused |
| Sequence number | Gapless counter per outbox row; replaces clock time for ordering |
| Idempotent | Applying a message twice equals applying it once |
| Checkpoint | "Applied everything up to seq N": `last_applied_seq` (cloud), `last_received_seq` (store) |
| Precondition | What must still hold for an intent to apply |
| Split-brain | Two machines both believing they are primary. The one real data-loss scenario (§11.3) |

## 2. The four ideas

### 2.1 Never write directly to the cloud: the outbox pattern
A sale writes its domain rows **and** the outbox row in one SQLite transaction (CLAUDE.md
§3.6, D-050). A background drain empties the outbox whenever it can. Online and offline
run identical code; only drain speed differs, so no bug exists only offline.

### 2.2 Never trust the clock
Tills have wrong dates. **No correctness decision depends on wall-clock time.** StoreServer
assigns a gapless increasing sequence to every outbox row. Timestamps are still recorded
for display and for the processing log (Art. 41 bis 3), never for ordering.

### 2.3 Assume every message arrives twice
Delivery is at-least-once. Every message has a stable id; the receiver keeps seen ids and
uses natural-key idempotent inserts. The store deletes outbox rows **only after the ack**:
a lost ack costs a replay, while deleting early costs data.

### 2.4 Ids are minted by the machine that writes
ULIDs in application code (D-038). Offline terminals cannot collide, and ids sort by
creation time.

## 3. Two sync problems, not one

| | Terminal ↔ store server (Level 2) | Store ↔ cloud (Level 1) |
| :---- | :---- | :---- |
| Network | LAN, milliseconds | Internet, seconds or nothing |
| Typical outage | Minutes–hours | Minutes–weeks |
| Personal data crosses? | No, same premises | This is the boundary |
| Writers | Two or more: real conflicts | One: almost none |
| At Basic tier? | No, the terminal is the server | Yes |

Different machinery for each. §4–§10 cover store↔cloud; §11 covers Level 2.

## 4. The pseudonymisation boundary

**Outbound:** pseudonymisation happens *before* the outbox, in Application. What lands in
the outbox is the two D-043 streams: the anonymous basket record and the monthly customer
period record. Tier 2 itself stays local in DuckDB. **Inbound:** the cloud sends
pseudonym-keyed messages. The store half of the Integration Layer resolves them at
delivery, checks `objection_flag` and writes `processing_log`.

`Waymark.Sync` never references `Waymark.Pseudonymisation` and never touches
`Waymark.Domain.Privacy`; it carries already-shaped payloads (D-051).

## 5. Channels

| | Channel | Direction | Nature | Conflicts? |
| :---- | :---- | :---- | :---- | :---- |
| A | Statistics | store → cloud | The D-043 streams, append-only | No |
| B | Operational state | store → cloud | Products, prices, batches, suppliers, POs. No personal data | No, the store is the sole writer |
| C | Recommendations | cloud → store | Pseudonym-keyed, nightly, append-only | No |
| D | **Intents** | cloud → store | Retailer decisions made in Cloud Admin | **The only hard one** |
| E | Control plane | cloud → store | Entitlements, tier, retention config, notice versions | No, cloud-authoritative |
| F | Engine parameters | cloud → store | Reorder points, thresholds, curves; versioned with `computed_at` | No, cloud-authoritative |

Wire shapes: `OutboundEnvelope` and `InboundEnvelope` in `Waymark.Contracts` (D-049).

## 6. Intents

### 6.1 The cloud is not a second writer
**Cloud Admin never mutates operational state; it queues intents.** The store validates
and applies. That removes the two-writer conflict rather than solving it. Cost: a price
change from a phone takes effect at the next sync.

### 6.2 Preconditions replace conflict resolution
An intent created at 14:00 may arrive at 17:30 after the product was discontinued. That is
not a conflict; it is an instruction that is no longer valid. Each intent carries
preconditions (a single comparison each, D-049) and has three outcomes:
**applied**, **rejected-stale** (past expiry) or **rejected-invalid** (a precondition
failed). **Rejections are never silent.** Each becomes a fresh decision request, computed
on current data, in the pending queue.

### 6.3 Expiry per intent type
Windows are fixed per type, organised by the cost of applying late. Re-confirmation was
rejected: while the store is offline, the Cloud Admin view is already stale, so
re-confirming decides on the same stale picture. **Values not locked.**

| Intent | Window |
| :---- | :---- |
| Accept a reorder / adjust its quantity | 48 h |
| Markdown stage decision | 24 h |
| Dismiss or snooze | Never expires |
| Price change | 7 days |
| Promotion create or schedule | Void if `valid_from` has passed on arrival |
| Catalogue edits | 30 days |
| Supplier or PO record edits | 7 days |
| Consent and rights actions | Never expires; legal deadlines attach |

### 6.4 The conflicts that remain
- **Two isolated terminals sell the last unit:** both succeed, stock goes negative, and
  reconciliation surfaces it ("never block a sale to protect a number").
- **One recommendation decided in both Admins:** `recommendation_id` is the idempotency
  key. The first decision wins; the second is a no-op with a notice.

## 7. The tenant key (was: the mapping table)

There is no mapping table and no identity database (D-039). The pseudonym is a keyed hash;
what is kept apart is the key. It goes into the **local** nightly backup to the second
device and **never** into the cloud backup or any sync payload (D-042). If the hardware is
lost and only the cloud backup survives, the history becomes permanently unlinkable:
totals survive, the link to named customers does not. Explain this at onboarding. After a
restore the key-check value must match before sync resumes. General rule kept from the
old design: **order writes so failure leaves garbage, not a gap.**

## 8. The engine split

**The cloud fits. The store compares. No formula is implemented twice.**

A store-side evaluator exists regardless, because expiry detection (the headline claim,
zero history needed) is date arithmetic on local batches. It must work without internet.
The store evaluator **may** read local data and channel-F parameters, compare, do
arithmetic on a couple of quantities, and do date arithmetic. It **may not** fit,
aggregate over history, iterate or optimise.

| Department | Cloud (nightly) | Store (at transaction time) |
| :---- | :---- | :---- |
| Inventory G1, stock health | Demand rate for days of cover | Stock and value, incoming, days-of-cover arithmetic, adjustment feed |
| Inventory G2, replenishment | Reorder point, safety stock, quantity, stockout date | Threshold crossing against the cloud's reorder point |
| Inventory G3, risk and waste | Overstock, dead-stock, decline thresholds; markdown ladder | **Expiry detection and markdown staging**, with zero history |
| Sales & Demand, Supply, Planning | Everything | Cached reports only |
| Customer | Segmentation, RFM, CLV, churn | Loyalty thresholds, tier boundaries |

The parameter registry (cold-start table in `System_Architecture` §4.7) plus `version` and
`computed_at` is the channel F contract, and it lets the Because block work offline. The
engine still never participates in a live transaction: it ran last night.

## 9. Erasure: unlink, do not destroy

On erasure: **null the pseudonym on the cloud's transaction rows** and record it in the
append-only `erasure_ledger`. The processing log needs nothing, because it never held an
identifier (D-045).

- **Why not destroy a key or mapping.** The history would stay linked to itself, and a
  year of baskets can single someone out.
- **Costs.** A write pass over one pseudonym's history. It mutates otherwise-append-only
  cloud data. The erasure message needs **priority** flush and must survive long outages,
  because Article 35's clock does not pause.
- **A restore resurrects erased data**, so the erasure ledger lives outside the erased
  data and is re-applied on every restore. It is also the evidence of compliance.
- **Erasure can be blocked** by a credit balance or fiscal retention. The rights tool needs
  a **blocked-with-reason** state.

## 10. Transport, cadence, authentication

### 10.1 Shape
**The store polls; the cloud never dials in.** A shop is behind a consumer router and
should never listen on the internet. One round trip: the store POSTs its outbox batch plus
`last_received_seq`, and the cloud replies with everything after it.

### 10.2 Cadence
| Trigger | Interval | Carries |
| :---- | :---- | :---- |
| Idle poll | 3 min | Everything queued |
| Post-sale nudge | ~15 s debounce | Sale events |
| Priority flush | Immediate | Erasure, consent withdrawal, objection |

Backoff 3 s → 10 s → 30 s → 2 min, **capped at 5 min**.

### 10.3 Batching and resumption
Batches are **500 events or 1 MB**, gzipped, with the drain rate-limited during trading
hours. The cloud applies a batch in one transaction, persists `last_applied_seq`, then
acks. There is **no separate catch-up mode**: three weeks offline is roughly 500 batches
through the same loop. Sync runs as a background service in StoreServer with its own
connection. WAL means it never blocks the till.

### 10.4 Authentication
**mTLS with a client certificate per store** (not per retailer), issued at onboarding,
valid one year and auto-renewed, with an enrolment-token fallback for a store offline past
expiry. **Authorisation is separate:** every write carries `store_id`, checked against the
certificate in one middleware (DPIA R9). Cloud Admin authenticates as a person with a
session, never with store credentials. The certificate's private key is protected by
DPAPI on the till.

## 11. Level 2: terminal ↔ store server (Pro tier, Phase 5)

### 11.1 What a terminal holds
Its own SQLite file (never the store DB): price list, recent transactions for offline
refunds, last-known stock and balances, and its own outbox. ULIDs are minted at the
terminal.

### 11.2 Reconnection
The terminal pushes its outbox; StoreServer assigns store sequence numbers in arrival
order. Negative stock is allowed and reconciled. Above-threshold discounts go to a review
queue. Cross-store refunds are blocked with an explanation. **The credit ceiling is the
one real conflict:** exposure is **ceiling × isolated terminals**, so set the value with
that in mind. Over-limit sales go to the review queue.

### 11.3 Hot replica promotion: manual
Automatic failover is rejected, because a partitioned LAN lets both sides promote and
diverge. Promotion is **manual**: offered only after several minutes of primary
unreachability, with a prompt saying the primary must stay off, and who and when recorded.

## 12. Open items

| Item | Needed by |
| :---- | :---- |
| Customer period record: fields at emit and spend bands (the basket is fixed by D-043; D-064) | Phase 2 |
| Precondition list per intent type; lock the §6.3 windows | Phase 4 |
| Erasure ledger format and restore-time re-application | Phase 4 |
| Certificate enrolment and renewal, including the offline-past-expiry path | Phase 4 |
| Level-2 credit ceiling value | Phase 5 |

## 13. Reading, when the phase arrives

Read first: transactional outbox; idempotency and at-least-once delivery (Stripe's
idempotency-key docs); local-first software (localfirstweb.dev); SQLite WAL. Read later:
event sourcing and CQRS vocabulary, split-brain and quorum, Lamport clocks, and CRDTs
(enough to know why they are not needed), mTLS, backoff. Depth: *Designing
Data-Intensive Applications* ch. 5 (replication) and ch. 7 (transactions). Skip
consensus; manual promotion exists so it is not needed.
