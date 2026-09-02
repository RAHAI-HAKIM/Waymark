# Waymark — Build plan

The detailed expansion of the eight phases. Owner: Hakim. Opened: 02/09/2026.

**Relationship to other documents.** This does not introduce scope; it expands the phase table in Waymark\_Implementation\_Decisions §6 into something that can be worked from daily. Where the two disagree, that table is the shorter statement of the same thing and this file is wrong.

**Principle, restated.** The product is never shown mid-phase. Each phase ends demo-able. Work proceeds as far as time allows; the February demo is whatever phase last completed.

**Change from 02/09.** The Customer department is no longer deferred. Customer records and compliance tooling move into Phase 1; customer intelligence moves into Phase 2, gated behind an entitlement bundle carrying its own legal obligations. Old Phase 8 is removed. **Consequence: Phase 1 grows.** Holding a name and a phone number requires the Article 32 notice, recorded consent with notice version, the objection flag, and rights tooling with the ten-day clock — all from the first customer record, not from Phase 4\.

---

## 0\. Reading the estimates

|  |  |
| :---- | :---- |
| Rate | 3–4 h/day, \~20 h/week, less once term load resumes |
| Total below | 690–940 h |
| February | \~22 weeks ≈ 440 h |

At that rate February lands **inside Phase 3**, so the demo is **Phase 2** — which is the pitch anyway. Phase 3 completing before February would be ahead of plan, not on it.

The total has grown from the earlier 500–750 h. Two honest reasons: the Customer change added real work, and these lists are more granular than the estimate they replace.

---

## 1\. Cross-cutting rules

### 1.1 Every phase, regardless of content

A phase is not done until all of these are true:

- Decision log has an entry for every non-obvious choice made during it  
- `CLAUDE.md` updated if any architecture rule changed  
- `/docs/diagrams` updated if any diagram is now wrong  
- Migrations exist and run forward from an empty database  
- Nothing merged that cannot be explained to a sceptical judge  
- The synthetic store still loads and the previous phase's demo still runs

That last one is the regression guard. It is cheap and it is the thing that stops Phase 4 quietly breaking Phase 1\.

### 1.2 Division of labour — the general shape

Unchanged from Waymark\_Implementation\_Decisions §1: divide by **reversibility and silence**, not by difficulty. Per-phase specifics appear below, but the constant is:

**Hakim** — schema, migrations, the pseudonymisation boundary, money and stock arithmetic, sync rules, the recommendation envelope, engine method selection, cold-start fallbacks, interval computation, anything the DPIA promises.

**Claude Code** — UI components, CRUD screens, the synthetic generator, test scaffolding, migration boilerplate after the schema change is decided, refactors, glue, build setup.

**Shared** — architecture choices, library selection, debugging. Hakim decides, Claude Code argues both sides.

Tasks to Claude Code stay small and scoped. "Implement the batch expiry query against this schema", never "build the inventory module".

### 1.3 On learning

Learn on contact, not in advance. The one exception is C\#, where a week before Phase 0 saves more than it costs. Everything else is learned when the phase needs it.

Channels worth knowing for .NET, both of which teach the same architecture this project uses: **Milan Jovanović** (clean architecture, EF Core, minimal APIs, CQRS — search his channel for the specific topic when the phase needs it) and **Nick Chapsas** (C\# language features, testing, performance). Neither should be watched front to back.

---

## Phase 0 — Foundation

**No demo.** Everything after this assumes it exists. Retrofitting any of it is the expensive kind of work.

**Estimate: 50–70 h.**

### What to implement

**Repository and solution**

- Git repo, `.gitignore`, branch convention (solo: trunk with tags per phase)  
- Solution file, nine projects, three test projects, reference wiring per diagram 05  
- `CLAUDE.md`: layering contract, decimal rule, audit-write rule, store-scoping rule, ULID rule  
- `/docs/decisions.md` and `/docs/diagrams`

**Schema — the whole thing, including dark features**

- Every table from DB\_design\_v5: products, variants, categories, batches, inventories, stock\_movements, prices, promotions, bundles, suppliers, supplier-variant, purchase\_orders and items, transactions and items, returns, customers, staff, shifts, terminals, stores  
- Compliance tables: `consent_events`, `processing_log`, `data_subject_requests`, `retention_policies`  
- `sensitive_flag` and `sensitive_reason` on Categories  
- Parameter registry table — the cold-start reference plus `version` and `computed_at`  
- Outbox and inbox tables, sequence counter  
- First EF Core migration, running forward from empty

**Type and identity discipline**

- `Money` and `Quantity` value objects; monetary columns `TEXT` or `INTEGER`, never `REAL`  
- ULID generation in application code for every primary key  
- Store scoping as an EF Core global query filter, not a per-query `where`

**The two-file split**

- `waymark-store.db` and `waymark-identity.db`, separate connections, separate keys  
- `Waymark.Pseudonymisation`: mapping read/write, the mapping-first write ordering, the tier 1→2 transform interface  
- Only this project holds the identity path

**Contracts**

- Recommendation envelope: `{department, explanation, urgency, action_type: binary|menu, options[], source}`  
- Intent shape with preconditions and expiry  
- Sync message shapes

**Application scaffolding**

- Command/handler skeleton  
- `processing_log` write helper, so auditing is structural rather than remembered

**Synthetic store generator** — likely the highest-leverage build in this phase

- A fake épicerie: \~400 variants, 8 categories, 5 suppliers, 3 staff  
- One year of transactions with weekly and seasonal shape, Ramadan effect, payday spikes  
- Batches with realistic expiry, some spoilage, some stockouts, some returns  
- Configurable: store type, history length, connectivity quality  
- Deterministic from a seed, so tests are repeatable

**Fake hardware**

- ESC/POS writer to a file, drawer-kick logger, scanner simulated as keyboard input

### Skills and tools

| Need | Role and output | How to get there |
| :---- | :---- | :---- |
| C\# / .NET | The store-side language. Output: everything below the UI | One focused week before starting. Language basics, `async`/`await`, LINQ, interfaces and generics. Java and C++ carry most of it |
| EF Core | ORM and migrations. Output: schema as code, forward-runnable | Microsoft Learn's EF Core docs, then Milan Jovanović on EF Core specifics |
| SQLite | The operational store. Output: one file, no service | Its own docs; read the WAL-mode page properly |
| xUnit | Test framework. Output: the safety net for silent errors | Nick Chapsas on testing |
| NetArchTest | Architecture tests. Output: the layering rule enforced by CI, not by discipline | README of the library; an hour |
| ULID library | Sortable IDs. Output: no collisions across offline terminals | Read the ULID spec first (short), then pick a .NET package |

### Definition of done

- Solution builds; architecture tests pass and *fail* when a forbidden reference is added  
- Migration creates both databases from empty  
- The generator produces a year of data that loads and looks plausible under inspection  
- Value-object tests cover money rounding, currency, negative quantities  
- A mapping row and its customer survive a simulated crash between the two writes, leaving garbage rather than a gap

**Test kinds:** unit tests on value objects and domain rules; architecture tests; one smoke test that generated data loads.

### Division of labour

Hakim writes the schema, the value objects, the pseudonymisation project, `CLAUDE.md`, and the ULID and decimal decisions. Claude Code does project scaffolding, migration boilerplate after each schema decision, the generator from a written spec, and test fixtures. Review the generator's *output distributions*, not its code.  
---

## Phase 0.5 — The walking skeleton

**Not a phase. A three-to-five day slice at the start of Phase 1**, before any breadth.

One thin vertical cut through every layer: scan a barcode → add to cart → complete sale → write transaction, stock movement and tier 1 → pseudonymise → outbox row → a local stub "cloud" process reads it → expiry evaluator flags a near-expiry batch → recommendation envelope → Integration Layer role check → card appears in Local Admin → accept → decision written back and logged.

One product, one store, one department, ugly UI. Its only job is to prove the architecture holds end to end before you build 40 screens on top of it. If something in the design is wrong, it is far cheaper to find out here.

**Done when:** the whole path runs, and you can explain every hop in it.

---

## Phase 1 — The till runs a shop

**Demo: a real shop could open on this. Nothing intelligent yet.**

**Estimate: 150–200 h.** The largest phase, and the least compressible.

### What to implement

**POS category A — checkout**

- Scan or manual search; add to cart  
- Quantity edit, line removal before completion  
- Weighted / PLU entry (scale integration deferred; manual weight entry now)  
- Discount per line and per transaction, with reason code  
- Price override behind manager PIN  
- Attach customer by phone lookup  
- Split payment across tender types  
- Cash tendered, change calculation, card, mobile wallet  
- Receipt print; reprint a past receipt  
- Void line, void transaction pre-finalisation  
- No-sale drawer open, logged as an audit event  
- Park and resume a transaction  
- Return / refund linked to the original transaction  
- Store credit  
- Paid-in / paid-out petty cash  
- Clock in / out at the terminal  
- Manual stock lookup mid-sale

**POS category B — shift and end of day**

- X-report (mid-shift, read-only)  
- Z-report: gross by tender, transaction count, voids, no-sales, discounts and overrides, cash variance  
- Drawer float at start, counted total at end, variance flagged  
- Per-cashier handover summary

**Category C — catalogue, manual and CSV**

- Add / edit product and variant  
- Category hierarchy management  
- Bulk price update across a category  
- Discontinue / archive  
- CSV import with validation and a dry-run preview

**Category D — inventory and stock**

- Receive a delivery against a PO, creating Batch and Inventory rows  
- Manual stock adjustment with reason (damage, loss, correction)  
- Physical and cycle count reconciliation  
- View stock by store, variant, batch  
- Manual near-expiry flag (the engine does this in Phase 2\)

**Category E — customers, and the compliance that comes with them**

- Customer CRUD; no address field  
- Article 32 information notice, versioned, held as data  
- Consent capture split into processing and marketing, each with timestamp, notice version, capturing staff, method  
- `consent_events` append-only writes, including withdrawal  
- Objection flag, evaluated before any customer-directed output  
- Loyalty points accrual and redemption, manual  
- Purchase history view; manual tier override; customer-specific discount  
- Credit balance  
- Rights tooling: information, access, rectification, objection, erasure — with the ten-day clock on Article 35 rectification  
- Erasure with a **blocked-with-reason** state for fiscal retention conflicts  
- `data_subject_requests` tracking

**Category F — staff**

- Staff CRUD, role assignment, PIN  
- Clock in / out, basic scheduling  
- Deactivate / terminate

**Category J — store and system administration**

- Store profile, terminals, roles and permissions  
- Single store only; multi-store viewing is Phase 5+

**Platform**

- Auth: sessions, roles, PIN gating at the till  
- Local Admin web app, served by StoreServer over the LAN  
- Offline Level 1 behaviour: everything works with no cloud; the outbox accumulates  
- Nightly local backup to a second device, including the identity file; one-click restore a non-technical user can perform  
- Real hardware: ESC/POS printing, drawer kick, scanner input handling

### Skills and tools

| Need | Role and output | How to get there |
| :---- | :---- | :---- |
| Avalonia | POS UI framework. Output: the till application | Official docs and one build-along tutorial. The UI is simple; don't over-invest |
| ASP.NET Core | StoreServer host. Output: the API both clients call | Milan Jovanović on minimal APIs and authentication |
| TypeScript \+ one frontend framework | Local Admin. Output: catalogue, stock, customer and consent screens | **Decide the framework before starting.** React has the largest ecosystem; Svelte is smaller and faster to learn. Pick one, follow its official tutorial, stop |
| `System.IO.Ports` \+ ESC/POS | Receipt and drawer control. Output: a printed receipt and an opening drawer | Epson's ESC/POS command reference is the real source. Budget a day or two; develop against the fake printer |
| ASP.NET Core Identity or hand-rolled auth | Sessions, roles, PIN. Output: the access control the DPIA assumes | Milan Jovanović on auth; keep it simple, this is a LAN app |
| CSV parsing | Catalogue import. Output: migration path for new customers | A library, an afternoon |

### Definition of done

- A full simulated trading day runs start to finish: open, float count, 30+ sales including a weighted item, a split payment, a discount with reason, a price override, a void, a no-sale, a refund against an earlier sale, close, Z-report reconciles to the cash counted  
- A customer can be created, consented, and then exercise access, rectification and erasure — with erasure correctly blocked when a credit balance exists  
- Backup runs nightly; a restore is performed from scratch onto a clean machine and the shop reopens  
- The receipt prints on real hardware and the drawer opens

**Test kinds:** domain unit tests (money, refund rules, stock movement validity, shift reconciliation); integration tests against a real SQLite file; hardware tests against the fake printer plus one manual run on real hardware; a scripted manual end-to-end day; a rehearsed restore drill.

### Division of labour

Hakim: refund and void rules, cash reconciliation arithmetic, consent and rights logic, PIN gating, anything touching `consent_events` or `processing_log`. Claude Code: the CRUD screens, the cart UI, the CSV importer, report rendering, most of the Admin app. Shared: the ESC/POS layer — Claude Code drafts, Hakim verifies against the command reference and real hardware.

**Risk to watch:** the frontend is the single largest time sink in the project, and this is where it starts. If Phase 1 is running long, cut Admin polish, never POS function.

---

## Phase 2 — The engine speaks

**Demo: this is the pitch. Expiry works from day one with zero history, end to end.**

**Estimate: 100–130 h.**

### What to implement

**Statistics module, for real**

- Tier 1 → tier 2 boundary implemented, not stubbed  
- DuckDB tier 2/3, running as a local process at this stage (cloud is Phase 4\)  
- The \~8–10 statistics the first departments need: demand rate per variant, units sold by period, current stock and value, inventory valuation, days of inventory remaining, sell-through, adjustment feed, gross profit, net sales, plus the four dashboard tiles  
- Incremental recomputation rather than full rescans

**Integration Layer, both halves**

- Cloud half emits pseudonym-keyed recommendations  
- Store half resolves identity at delivery, checks `objection_flag`, writes `processing_log`  
- Role gating and the pending queue for outputs nobody privileged is present to see  
- Envelope in real use, both `binary` and `menu` action types

**POS and Admin category K — engine interaction**

- Recommendation feed grouped by department  
- Accept / adjust / dismiss / snooze  
- The explanation view behind every recommendation  
- Real-time surfacing at checkout, permission-gated

**Inventory Group 1 — stock health**

- Current stock and value by variant, store, batch  
- Incoming stock from open POs  
- Days of inventory remaining  
- Damaged / lost / adjustment feed

**Inventory Group 3 — expiry only**

- Near-expiry batch detection, pure date arithmetic, zero history required  
- Three-stage markdown ladder with expected recovery value per stage  
- `menu` action: pick a stage or dismiss  
- Category-specific markdown thresholds from the parameter registry

**Customer department — gated behind the entitlement bundle**

- RFM, purchase frequency, spending, basket composition, recency segmentation  
- Churn and inactivity detection; customer lifetime value  
- Segmentation on non-sensitive categories only, evaluated across *all* category links  
- Credit and tier outputs informational only — no accept/decline loop  
- All customer-facing output routes through retailer approval  
- Off by default; the bundle carries its own consent and notice obligations

**Brand-correct presentation**

- Almanac card component: four weights (quiet, standard, warning, critical), label before colour, cyan top edge, never a cyan fill  
- Because block: at most three reasons, mono where a figure appears  
- Every number carries its interval; every output carries its computed-at age  
- Plex Mono for labels and figures, Archivo for everything else

**Parameter registry**

- Versioned, each parameter with `computed_at`, feeding the Because block's provenance

### Skills and tools

| Need | Role and output | How to get there |
| :---- | :---- | :---- |
| DuckDB in production | Analytical store. Output: tiers 2–3 and the engine's input | Already known; read the docs on incremental patterns and file-per-tenant |
| Polars beyond notebooks | Computation. Output: statistics that recompute cheaply | Re-aiming, not learning. The gap is failure paths and sparse input |
| Job scheduling | Nightly recompute. Output: the engine runs without you | Hosted services in .NET, or cron on the engine side. An hour |
| Charting in the Admin | Statistics presentation. Output: the four dashboard tiles and department reports | Pick one library and stay with it |
| Python↔.NET interchange | Engine output reaching the store. Output: the parameter and report contract | Design decision, not a skill. Files or HTTP; decide in this phase |

### Definition of done

- Load a synthetic store with **zero sales history**, receive a delivery with near-expiry batches, and get a correct markdown recommendation with a Because block and a menu action  
- Accept a markdown stage; the decision writes through to the operational DB, the statistics module and `processing_log`  
- Set `objection_flag` on a customer and confirm no customer-directed output reaches the till  
- Flag a category sensitive and confirm its products are excluded from customer-level processing across every category link, not just the primary  
- No direct identifier appears anywhere in tier 2 or 3, verified by a test that scans the DuckDB files

**Test kinds:** golden-file tests on statistic computation against the deterministic synthetic store; property tests on the markdown ladder (monotonic discount, never exceeds recovery value); boundary tests proving no identifier crosses; presentation tests that no engine figure renders without an interval.

### Division of labour

Hakim: the boundary implementation, statistic definitions, markdown ladder arithmetic, interval computation, the sensitive-category exclusion logic, the envelope contract in practice. Claude Code: the recommendation feed UI, card components, chart wiring, DuckDB query implementations against Hakim's specifications, the scheduler.

---

## Phase 3 — It orders for you

**Demo: the full Basic tier engine depth as written in the Operating Rules.**

**Estimate: 90–120 h.**

### What to implement

**Sales & Demand**

- Demand forecast per variant and category, with prediction intervals  
- Rolling update on each recompute  
- Variance analysis explaining inaccurate forecasts  
- Anomaly detection with attempted explanation  
- Cold-start fallback to deterministic heuristics, clearly labelled as such

**Inventory Group 2 — replenishment**

- Reorder point calculation  
- Safety stock, ABC-tiered service levels where history allows, 90% blended where it does not  
- Recommended order quantity  
- Reorder timing and expected stockout date  
- Store-side threshold crossing at transaction time, against cloud-computed parameters  
- Accept creates a Purchase Order

**Category G — suppliers and purchasing**

- Supplier CRUD and terms (Supplier-Variant: net days, MOQ, lead time, purchase price)  
- Create a PO manually or from a recommendation  
- Track status: pending, partially received, received  
- Log delivery discrepancies

**Category F completion**

- Sales performance per staff member

**Statistics additions** feeding the above: demand variance, lead-time observations, ABC classification, profitability by product and category.

### Skills and tools

| Need | Role and output | How to get there |
| :---- | :---- | :---- |
| statsforecast in production | Forecasts with intervals. Output: the number and its range | Existing Stage 1 pipeline, hardened for sparse and dirty input |
| Sparse-data handling | Not failing on 11 observations. Output: honest degradation | Learned by writing the fallback paths |
| Inventory theory, defensibly | Reorder point and safety stock. Output: a formula you can defend on stage | Already studied in Hillier & Lieberman. Re-derive it once by hand before implementing |

### Definition of done

- A backtest harness runs the forecaster over the synthetic year and reports interval coverage close to nominal  
- With 0, 5 and 30 days of history the system produces sensible output at each level, degrading visibly rather than silently  
- Accepting a reorder recommendation produces a correct Purchase Order against the right supplier  
- Every safety-stock figure can be traced to its inputs and explained aloud

**Test kinds:** backtesting against known synthetic ground truth; cold-start tests at several history lengths; interval coverage checks; integration test on recommendation-to-PO.

### Division of labour

Hakim owns almost all of this phase's substance — method selection, formulas, fallbacks, intervals. Claude Code: the supplier and PO screens, the backtest harness scaffolding, the report presentation.

---

## Phase 4 — Reachable and remembering

**Demo: check your shop from your phone; customer details never leave it. Also where the DPIA becomes true rather than designed.**

**Estimate: 110–150 h.** The hardest phase.

### What to implement

**Cloud side**

- Cloud API — language decision due at the start of this phase  
- Postgres: tenants, subscriptions, sync state  
- DuckDB per tenant  
- Hosting provider chosen and provisioned (carried over from Operating Rules)  
- The engine moves here and runs nightly

**Sync — store↔cloud**

- Outbox drain, inbox application  
- Gapless sequence numbers; `last_applied_seq` and `last_received_seq` checkpoints  
- Idempotency: stable message IDs, dedupe table, natural-key-idempotent inserts  
- Batching at 500 events or 1 MB; gzip; rate-limited drain  
- Cadence: 3-minute idle poll, \~15 s debounced post-transaction nudge, immediate priority flush  
- Backoff 3 s → 10 s → 30 s → 2 min, capped at 5 min  
- Background service with its own SQLite connection; never blocks the till

**Channels A–F** as specified in Waymark\_Sync\_Design §5, including the engine parameter channel

**Intents**

- Precondition list per intent type  
- Expiry windows per the §6.3 table  
- Applied / rejected-stale / rejected-invalid, with rejections becoming fresh decision requests  
- `recommendation_id` as idempotency key across both Admin surfaces

**Authentication**

- mTLS, client certificate per store, issued at onboarding, one-year validity, auto-renewal  
- Enrolment token fallback for a store offline past expiry  
- `store_id` verified against the certificate in one middleware  
- Cloud Admin authenticates separately, as a person with a session

**Cloud Admin**

- PWA: dashboard, statistics, all department reports and recommendations, catalogue and supplier views, purchase orders  
- Accept / adjust / dismiss queueing intents  
- No screen showing a customer name, phone or email

**Erasure propagation**

- Unlink: null the pseudonym on cloud transaction rows, delete the mapping row, purge identity columns in `processing_log`  
- Priority flush, surviving long offline windows  
- Append-only erasure ledger outside the erased data, re-applied on every restore

**Backups**

- Cloud backup that never contains the identity file  
- Restore drill including erasure ledger re-application

### Skills and tools

| Need | Role and output | How to get there |
| :---- | :---- | :---- |
| The sync vocabulary | Everything in this phase | Waymark\_Sync\_Design §13 research list. Do this reading *before* the phase, not during |
| mTLS and PKI basics | Store identity. Output: DPIA §5.4 delivered | Enough to issue, rotate and revoke a client certificate. Half a day |
| Postgres | Cloud operational store | Known territory; the new part is running it, not querying it |
| PWA | Mobile Admin without an app store | Service worker, manifest, install prompt. A day |
| Cloud deployment | Where it all runs | Provider-specific, learned on contact |

### Definition of done

- Kill the connection mid-batch and confirm no data loss and no duplication on retry  
- Replay a full batch twice and confirm the second is a no-op  
- Simulate three weeks offline, then reconnect and drain — no manual intervention, no separate catch-up path  
- Queue an intent, wait past its expiry, confirm it becomes a fresh decision request rather than applying or vanishing  
- Attempt a cross-tenant read with a valid certificate for another store; it must fail  
- Erase a customer, restore from a pre-erasure backup, confirm the erasure ledger re-applies

**Test kinds:** failure-injection tests (kill mid-batch, duplicate delivery, clock skew); long-offline replay; cross-tenant authorization tests; restore drills; end-to-end intent round trip including both rejection paths.

### Division of labour

Hakim: every sync rule, precondition lists, the erasure path, certificate and authorization design. Claude Code: the Cloud Admin PWA, transport plumbing once the rules are written, the failure-injection harness, deployment scripting.

---

## Phase 5 — Multi-terminal

**Demo: the Pro archetype is now real.**

**Estimate: 80–110 h.**

### What to implement

**Offline Level 2**

- Terminal-local SQLite: price list, recent transactions for offline refunds, last-known stock and balances, own outbox  
- ULIDs generated at the terminal  
- Reconnection: store server assigns sequence numbers in arrival order  
- Negative stock permitted and reconciled  
- Above-threshold discounts flagged into a review queue  
- Cross-store refunds blocked, with an explanation rather than a silent failure  
- Credit ceiling, set with **ceiling × isolated terminals** in mind; over-limit into the review queue

**Hot replica**

- Continuous replication to a second terminal  
- **Manual promotion only**, offered after several minutes of primary unreachability, with a prompt stating the primary must stay off, and a record of who promoted and when

**Supply department**

- Average lead time and lead-time variability  
- On-time delivery, fulfilment rate, quantity discrepancies  
- Purchase price and price evolution, product availability  
- Supplier comparison and ranking, 50/50 price/reliability until revealed-preference data exists

**Categories H and I**

- Promotions: create scoped to product or variant, validity window, activate/deactivate/schedule, price history  
- Returns admin: review and approve above threshold, return-reason analytics

### Skills and tools

Replication and reconciliation patterns; the split-brain material from the research list, read to the depth needed to understand what manual promotion avoids. No new toolchain.

### Definition of done

- Pull the LAN cable on a terminal mid-shift; it keeps selling, and reconciles cleanly on rejoin  
- Two terminals sell the last unit while isolated; both succeed, stock goes negative, reconciliation surfaces it  
- Kill the primary; promote the replica manually; confirm no divergence and a recorded promotion event

**Test kinds:** partition tests; reconciliation tests with deliberately conflicting terminal states; review-queue integration tests.

---

## Phase 6 — Planning

**Estimate: 70–90 h.**

### What to implement

- Budget-constrained multi-product ordering in CP-SAT, respecting expiry limits  
- Knapsack / MIP order allocation that scales back reorder quantities against a monthly budget or shelf volume  
- The remaining statistics from the list of thirty that deferred departments did not need  
- Planning department UI: inputs, solve, explain the result

The explanation requirement is the hard part. A solver output nobody can interrogate breaks the brand promise as surely as an unexplained forecast does.

### Skills and tools

OR-Tools CP-SAT in production rather than in a notebook — the new part is model construction from live data, infeasibility handling, and time limits. Existing Stage 1 work is the foundation.

### Definition of done

Given a budget and a product mix, the solver returns a plan, and every line in the plan can be explained: why this product, why this quantity, what was traded off.

---

## Phase 7 — Depth

**Estimate: 110–150 h.**

### What to implement

- Inventory Group 3 completion: overstock detection, dead stock and slow movers, declining products  
- Product performance conclusions: growth, decline, sales contribution, margin, turnover, demand trend, customer penetration  
- Substitution effect analysis  
- Price elasticity and the what-if simulation sandbox  
- New Product EVSI pilot tester  
- Risk and cash-flow simulator (1,000-month Monte Carlo)  
- Multi-period order batcher (Wagner-Whitin), non-perishables only  
- Supplier selection under risk, with revealed-preference inference  
- Revealed-preference tuning across Inventory Groups 2 and 3

This is the phase where the Stage 1 learning pays off most directly, and the phase most safely deferred.

---

## 2\. What is not in any phase

Recorded so it does not creep in.

Stock transfer between stores · consolidated purchasing · central catalogue governance · role hierarchy beyond store level · multi-currency · tax engines · merchant-of-record integration · native mobile apps · scale protocols beyond whatever the founding cohort actually owns · Arabic RTL beyond what Avalonia and the web give for free.

Enterprise capability ships only when it exists, per Operating Rules §3.

---

## 3\. Open items this plan depends on

| Item | Needed by |
| :---- | :---- |
| Frontend framework choice | Phase 1, before any Admin work |
| Cloud API language | Phase 4 |
| Cloud hosting provider | Phase 4 |
| Level-2 credit ceiling value | Phase 5 |
| Precondition list per intent type | Phase 4 |
| Python↔.NET interchange mechanism for engine output | Phase 2 |
| Which scale protocols to implement | Founding cohort hardware survey |

---

*Companion to Waymark-Implementation, System\_Architecture, Waymark\_Operating\_Rules and Waymark\_DPIA\_v1.*  
