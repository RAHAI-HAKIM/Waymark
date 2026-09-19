# Waymark — Build plan

Phase contents and definitions of done. Owner: Hakim. Opened 02/09/2026, compacted
13/09/2026. Progress is tracked in `status.md`; who writes what is in
`Waymark_Implementation` §1.

**Principle.** The product is never shown mid-phase. Each phase ends demo-able, and the
February demo is whatever phase last completed.

| | Estimate |
| :---- | :---- |
| Rate | ~20 h/week, less in term time |
| Total | 690–940 h |
| To February | ~22 weeks ≈ 440 h, landing inside Phase 3, so **the demo is Phase 2** (the pitch anyway) |

The Customer department is not deferred (02/09). Records and compliance tooling are in
Phase 1; customer intelligence is in Phase 2, behind an entitlement bundle with its own
legal obligations.

---

## Every phase, regardless of content

A phase is done only when:
- `decisions.md` has an entry for every non-obvious choice made during it;
- `CLAUDE.md` and `diagrams/` are updated if a rule or picture changed;
- migrations run forward from an empty database;
- nothing merged that cannot be explained to a sceptical judge;
- **the synthetic store still loads and the previous phase's demo still runs**. This is
  the regression guard.

---

## Phase 0 — Foundation *(complete 17/09/2026; see `recaps/phase-0.md`)*

No demo. The repository, nine projects with tests and CI; the whole schema including dark
features; `Money`, `Quantity`, the rounding mechanisms and ULIDs; store scoping; the
pseudonymisation boundary; contracts; command scaffolding; fake hardware; **the synthetic
store generator** (D-046), the highest-leverage build in the phase.

**Done when:**
- The solution builds; architecture tests pass *and fail* when a forbidden reference is
  added
- The migration creates the store database from empty, **encrypted**, with every trigger
- The generator produces a year of data that loads and looks plausible under inspection
- Value-object tests cover both rounding policies, exact allocation, TVA from TTC, currency
  mismatch, the cash step, and the level/change algebra
- A test that fails when the exclusion is removed proves the tenant key cannot reach a
  backup payload or the outbox

---

## Phase 0.5 — Walking skeleton

**A three-to-five-day slice at the start of Phase 1, before any breadth.** One thin cut
through every layer:

> scan a barcode → add to cart → complete sale → transaction, stock movement, tier 1 →
> pseudonymise → outbox row → a local stub "cloud" reads it → the expiry evaluator flags a
> near-expiry batch → recommendation envelope → Integration Layer role check → card in
> Local Admin → accept → decision written back and logged

One product, one store, one department, ugly UI. Its job is to prove the architecture
holds end to end before 40 screens are built on it.

**Done when** the whole path runs and every hop can be explained. The hop-by-hop gap list
is in `status.md` §6 (Phase 0.5 is next).

---

## Phase 1 — The till runs a shop

**Demo:** a real shop could open on this, with nothing intelligent yet. **150–200 h**, the
largest and least compressible phase.

**A. Checkout.**
- Scan or search; quantity edit; line removal; manual weighted and PLU entry
- Line and transaction discounts with a reason; price override behind manager PIN
- Customer by phone; split tender (cash with change, card, wallet)
- Receipt print and reprint; line and transaction voids; no-sale open (audited)
- Park and resume; refunds linked to the original; store credit
- Paid-in and paid-out; clock in/out; stock lookup

**B. Shift.** X-report; Z-report (gross by tender, count, voids, no-sales,
discounts/overrides, cash variance); float and counted close with variance; handover.

**C. Catalogue.** Product and variant CRUD, categories, bulk price update, archive, CSV
import with validation and a dry-run preview.

**D. Stock.** Receive against a PO (creating batches and inventory); adjustments with a
reason; physical and cycle counts; stock views; manual near-expiry flag.

**E. Customers and compliance.**
- CRUD, with no address field; the Article 32 notice, versioned, as data
- **Two consents** (processing and marketing), each with timestamp, notice version, staff
  member and method; append-only `consent_events`, withdrawal included
- Objection flag checked before customer-directed output
- Manual loyalty, history, tier override, customer discount, credit balance
- Rights tooling (information, access, rectification with the 10-day clock, objection,
  erasure) with a **blocked-with-reason** state; `data_subject_requests`

**F. Staff.** CRUD, roles, PIN, clock and basic scheduling, deactivation.

**J. Store.** Profile, terminals, roles and permissions. Single store only.

**Platform.**
- Sessions, roles, PIN gating; Local Admin served by StoreServer over the LAN
- Offline Level 1 complete: everything works with no cloud, and the outbox accumulates
- Nightly local backup to a second device, **keys included** (never in the cloud backup),
  with a one-click restore a non-technical user can perform
- The recovery-code screen (D-042); real ESC/POS printing, drawer and scanner

**Done when:**
- A simulated day runs start to finish. It opens with a float count and has 30+ sales,
  including a weighted item, a split payment, a discount with reason, an override, a void,
  a no-sale and a refund against an earlier sale. It closes, and the Z-report reconciles
  to the counted cash
- A customer is created, consented, then exercises access, rectification and erasure, with
  erasure blocked while a credit balance exists
- A nightly backup runs, and a restore onto a clean machine reopens the shop
- A receipt prints on real hardware and the drawer opens

**Tests:** domain units (money, refund rules, movement validity, reconciliation);
integration on a real SQLite file; the fake printer plus one real-hardware run; a scripted
manual day; a restore drill. **Owners:** Hakim does refund/void rules, cash
reconciliation, consent and rights logic, PIN gating, anything touching `consent_events`
or `processing_log`. Claude does CRUD screens, cart UI, CSV importer, report rendering,
most of Admin. ESC/POS is drafted by Claude and verified by Hakim against the command
reference. **Risk:** the frontend is the largest time sink. If Phase 1 runs long, cut
Admin polish, never POS function.

---

## Phase 2 — The engine speaks

**Demo: the pitch. Expiry works from day one with zero history, end to end.**
**100–130 h.**

- **Statistics for real:** the tier 1→2 boundary into local DuckDB (D-043), encrypted
  (D-065, how: O-23), replacing Phase 0.5's stub; and the outbox streams, including the
  customer period record and its spend bands, deferred here from 0.5 (D-064). The ~8–10 statistics the first departments need: demand rate, units sold by
  period, stock and value, valuation, days remaining, sell-through, adjustments, gross
  profit, net sales, plus 4 dashboard tiles. Incremental recompute
- **Integration Layer, both halves:** pseudonym-keyed emission; resolution at delivery
  with an `objection_flag` check and a log write; role gating and the pending queue;
  `binary` and `menu` in real use
- **K. Engine interaction:** feed by department; accept, adjust, dismiss, snooze; the
  explanation view; checkout surfacing with permission gating
- **Inventory G1** (stock health) and **G3 expiry only**: date-arithmetic detection, a
  3-stage markdown ladder with expected recovery per stage, a `menu` action, per-category
  thresholds from the registry
- **Customer department** (gated, off by default): RFM, frequency, spend, basket, recency,
  churn, CLV. Non-sensitive categories across all links; credit and tier informational;
  all customer-facing output approved
- **Brand-correct presentation:** the Almanac card in four weights (quiet, standard,
  warning, critical), label before colour, a cyan top edge and never a cyan fill. Because
  block of at most 3 reasons; every number with its interval and computed-at age; Plex
  Mono for figures
- **Parameter registry:** versioned, with `computed_at`, feeding Because provenance

**Done when:**
- Zero sales history plus a delivery of near-expiry batches gives a correct markdown
  recommendation, with a Because block and a menu
- Accepting a stage writes through to the operational DB, statistics and `processing_log`
- An objecting customer receives no customer-directed output at the till
- A category flagged sensitive excludes its products across *every* category link
- A test scanning the DuckDB files finds no direct identifier in tiers 2 or 3

**Tests:** golden files on statistics over the deterministic synthetic store; property
tests on the ladder (monotonic, never above recovery value); boundary tests; no figure
renders without an interval. **Decide in this phase:** the Python↔.NET interchange for
engine output. **Owners:** Hakim does the boundary, statistic definitions, ladder
arithmetic, intervals, sensitive exclusion, and the envelope in practice. Claude does the
feed UI, cards, charts, DuckDB queries to spec, the scheduler.

---

## Phase 3 — It orders for you

**Demo:** the full Basic-tier engine depth from the Operating Rules. **90–120 h.**

- **Sales & Demand:** forecasts per variant and category with prediction intervals, rolling
  updates, variance analysis, anomaly detection, a labelled cold-start fallback
- **Inventory G2:** reorder point; safety stock (ABC-tiered where history allows, otherwise
  90% blended); order quantity; timing and stockout date; store-side threshold crossing;
  **accept creates a PO**
- **G. Suppliers and purchasing:** CRUD and terms, POs by hand or from a recommendation,
  status, discrepancies. **F:** sales per staff member
- **Statistics:** demand variance, lead-time observations, ABC, profitability

**Done when:**
- A backtest over the synthetic year shows interval coverage near nominal
- 0, 5 and 30 days of history each give sensible output, degrading visibly
- Accepting a reorder produces a correct PO against the right supplier
- Every safety-stock figure traces to its inputs and can be explained aloud

Hakim owns the substance: methods, formulas, fallbacks, intervals. Re-derive reorder point
and safety stock by hand first. Claude does the supplier and PO screens, backtest
scaffolding and presentation.

---

## Phase 4 — Reachable and remembering

**Demo:** check your shop from your phone; customer details never leave it. This is where
the DPIA becomes true rather than designed. **110–150 h, the hardest phase.** Read the
`sync-design.md` §13 list *before* starting.

- **Cloud:** API (language decided at phase start), Postgres (tenants, subscriptions, sync
  state), DuckDB per tenant, a hosting provider, the nightly engine
- **Sync** per `sync-design.md`: outbox drain and inbox apply, gapless sequences and
  checkpoints, idempotency, batching, cadence and backoff, a background service that never
  blocks the till. Channels A–F
- **Intents:** preconditions per type, expiry windows, applied / rejected-stale /
  rejected-invalid, with rejections becoming fresh decision requests; `recommendation_id`
  idempotency
- **Auth:** mTLS certificate per store with renewal and an enrolment-token fallback;
  `store_id` checked against the certificate in one middleware; Cloud Admin as a person
  with a session
- **Cloud Admin PWA:** dashboard, statistics, reports, recommendations, catalogue,
  suppliers, POs; decisions queue intents; **no screen with a customer name, phone or
  email**
- **Erasure:** null the pseudonym on cloud rows, priority flush, an append-only erasure
  ledger re-applied on restore
- **Backups:** a cloud backup that never contains `keys\`, proved by a failing-when-removed
  test; a restore drill including ledger re-application; the key-check value enforced

**Done when:**
- Killing the connection mid-batch loses and duplicates nothing
- Replaying a batch twice makes the second a no-op
- Three weeks offline drains with no manual step
- An expired intent becomes a fresh request
- A cross-tenant read with another store's valid certificate fails
- Erase, then restore a pre-erasure backup: the ledger re-applies

**Owners:** Hakim does every sync rule, preconditions, erasure, certificate and
authorisation design. Claude does the PWA, transport plumbing once the rules are written,
the failure-injection harness, deployment scripting.

---

## Phase 5 — Multi-terminal

**Demo:** the Pro archetype is real. **80–110 h.**

- **Offline Level 2** (`sync-design.md` §11): terminal-local SQLite, reconnection
  sequencing, negative stock reconciled, a discount review queue, cross-store refunds
  blocked with an explanation, a credit ceiling sized as ceiling × isolated terminals
- **Hot replica:** continuous replication; **manual promotion only**, recorded
- **Supply department:** lead time and variability, on-time, fulfilment, discrepancies,
  price evolution, availability, ranking (50/50 until revealed preference exists)
- **H. Promotions**, **I. Returns admin**

**Done when:** a terminal unplugged mid-shift keeps selling and reconciles; two isolated
terminals both sell the last unit and reconciliation surfaces the negative; killing the
primary, promoting manually, and confirming no divergence plus a recorded promotion event.

---

## Phase 6 — Planning

**70–90 h.** Budget-constrained multi-product ordering in CP-SAT respecting expiry;
knapsack/MIP scale-back to budget or shelf volume; the remaining statistics; the Planning
UI (inputs, solve, explain). **Done when** a plan is returned for a budget and product
mix and every line explains why this product, why this quantity, and what was traded off.
Model construction from live data, infeasibility and time limits are the new skills.

## Phase 7 — Depth

**110–150 h.** G3 completion (overstock, dead stock, declining); product performance
conclusions; substitution effects; elasticity and the what-if sandbox; the EVSI pilot
tester; the 1 000-month Monte Carlo risk simulator; the Wagner-Whitin batcher
(non-perishables); supplier selection under risk; revealed-preference tuning. The most
safely deferred phase.

---

## Not in any phase

Stock transfers between stores · consolidated purchasing · central catalogue governance ·
role hierarchy beyond store · multi-currency · tax engines · merchant-of-record · native
mobile apps · scale protocols beyond what the founding cohort owns · Arabic RTL beyond what
Avalonia and the web give (receipts stay French in ASCII until Phase 1). Enterprise capability ships only when it
exists (Operating Rules).

## Open items this plan depends on

| Item | Needed by |
| :---- | :---- |
| Python↔.NET interchange for engine output | Phase 2 |
| Tier-2 encryption: how, and which key (O-23) | Phase 2 |
| Customer period record: spend bands (D-064) | Phase 2 |
| Cloud API language, hosting provider | Phase 4 |
| Precondition list per intent type | Phase 4 |
| Level-2 credit ceiling | Phase 5 |
| Scale protocols | Founding-cohort hardware survey |
