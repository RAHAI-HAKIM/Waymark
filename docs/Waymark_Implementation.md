# Waymark — Implementation 

Stage 3 planning record. Owner: Hakim. Opened: 01/09/2026.

**Scope.** Technology stack, project layout, and build sequencing for the implementation stage. Not commercial or legal rules — those are in Waymark\_Operating\_Rules. Not module design — that is System\_Architecture. Not the roadmap itself — that is Project\_Organization.

**Precedence.** Operating\_Rules wins on commercial, legal and privacy matters. System\_Architecture wins on module and data design. This document wins on how the software is built.

**How a decision changes.** Superseded in place, dated, with the old decision kept and marked superseded. Same convention as Operating\_Rules.

---

## 0\. Context and constraints

| Constraint | Value |
| :---- | :---- |
| Available time | 3–4 h/day, \~20 h/week, less once term load resumes |
| Estimated total scope | 500–750 h across the documented v1 feature set |
| Incubator pitch | February 2027 |
| Target hardware | Windows 10 tills, recently purchased. Assume 4 GB RAM, single machine at Basic tier |
| Team | Solo, with Claude Code as implementation assistant |

**Hardware finding (01/09).** Checkout software is a new trend in Algerian retail and observed retailers are running recent Windows 10 machines. This removes the Windows 7 constraint and with it the case for Electron. Recorded because it is load-bearing for §2.  
---

## 1\. Division of work with Claude Code

The division is by **reversibility and silence**, not by difficulty. Hakim owns anything expensive to undo, anything where a wrong answer produces no error message, and anything that must be defended on stage.

### Hakim writes, or specifies precisely and reviews line by line

- Schema and every migration  
- The Statistics tier 1 → tier 2 pseudonymisation boundary and the pseudonym scheme  
- Money and stock arithmetic; the decimal discipline  
- Sync rules: what conflicts, what wins, what is flagged for review  
- The recommendation envelope and the Integration Layer contract  
- Engine method selection, cold-start fallbacks, interval computation  
- Anything the DPIA makes a promise about

### Claude Code writes, Hakim reviews the result

- UI components and CRUD screens  
- The synthetic store generator  
- Test scaffolding and fixtures  
- Migration boilerplate, once the schema change is decided  
- Refactors, renames, mechanical changes across files  
- Glue, configuration, build setup

### Shared — Hakim decides, Claude Code drafts options and argues both sides

- Architecture choices, library selection, debugging

### Two standing rules

1. **Nothing is merged that cannot be explained** — to a sceptical incubator judge asking why the safety-stock formula uses that z-score. Not "roughly understood". The failure mode is 20,000 working lines that cannot be defended, and it is a specific risk here because interpretability *is* the product.  
2. **A decision log is kept.** One file, one entry per non-obvious choice: what, why, what was rejected.

**Mechanism.** `CLAUDE.md` at the repo root carries the architecture rules, the layering contract, the decimal rule and the audit-write rule. Tasks given to Claude Code are small and scoped — "implement the batch expiry query against this schema", never "build the inventory module".  
---

## 2\. Technology stack

Decided 01/09/2026. Three languages, each on the surface it is best at.

| Surface | Choice | Status |
| :---- | :---- | :---- |
| Analytical engine (cloud) | Python | Confirmed |
| Store server | C\# / .NET | Decided |
| POS client | C\# / .NET, Avalonia UI | Decided |
| Admin (local and cloud) | TypeScript web app | Decided |
| Cloud API | Deferred to Phase 4 | Open |

### 2.1 Analytical engine — Python

Unchanged from Stage 2\. OR-Tools CP-SAT, statsforecast, Polars, DuckDB, statsmodels. No other ecosystem carries this stack, and it is the existing Stage 1 body of work.

### 2.2 Store server and POS — C\# / .NET

**Treated as one decision** because at Basic tier both run on the same machine, and a split (Go server \+ .NET POS) would mean two toolchains and no shared types on one box.

Reasons:

- `System.IO.Ports` is first-class; ESC/POS and peripheral libraries are the most mature of any ecosystem.  
- Self-contained publish produces a folder that runs on a machine with no runtime installed. This matters because onboarding is in person, alone, on unfamiliar hardware, and a failed install costs a customer and a day of travel.  
- 40–70 MB footprint against Electron's 150–250 MB, and cold start in a few hundred ms. On a 4 GB till running POS \+ server \+ database, that margin is not theoretical.  
- Shared domain model and types across the two store-side processes.

**Avalonia over WPF** for the POS UI: cross-platform, actively developed, better RTL support for Arabic, and no lock-in to Windows if a Linux till ever makes sense.

**Learning cost:** revised down to 1–2 weeks given existing C++ and Java experience. The unfamiliar surface is `async`/`await`, LINQ and the ecosystem, not the language.

### 2.3 Performance framing

Recorded because it settles future arguments. A busy supérette runs 800–2,000 transactions/day, peaking at roughly 3 sales/minute across three tills. A barcode lookup is one indexed query over 5k–50k variants. **Throughput is not a constraint anywhere in the POS.**

The real client-side constraints are: input latency under \~100 ms scan-to-screen, memory footprint on 4 GB hardware, cold start (a cashier reboots with customers waiting), and no pause during scanning. The client stack is therefore optimised for **footprint, startup and deployability** — never for speed.

Where performance is a genuine engineering problem is the **engine**: incremental recomputation, the tier-2 cache, avoiding full-history rescans. That is Polars/DuckDB work and is independent of every choice above.  
---

## 3\. Data stores

| Layer | Store side | Cloud side |
| :---- | :---- | :---- |
| Operational (transactional writes) | **SQLite** | **Postgres** — subscriptions, tenants, sync state |
| Statistics tier 1 (raw, identified) | **SQLite** | — |
| Statistics tiers 2–3 (pseudonymised) | — | **DuckDB**, one file per tenant |
| Mapping table (`customer_id` ↔ `pseudonym_key`) | ~~SQLite, separate file~~ **removed — keyed hash, no table (D-039)** | never |
| POS Level-2 offline cache | **SQLite, own file and schema** | — |

### 3.1 SQLite for operational data — confirmed

An embedded file: no service to install, no port, no user account, nothing an antivirus flags, nothing a shopkeeper can accidentally stop. Instant startup. A backup is a file copy. WAL-mode write throughput is thousands of transactions/second against a requirement of roughly three per minute. `Microsoft.Data.Sqlite` is first-party in .NET.

**Single-writer objection** does not bite: writes take under a millisecond, and readers do not block the writer in WAL mode. Three terminals against one store server produce no meaningful contention.

**Weak typing objection** is real and is handled by convention, see §3.4.

### 3.2 DuckDB for statistics — confirmed

Volume check: a supérette at 2,000 transactions/day with \~6 lines each produces \~4–5 M line-item rows per year, a few hundred MB per store per year. This is small data.

DuckDB's single-writer embedded limitation is a non-issue: one database file per tenant is what tenant isolation already requires.

### 3.3 The mapping table lives in its own file

> **SUPERSEDED 11/09/2026 by D-039.** There is no mapping table. The property this section
> was buying — "never synchronised, never backed up" as configuration rather than as a rule
> to remember — is the one thing the change genuinely costs, and it is now bought instead
> by a test that fails if the tenant key can reach a backup payload or the outbox.

Not merely its own table. This makes "never synchronised, never backed up to cloud" a property of the sync and backup configuration rather than a rule someone must remember.

### 3.4 Decimal discipline

Monetary columns are declared `INTEGER`, in minor units, and mapped to the `Money` value object in C\#. **Never `REAL`, never `TEXT`.** One Waymark minor unit is 1/100 of a currency unit, for every currency. The convention is enforced by `STRICT` tables in the schema, by `Money` in the domain layer, and by tests — including one that fails if a `decimal` or a `double` ever appears in the value objects' public API.

*Revised 11/09/2026 (decisions.md D-031): this said "`TEXT` or `INTEGER` … mapped to `decimal`", and left fixed-scale strings open. `INTEGER` won because `STRICT` makes it mechanical, and `Money` replaced bare `decimal` because a bare decimal cannot refuse to be added to another currency or to round without being told how.*  
---

## 4\. Admin app — two surfaces, one product

**Problem identified 01/09.** If Admin were served only by the store server, then at Basic tier — where the till *is* the server — the retailer loses Admin whenever the shop is closed and the machine is off. That contradicts the intended offer.

**Resolution.** Admin becomes two surfaces backed by the existing data placement.

**Local Admin** — reached on the store LAN, backed by the store server. Full capability, including everything touching customer and staff identifiers, consent screens, and data-subject requests.

**Cloud Admin** — reachable from anywhere including mobile, backed by the cloud. Dashboard, statistics, all engine department reports and recommendations, catalogue and supplier views, purchase orders. Accept / adjust / dismiss works: the decision is written in the cloud and queued to the store, which applies it on next sync.

**Deliberately absent remotely:** any screen showing a customer's name, phone or email. This is not a limitation to apologise for — it is the pseudonymisation boundary behaving exactly as the DPIA describes, and it is a pitch line:

> *You can check your shop from your phone. Your customers' details never leave your shop.*

**Mobile** is the cloud Admin shipped as a **PWA** — installable, works on Android, no app store, no second codebase. A native app only on demonstrated demand.

**Accepted consequence.** The cloud Admin becomes the surface most retailers use daily and it cannot work offline. Acceptable because the POS — the thing that must never stop — is unaffected.  
---

## 5\. POS hardware reality

Recorded because it is the reason the POS cannot be a browser tab.

| Device | How it actually works |
| :---- | :---- |
| **Receipt printer** | Not a document printer. A USB/serial endpoint speaking **ESC/POS**. No page rendering — a byte stream is written: `1B 40` initialise, `1B 61 01` centre, characters, `1D 56 00` cut. Prints in under a second, no dialog, no driver |
| **Cash drawer** | Not connected to the computer at all. Plugs into the *printer* via RJ11. Opened by sending the printer a pulse command, e.g. `1B 70 00 19 FA`. Drawer control **is** printer control — so the no-sale audit event is a byte sequence emitted deliberately |
| **Barcode scanner** | HID keyboard-wedge: types the digits, presses Enter. No driver, no integration. This is why the POS is keyboard-first — the scanner *is* a keyboard |
| **Scale** | The messy one. Serial and protocol differ by manufacturer (Toledo, CAS, Dibal). Poll or read a continuous weight stream and parse. One integration at a time |

**Main hardware risk in the project:** the ESC/POS layer is written in-house or on a thin library. We’ll budget a day or two, and develop against a fake printer that writes to a file (see §7, `Waymark.Hardware`).  
---

## 6\. Build phases

**Principle.** The product is never shown mid-phase. Each phase ends in a demo-able state; work proceeds as far as time allows and the demo is whatever phase was last completed. This replaces date-based scoping.

**Nothing designed in Stage 2 is deleted.** This is a sequencing decision. The schema ships whole in v1 even where features are dark; the contracts ship whole; and the cold-start table in System\_Architecture is already a graceful-degradation spec — a department running on deterministic heuristics is a department that works, less well, with the upgrade path being method substitution behind a fixed interface.

**Phase 0 — Foundation.** No demo. Repo, solution layout, full schema, migrations, decimal discipline, store scoping, domain model, synthetic store generator, `CLAUDE.md`, decision log. Everything after this assumes it exists.

**Phases details can be found in [Project\_Organization](https://docs.google.com/document/d/1__v-9izSWhX6HAyEUiPZt3c4OppVgwnE5vQdULxpk6Y/edit?usp=sharing)**

**Founding customers** become possible after Phase 3 and comfortable after Phase 4\.

### 6.1 Why the POS is a floor and the engine is a dial

Counterintuitive and worth recording. The instinct is to trim the POS as "just plumbing" and protect the engine as the differentiator. That is backwards.

A shop cannot run on a POS missing refunds, voids or end-of-day reconciliation. A founding customer forced to keep a notebook beside the till is **running in parallel** — which the founding-programme rules identify as the single thing that ruins the data window. Categories A, B, D, F and G are close to incompressible.

Every engine department, by contrast, degrades to a heuristic and is independent of the others. That is where sequencing flexibility lives.

**Statistics is the hidden cost.** Thirty statistics is not thirty SQL queries — it is thirty × (compute \+ cache \+ incremental update \+ chart \+ UI placement). Most exist to feed the engine, and deferred departments do not need theirs. Likely the largest single saving, at almost no visible cost.

### 6.2 Built in Phase 0 regardless of when the feature ships

Retrofitting any of these is the expensive kind of work:

- Full schema, including tables for dark features  
- The recommendation envelope, both `binary` and `menu` action types  
- Integration Layer, both halves, with role gating and the pending queue  
- `processing_log` and `consent_events` writes at every access site  
- Statistics tier 1 → tier 2 pseudonymisation boundary  
- Store scoping on every query  
- Decimal discipline  
- Migrations  
- The synthetic store generator

**On the synthetic store generator:** likely the highest-leverage Phase 0 build. There is no real store data. A fake épicerie with a year of transactions carrying seasonality, expiry, stockouts and returns is what unblocks developing any engine department at all, testing cold-start honestly, and having something to demo in February.

---

## 7\. Solution and project layout

**Why this is decided early.** A .NET project's reference list is compiler-enforced architecture. If `Waymark.Domain` does not reference `Waymark.Persistence`, then nobody — including Claude Code at 2 a.m. — can write a SQL query inside business logic. It is the one architectural rule that enforces itself.

### 7.1 Layering

Dependencies point inward. The centre knows nothing about the outside.

        POS  ·  Server  ·  Admin API        ← hosts

                    ↓

              Application                    ← use cases

                    ↓

                 Domain                      ← rules, entities

                    ↑

        Persistence · Hardware · Sync        ← infrastructure

Infrastructure points *up* into Domain because it implements interfaces Domain declares. Domain declares `IBatchRepository`; Persistence implements it over SQLite; Domain never mentions SQLite. Repository / unit-of-work, per *Architecture Patterns with Python* (Percival & Gregory). This is what makes money and stock logic testable with no database.

### 7.2 Projects — full nine, decided 01/09

**Core**

| Project | Contents |
| :---- | :---- |
| `Waymark.Domain` | Entities, value objects (`Money`, `Quantity`), the rules: expiry windows, markdown ladders, reorder arithmetic, stock-movement validity. **Zero dependencies.** Where most tests live |
| `Waymark.Application` | Use cases as commands and handlers — `CompleteSale`, `ReceiveDelivery`, `AcceptRecommendation`. Orchestrates domain objects and repository interfaces. Also where `processing_log` writes belong, so auditing cannot be forgotten per call site |
| `Waymark.Contracts` | Recommendation envelope, sync message shapes, DTOs. Shared with the TypeScript clients via generated types |

**Infrastructure**

| Project | Contents |
| :---- | :---- |
| `Waymark.Persistence` | EF Core, SQLite, migrations, repository implementations |
| `Waymark.Hardware` | ESC/POS printing, drawer kick, scale serial protocols. Behind interfaces, so development runs against a fake printer writing to a file |
| `Waymark.Pseudonymisation` | The tier 1→2 boundary and the tenant key, isolated so the boundary is a **compile-time fact**. Nothing on the cloud side may reference it, and since D-039 this project holds the key, which makes that absence matter more |
| `Waymark.Sync` | Outbound queue, inbound application, conflict rules |

**Hosts**

| Project | Contents |
| :---- | :---- |
| `Waymark.StoreServer` | ASP.NET Core. Owns the SQLite file, serves local Admin, exposes the API the POS calls, runs sync and scheduled jobs |
| `Waymark.Pos` | Avalonia. UI plus hardware. Talks to StoreServer over local HTTP |

**Tests:** `Waymark.Domain.Tests` (the ones that matter most), `Waymark.Application.Tests`, `Waymark.Integration.Tests`.

**Outside the solution:** `waymark-admin` (TypeScript), `waymark-engine` (Python), `waymark-cloud` (language deferred to Phase 4).

### 7.3 Two layout decisions

**The POS talks HTTP to StoreServer even at Basic tier**, where both are on one machine. One code path instead of two; localhost HTTP costs under a millisecond. The alternative — in-process at Basic, HTTP at Pro — saves nothing and doubles the surface to reason about.

**The POS Level-2 cache is its own SQLite file** with its own schema, holding recent transactions for offline refunds and the current price list. Never confused with the store DB.

### 7.4 Recorded risk

Nine projects is a lot for one person and premature layering is a real failure mode. The three that unambiguously earn their separation are **`Domain`** (testability), **`Pseudonymisation`** (legal defensibility) and **`Hardware`** (develop with nothing plugged in). If time pressure bites, the others may be merged and re-split later. The full nine is this moment's decision and may be revised.  
---

## 8\. Skills and learning

### Already held, needs re-aiming

Python fluency, Polars/DuckDB, SQL, the statistical and OR methods. The gap is not knowledge — it is the move from notebook to scheduled job: sparse input, failure paths, and an output shape carrying an interval and three explanation strings.

### Missing and load-bearing

1. Application architecture — layering, dependency direction, domain logic out of UI and DB  
2. Production database work — migrations, transactions, isolation, constraints as invariants  
3. Frontend — two apps, very different demands. Likely the largest single time sink  
4. Offline-first and sync — the hardest problem in the project  
5. Auth, roles, sessions, PIN gating  
6. Packaging and deployment, including a restore a non-technical user can perform  
7. Testing where errors are silent  
8. Ops hygiene — structured logging, backup verification, a rehearsed restore drill  
9. Security implementation — DPIA §5.4 is currently a claim, not code

### Resources

Deliberately short; most of this is learned by building.

| Topic | Resource | How much |
| :---- | :---- | :---- |
| Architecture | *Architecture Patterns with Python*, cosmicpython.com | Ch. 1–7 properly. The only one worth reading cover to cover |
| Databases | *Designing Data-Intensive Applications* | Ch. 5 and 7 only, as reference |
| Sync | localfirstweb.dev and the local-first literature | \~2 hours, selectively. Single-writer-per-store likely avoids CRDTs entirely via a store-authoritative event log |
| Frontend | Stack docs | After the stack is picked, not before |
| Testing | *Python Testing with pytest* | Reference only |

Everything else is learned on contact.  
---

## 9\. Sync design

How the store and the cloud stay in agreement, and what happens when they cannot talk. Decided 01/09/2026.

Section 1 explains the vocabulary and the four ideas everything else rests on. Section 13 lists what to research to get comfortable with this material.  
---

### 9.1 The vocabulary, first

Sync has its own words. They are simple ideas with intimidating names.

**Sync** — keeping two copies of data in agreement when they are not always connected. If they were always connected there would be no problem: there would be one copy.

**Online / offline** — whether a machine can currently reach the machine it syncs with. In Waymark this is normal, not exceptional: an épicerie's internet will drop, and the system is designed for that rather than surprised by it.

**Store-authoritative** — when the two copies disagree, the store's version is correct. This is already locked in the Operating Rules and is the single assumption that makes everything below simple.

**Outbox** — a table on the store that holds "things the cloud needs to know, not yet sent".

**Inbox** — the same idea in reverse, for things arriving from the cloud that the store has not yet processed.

**Event** — a record of something that happened. "Sale \#4501 occurred at 14:02." Events are facts. A fact cannot be edited, only followed by another fact — a return is a new event, not a correction of the sale.

**Intent** — a record of something someone *asked for*, which has not happened yet. "The retailer accepted a reorder recommendation." An intent can be refused; an event cannot.

**Sequence number** — a counter that increases by one for every outbox row. Row 48,213 comes after row 48,212, always, with no argument. Used instead of clock times, for reasons in §2.2.

**Idempotent** — an operation that is safe to repeat. Applying the same message twice gives the same result as applying it once. This matters because networks fail halfway and messages get resent.

**Checkpoint (or watermark)** — a bookmark. The cloud remembers "I have applied everything up to store sequence 48,213". Reconnecting after three weeks then means one sentence: *send me everything after 48,213.*

**Precondition** — a condition that must still be true for an instruction to be valid. "Apply this markdown, provided batch B still has stock."

**Pseudonymisation** — replacing a real identifier (customer \#17, Amina Belkacem) with a meaningless one (`px_9f2a...`), keeping the link in a separate table so it can be undone locally when legitimately needed. Not encryption and not anonymisation: the link still exists, it just does not travel.

**Split-brain** — two machines both believing they are in charge, each accepting writes, diverging. The one genuinely dangerous failure in this design. See §11.3.  
---

### 9.2 The four ideas everything rests on

#### 9.2.1 Never write directly to the cloud

Waymark works on the rule: **The store always writes locally and never writes remotely.** Completing a sale writes the transaction rows *and* an outbox row in the same SQLite transaction — both or neither, never one without the other. A separate background process drains the outbox whenever it can.

The consequence: online and offline run identical code. The only difference is how quickly the outbox empties. There is no "offline mode" to switch into, and therefore no bug that only appears when the internet drops.

This is called the **outbox pattern**. It is the most important structural choice in this document.

#### 9.2.2 Never trust the clock

A shopkeeper's till may have the wrong date. A cashier may change it. Timezones and daylight saving exist. **No correctness decision in sync may depend on wall-clock time.**

Instead the store server assigns a gapless, always-increasing sequence number to every outbox row. The cloud tracks `last_applied_seq` per store; the store tracks `last_received_seq` from the cloud.

Wall-clock timestamps are still recorded — they appear on screen, and Article 41 bis 3 requires them in the processing log. They are simply never used to decide order or to resolve a disagreement.

#### 9.2.3 Assume every message arrives twice

Delivery is **at-least-once**. Consider: the cloud applies a batch, then the connection dies before the acknowledgement arrives. The store does not know it succeeded, so it resends. This is not an edge case; over months it is a certainty.

Therefore every message carries a stable identity, and **applying it twice must be a no-op**. Implemented with a table of seen message IDs and inserts that are idempotent by natural key.

The store deletes outbox rows only after receiving the acknowledgement. Losing an acknowledgement costs a harmless replay; deleting early costs data.

#### 9.2.4 Identifiers are generated by the machine that writes

**Every primary key is a ULID generated in application code, never a database autoincrement.**

Reason: two terminals working offline at Level 2 would each generate transaction \#4501, and those cannot be merged. ULIDs are unique without coordination.

ULID rather than UUIDv4 because ULIDs sort by creation time, which keeps database indexes well-behaved and makes debugging much easier.  
---

### 9.3 There are two sync problems, not one

Easy to miss, and expensive to miss.

|  | Terminal ↔ store server | Store ↔ cloud |
| :---- | :---- | :---- |
| Distance | Same shop, same LAN | Internet |
| Latency | Milliseconds | Seconds, or none at all |
| Typical outage | Minutes to hours | Minutes to weeks |
| Personal data crosses? | No — same premises | Yes — this is the boundary |
| Number of writers | **Two or more.** Real conflicts | **One.** Almost no conflicts |
| Called | Offline **Level 2** | Offline **Level 1** |
| Exists at Basic tier? | No — the terminal *is* the server | Yes |

They need different machinery. Unifying them would force the LAN layer to carry weight it does not need, and let the cloud layer assume speeds it will not get.

Sections 4–10 are the store↔cloud layer. Section 11 is the terminal↔server layer.  
---

### 9.4 Where the pseudonymisation boundary sits

**Outbound, pseudonymisation happens before the outbox.** What lands in the outbox is already tier-2 shaped and carries no real identifier.

**Inbound, the cloud sends pseudonym-keyed messages.** The store half of the Integration Layer resolves them to a real customer at the moment of delivery to POS or Admin, checks `objection_flag`, and writes `processing_log`. Exactly as the Operating Rules §5 already require.

The invariant this produces, which the project layout enforces at compile time:

> **`Waymark.Sync` does not reference `Waymark.Pseudonymisation`.**

Sync handles pseudonymous data in both directions and never has access to the mapping.  
---

### 9.5 What actually flows — six channels

Separated because they behave differently. Note that five of the six cannot conflict at all.

| \# | Channel | Direction | Nature | Conflicts? |
| :---- | :---- | :---- | :---- | :---- |
| A | Statistics events | store → cloud | Pseudonymised facts, append-only | No — facts don't contradict |
| B | Operational state | store → cloud | Products, prices, batches, suppliers, POs. No personal data | No — store is sole writer |
| C | Recommendations | cloud → store | Pseudonym-keyed, append-only, precomputed nightly | No — append-only |
| D | **Intents** | cloud → store | What the retailer did in Cloud Admin | **The only hard one** |
| E | Control plane | cloud → store | Entitlements, tier, retention config, notice versions | No — cloud-authoritative |
| F | Engine parameters | cloud → store | Reorder points, thresholds, forecast curves. Versioned, each with computed-at | No — cloud-authoritative |

Channel F exists because of the engine split in §8.  
---

### 9.6 Intents, and why there are almost no conflicts

#### 9.6.1 The cloud is not a second writer

**Decision: Cloud Admin never mutates operational state. It queues intents.**

The cloud emits "the retailer asked for X". The store validates and applies. The store's version remains the only truth.

This is why the classic conflict problem — two writers, one row, never arises. It is avoided by there being one writer.

The cost: a price change made from a phone does not take effect until the store syncs. 

#### 9.6.2 What replaces conflict resolution

An intent created at 14:00 arrives at 17:30 and the world has moved. The reorder was for a product discontinued at 15:00. The markdown targeted a batch that sold out.

That is not a conflict. It is an instruction that is no longer valid.

So **every intent carries preconditions**, checked on arrival. Three outcomes:

- **Applied** — preconditions hold.  
- **Rejected as stale** — past its expiry window (§6.3).  
- **Rejected as invalid** — preconditions no longer hold.

**Rejections are never silent.** A rejected intent lands in the pending queue as a fresh decision request, computed against current data, and surfaces to the retailer. Silent rejection is the failure that destroys trust in sync.

#### 9.6.3 Expiry per intent type

**Decision: fixed expiry windows per type.**

Re-confirmation was rejected for a reason worth recording. If the store is offline, the Cloud Admin the retailer is looking at is *already stale*. He decided on data from before the outage. Asking him to re-confirm gives him a second chance to decide on the same stale picture — it looks like a safeguard and is not one.

An expired intent does not vanish. It becomes a fresh decision request at the store, against current data. Same conversation, no round trip.

The organizing principle is: **cost of applying late**. Dismissing a notification costs nothing. Placing a purchase order costs money, these parameters are not locked yet:

| Intent | Window | Reasoning |
| :---- | :---- | :---- |
| Accept a reorder | 48 h | Demand and stock move; a PO placed three days late is a different PO |
| Markdown stage decision | 24 h | Days-to-expiry is the whole input, and it changes daily by definition |
| Adjust a recommended quantity | 48 h | Same clock as the reorder it modifies |
| Dismiss or snooze | never expires | Idempotent, always safe to apply late |
| Price change | 7 days | A deliberate decision; does not go stale on its own |
| Promotion create/schedule | self-expiring | Void if `valid_from` has already passed on arrival |
| Catalogue edits (name, description, category) | 30 days | Facts that do not move |
| Supplier and PO record edits | 7 days | Commercial data, slow-moving |
| Consent and rights actions | never expires | Legal deadlines attach; must apply whenever they land |

#### 9.6.4 The conflicts that genuinely remain

Only two, and both already have rules.

**Two terminals sell the last unit at Level 2\.** Both succeed, stock goes negative, reconciliation surfaces it. Operating Rules §4 already decided this: *never block a sale to protect a number*.

**The same recommendation gets decided in both Cloud Admin and at the store.** `recommendation_id` is the idempotency key. The first decision wins; the second is a no-op with a notice.

---

### 9.7 The mapping table

> **SUPERSEDED 11/09/2026 by `decisions.md` D-039.** There is no mapping table and no
> second file. The pseudonym is `HMAC-SHA256(tenant_key, "waymark:customer:v1:" ‖
> customer_id)`, truncated to 128 bits. The four reasons below were weighed individually
> in D-039: reason 1 survives in a different form (there is no mapping to join *to*), reason
> 2 was always weaker than it reads — the PII lives in `customers` in the operational
> database, so a thief with the till gets the names regardless — reason 4 still holds, and
> **reason 3 is the real cost of the change** and is why the key exclusion is now enforced
> by a test rather than by file-level configuration. §9.7.2 no longer applies at all.
> Kept below because it is the rationale for what was rejected.

**Decision: a second SQLite file on the store machine.**

`waymark-store.db` holds operational data. `waymark-identity.db` holds nothing but `(customer_id, pseudonym_key, created_at)` and the staff equivalent.

This follows DPIA §5.2, which already promises the mapping is "stored separately from the operational database and never synchronised". The schema's `pseudonym_key` column on `Customers` is superseded by this.

Four reasons a separate *file* rather than a separate table:

1. SQLite foreign keys cannot cross files, so the boundary is physically impossible to violate rather than merely forbidden.  
2. It gets its own encryption key. This is what actually mitigates DPIA risk R5 — stolen hardware yields the operational DB but not the link, unless both keys are taken.  
3. Backup and sync exclusion become file-level configuration, not a rule to remember at every call site.  
4. Only `Waymark.Pseudonymisation` holds the path. No other project can open it.

#### 9.7.1 Backup treatment — stated explicitly 

> **Still true, with "tenant key" in place of "identity file" (D-039).** The key goes into
> the local nightly backup to the second device and never into the cloud backup, and the
> recovery consequence below is unchanged — which is why D-039 does not count key loss as a
> new risk. What *did* change is that this is now enforced by a test over the backup
> payload rather than by a file simply not being in the backup set, and that is the real
> cost of the change.


The identity file goes into the **local** nightly backup to the second device. It **never** goes to the cloud backup.

**Recovery consequence, which must be explained at onboarding:** if the store hardware is lost and only the cloud backup survives, the mapping is gone permanently and the pseudonymised history becomes unlinkable. Transactions and totals survive; the connection to named customers does not.

#### 9.7.2 Write ordering — the orphan rule

> **NO LONGER APPLIES (D-039).** One file means one transaction, so the window this
> section manages does not exist. The general rule at the end — *order writes so failure
> leaves garbage, not a gap* — is kept in CLAUDE.md §3.5 because it outlives the case it
> was written for.


Two files means two transactions, which means a window where one has committed and the other has not. You choose which side of that window is safe.

**Write the mapping row first, commit, then write the customer row.**

- Crash in between → a mapping row for a customer that does not exist. Nothing references it, nothing reads it, it costs a few dozen bytes. Harmless garbage.  
- Reverse order, crash in between → a customer with no pseudonym. The first sale hits the boundary and finds nothing to translate. You must then drop the event, block the sale, or mint a pseudonym at the till — and that last one produces two pseudonyms for one person and a permanently split history.

The general rule, which applies anywhere a write spans two stores:

> **Order the writes so that a failure leaves garbage rather than a gap.**

An optional sweep deletes orphan mappings. At a few hundred customers per store it will never matter in practice.

This is also why the domain row and the outbox row go in *one* transaction — so the question never arises there.  
---

### 9.8 The engine split

The biggest architectural consequence of this section.

#### 9.8.1 The tension, and what resolves it

Two forces pulled in opposite directions: poor connectivity argued for keeping the whole engine in the cloud; live notifications and privacy argued for putting some of it at the store.

What settles it is a fact that was already true. **A store-side evaluator is being built regardless.** Expiry detection is the headline claim — works from day one, zero sales history — and it is pure date arithmetic on local batch data. If the engine lived entirely in the cloud, the strongest single claim would require an internet connection to perform a subtraction.

So the evaluator exists. The only question was how much goes into it. And once it exists, adding "is stock below this number" costs nearly nothing.

Supporting argument: Claim 3 is *it keeps working when the internet doesn't*. Basic tier is a single-till épicerie, which is also where connectivity is the worst. An all-cloud engine goes silent exactly where the primary archetype needs it.

#### 9.8.2 The rule that keeps it from getting complicated

> **The cloud fits. The store compares. No formula is implemented twice.**

Comparison is not a second implementation of fitting. This is the discipline that stops the split from doubling the work.

**The store evaluator may:** read local operational data and cloud-supplied parameters; compare values; do arithmetic on a couple of quantities; do date arithmetic.

**The store evaluator may not:** fit models, aggregate over history, iterate, or optimize.

Anything needing more runs in the cloud and arrives as a cached report.

#### 9.8.3 Effect on each department

| Department | Cloud (nightly) | Store (at transaction time) |
| :---- | :---- | :---- |
| **Inventory Group 1** — stock health | Days-of-cover demand rate | Current stock and value, incoming stock, days-of-cover arithmetic, adjustment feed |
| **Inventory Group 2** — replenishment | Reorder point, safety stock, recommended quantity, expected stockout date | **Threshold crossing** — is stock now below the cloud's reorder point |
| **Inventory Group 3** — risk and waste | Overstock, dead-stock and declining thresholds; markdown ladder per category | **Expiry detection and markdown staging** — pure date arithmetic, no parameters needed. Works with zero history |
| **Sales & Demand** | Everything — forecasts, intervals, variance, anomaly detection, product performance | Nothing. Cached reports only |
| **Supply** | Everything — lead-time distributions, supplier comparison and ranking | Nothing. Cached reports only |
| **Planning** | Everything — CP-SAT, simulation, EVSI | Nothing. On-demand |
| **Customer** | Segmentation, RFM, CLV, churn | Loyalty thresholds, tier boundaries, unredeemed points |

**A privacy gain.** Evaluating loyalty and tier rules at the store means those customers' events never need to cross the boundary for those features at all. Less data pseudonymised, less data shipped.

#### 9.8.4 The parameter registry already exists

The consolidated cold-start reference table in System\_Architecture is the parameter registry. Every row already has a name, a default and a consumer.

Add two fields per parameter — a version and a computed-at timestamp — and it becomes the channel F contract directly. It also makes the Because block work offline, since each parameter carries its own provenance.

Consistent with the locked voice rule: *Almanac admits its age as well as its uncertainty.*

#### 9.8.5 The locked rule survives

*“The engine never participates in a live transaction”* still holds. The engine already ran, last night. The store is comparing numbers, not thinking.  
---

### 9.9 Erasure — unlink, do not destroy

**Decision: on erasure, null the pseudonym on the cloud's transaction rows, ~~delete the mapping row,~~ and purge identity columns in the processing log.**

> **Amended 11/09/2026 (D-039).** The mapping-row deletion is gone with the mapping. The
> rest stands unchanged, and the reasoning below is why: nulling the cloud pseudonym was
> always the load-bearing action, because destroying the mapping alone leaves the history
> linked to itself. What is lost is defence in depth — severance now depends on the cloud
> honouring the request, with no local unilateral cut.

#### 9.9.1 Why not simply destroy the mapping

Destroying the mapping leaves the history *linked to itself*: all of that person's baskets still share a pseudonym, so the shopping profile survives, merely unattributed.

That is the **singling-out** problem. In a shop with 200 regulars, a year of timestamped baskets is distinctive enough that someone who knows a person's habits could pick out their row. European anonymisation doctrine treats singling-out as disqualifying on its own.

#### 9.9.2 Why unlinking is better on every axis

- **Legally simpler.** No argument that key destruction constitutes erasure. There is no identifier left to argue about.  
- **No singling-out exposure.** Unlinked baskets cannot be reassembled into a pattern.  
- **The engine loses nothing that matters.** Inventory, Sales & Demand, Supply and Planning use line items, quantities and dates. None of them care who bought what. Only the Customer department does — and that customer asked to be forgotten, so losing the profile is the point.  
- **Restoring is less dangerous.** Restoring the cloud side alone cannot rebuild a profile.

#### 9.9.3 What it costs

- A write pass over that pseudonym's history rather than one row delete. Trivial at a few hundred MB per store per year.  
- It mutates data otherwise treated as append-only — though erasure breaks append-only under any scheme.  
- It requires an erasure message to reach the cloud, queued with **priority**, surviving a long offline window. Article 35's ten-day clock does not pause for a bad connection.

#### 9.9.4 Two constraints that apply regardless

**Backup restore resurrects erased data.** A restore from a backup taken before the erasure brings it back. Therefore an **append-only erasure ledger**, kept outside the erased data, re-applies on every restore. This ledger is also the evidence of compliance — the data itself cannot prove what was erased.

**Erasure can be blocked.** Operating Rules already note the retailer's fiscal retention obligations. A customer with an outstanding credit balance, or an invoice inside the statutory window, cannot be fully erased. The rights tool needs a **blocked-with-reason** state, not just a delete button.

#### 9.9.5 DPIA follow-up

Annex A will be revised  to ask about *this* version.  
---

### 9.10 Transport, cadence, and authentication

#### 9.10.1 Shape — store-initiated, both directions in one round trip

**The store polls. The cloud never dials in.**

Not a compromise. A shop sits behind a consumer router with no fixed address; anything cloud-initiated would need hole-punching or a persistent tunnel. Store-initiated also means the store is never a listening service on the open internet, which removes an entire attack surface.

One round trip carries both directions: the store POSTs its outbox batch plus its `last_received_seq`; the cloud replies with everything after that sequence. One request, bidirectional, nothing to keep alive.

#### 9.10.2 Cadence

| Trigger | Interval | Carries |
| :---- | :---- | :---- |
| Idle poll | 3 minutes | Everything queued |
| Post-transaction nudge | \~15 s debounce after a sale | Sale events, so Cloud Admin is not visibly stale |
| Priority flush | immediate | Erasure, consent withdrawal, objection flags |

Three minutes bounds Cloud Admin staleness; the debounced nudge makes the common case near-immediate without one request per sale.

**Backoff on failure:** 3 s → 10 s → 30 s → 2 min, capped at **5 min**. Never longer, because the shopkeeper's mental model is "it catches up when the internet comes back", and a 30-minute backoff after a brief outage violates that.

#### 9.10.3 Batching and resumption

Batches capped at **500 events or 1 MB**, whichever comes first. The cloud applies a batch in one transaction, persists the new `last_applied_seq`, then acknowledges. The store deletes acknowledged outbox rows only after the ack; a lost ack costs a harmless replay.

**There is no separate catch-up mode.** Resumption is the same loop running until the outbox drains. Three weeks offline is roughly 250k line-item rows — about 500 batches, a few minutes of work.

Bodies are gzipped. The drain is rate-limited so a catch-up burst does not saturate the shop's connection during trading hours.

**Sync must never block the till.** It runs as a background service in `Waymark.StoreServer` with its own SQLite connection. In WAL mode readers do not block the writer, so a long drain cannot stall checkout.

#### 9.10.4 Authentication

**Per-store credentials, not per-retailer.** A multi-store retailer has one account and several store identities. A compromised store must not reach another store's data.

**mTLS with a client certificate per store**, rather than a bearer token. The certificate is bound to the machine, is harder to lift out of a config file, and gives cryptographic store identity at the transport layer rather than something application code must check and might forget on one endpoint. Delivers DPIA §5.4's promises on encryption in transit and tenant isolation.

Issued at onboarding, valid one year, auto-renewed while online. A fallback enrolment token covers the store that has been offline past expiry — that case will happen and needs a path that is not "call the founder".

**Authorisation is separate and must not be skipped.** The certificate proves *which store* is calling. Every write also carries `store_id`, and the server verifies it matches the certificate. This is the guard against DPIA risk R9, cross-tenant leakage, and it belongs in one middleware rather than in each handler — same reasoning as putting `processing_log` writes in `Waymark.Application`.

**Cloud Admin authenticates separately.** Store identity is a machine; Cloud Admin is a person on a phone we do not control, with roles and a session. Conflating them would mean a stolen phone carried store-level cloud access.

**Key handling.** The certificate's private key and the identity-file encryption key both live on the till, protected by Windows DPAPI, tied to the machine account. Adequate for this threat model and requires no key-management infrastructure. This is half of the unwritten "encryption standards" policy in Operating Rules §1.  
---

### 9.11 Level 2 — terminal to store server

The other sync layer. Short outages, no boundary crossing, and — unlike everything above — **real conflicts**, because two isolated terminals are both writing.

#### 9.11.1 What a terminal holds

Its own SQLite file, separate schema:

- Current price list  
- Recent transactions, for offline refunds (Operating Rules: refund allowed only if the original is in local cache)  
- Last-known snapshot of stock and customer balances  
- Its own outbox

ULIDs generated at the terminal, so nothing collides on rejoin.

#### 9.11.2 Reconnection

The terminal pushes its outbox; the store server assigns store sequence numbers in arrival order and applies. Existing Operating Rules cover the outcomes: negative stock permitted and reconciled, above-threshold discounts flagged into a review queue, cross-store refunds blocked.

**The credit ceiling is the one real conflict.** Its value is still open. Two terminals isolated at once can each authorise up to the ceiling against the same last-known balance, so true exposure is **ceiling × number of isolated terminals** — three at Pro. Set the value with that multiplication in mind. Over-limit customers go into the same review queue as discounts rather than being blocked.

#### 9.11.3 Hot replica promotion — manual

At Pro tier a hot replica on a second terminal is required. When the primary dies, the replica must become authoritative. **How that happens is a decision, and it is the one place split-brain can occur.**

**Automatic failover is rejected.** If a LAN switch fails and partitions the terminals, both sides may promote themselves. Two authoritative stores then diverge with real transactions on each — the only genuine data-loss scenario in the whole design.

**Decision: manual promotion, with a loud prompt.**

- The replica offers promotion only after the primary has been unreachable for several minutes.  
- The prompt states plainly that the primary must stay off.  
- Who promoted, and when, is recorded.

Rationale: the segment has low digital literacy and no IT support, but a human pressing a button guarantees a single authority. The alternative is a consensus protocol — serious engineering and a poor use of the remaining weeks.

Phase 5, but it changes what the replica records, so it is written down now.

---

### 9.12 Open items from this section

| Item | Needed by |
| :---- | :---- |
| Level-2 credit ceiling value (remember: × isolated terminals) | Phase 5 |
| Precondition list per intent type | Phase 4 |
| Erasure ledger format and restore-time re-application | Phase 4 |
| Certificate enrolment and renewal flow, including the offline-past-expiry path | Phase 4 |
| Parameter registry schema — version and computed-at fields on the cold-start table | Phase 2 |
| DPIA Annex A revision: ask about unlinking, not mapping destruction | Before ANPDP submission |
| DPIA correction: DPIA §5.2 describes a separately-stored mapping; under D-039 there is no mapping and no identity file — the pseudonym is a keyed hash and the *key* is what is stored separately | Before ANPDP submission |

---

### 9.13 What to research

Ordered roughly by usefulness. Most of these are a blog post or a book chapter, not a course.

#### Read first — these directly explain the decisions above

| Term | Why it matters here |
| :---- | :---- |
| **Outbox pattern** (sometimes "transactional outbox") | §2.1. The whole design rests on it |
| **Idempotency** and **at-least-once delivery** | §2.3. Why messages must be safe to replay |
| **Optimistic vs. pessimistic concurrency** | Background for why store-authoritative removes the problem instead of solving it |
| **Local-first software** | The general name for this architecture. Start at localfirstweb.dev |
| **Offline-first sync** | Practical write-ups, mostly from mobile development |
| **ULID vs. UUID** | §2.4. Short read, immediately useful |
| **SQLite WAL mode** | Why sync cannot block the till |

#### Read when you reach that part

| Term | Where it applies |
| :---- | :---- |
| **Event sourcing** | Channel A. You are not doing full event sourcing, but the vocabulary helps |
| **CQRS** (command/query responsibility segregation) | The intent model in §6 is a light version of this |
| **Split-brain** and **quorum** | §11.3. Enough to understand what manual promotion avoids |
| **Vector clocks**, **Lamport timestamps** | Ordering without trusted clocks. Useful context for §2.2 |
| **CRDTs** (conflict-free replicated data types) | The general solution to multi-writer merge. **You almost certainly do not need these** — store-authoritative avoids the problem. Read enough to know why you skipped them |
| **mTLS** / mutual TLS | §10.4 |
| **Backpressure**, **exponential backoff** | §10.2–10.3 |
| **Idempotency keys in HTTP APIs** | Stripe's public docs are the clearest explanation available |

#### Privacy vocabulary worth being fluent in

| Term | Why |
| :---- | :---- |
| **Pseudonymisation vs. anonymisation** | The distinction the DPIA rests on |
| **Singling out**, **linkability**, **inference** | The three re-identification risks. §9.1 is a singling-out argument |
| **Data minimisation** | Already applied when `address` was dropped from `Customers` |
| **Right to erasure** and its limits | §9.4, and the retention conflict |

#### One book chapter, if you want depth

*Designing Data-Intensive Applications*, chapter 5 (Replication). Read the sections on leader-based replication and on replication lag. That is exactly this problem, described generally. Chapter 7 (Transactions) for the outbox write, which is the two-row transaction that remains after D-039 removed the two-file one.

Skip the chapters on distributed consensus. Manual promotion exists precisely so that material is not needed.  
---

## 10\. Decision log

One entry per non-obvious choice. What, why, what was rejected. Newest at the bottom.

Format: `### NNN — Title` · date · then three short paragraphs.

Entries 001–020 were made during Stage 3 planning and are recorded retrospectively from `Waymark_Implementation` and `Waymark_Sync_Design`.  
---

**001 — C\# / .NET for both store-side surfaces**  
**01/09/2026**

Store server and POS treated as one decision, because at Basic tier both run on the same machine. `System.IO.Ports` is first-class, ESC/POS libraries are the most mature of any ecosystem, and self-contained publish runs on a machine with no runtime installed — which matters because onboarding is in person, alone, on unfamiliar hardware.

Rejected: Python (worst packaging story, and the cost recurs on every customer machine forever); Go (slightly better as a pure server binary, but a split toolchain on one box costs more than it saves); Rust (its cost lands on the code that will be rewritten most).

Enabling finding: observed Algerian retailers run recent Windows 10 machines, which removed the Windows 7 constraint and with it the case for Electron.  
**002 — Avalonia over WPF for the POS UI**  
**01/09/2026**

Cross-platform, actively developed, better RTL support for Arabic, no lock-in to Windows if a Linux till ever makes sense. The POS UI is simple enough that framework capability is not the deciding factor.  
**003 — SQLite for operational data**  
**01/09/2026**

An embedded file: no service, no port, no user account, nothing an antivirus flags, nothing a shopkeeper can accidentally stop. A backup is a file copy. WAL-mode throughput is thousands of writes/second against a requirement of about three per minute.

Rejected: Postgres at the store (a service to install and maintain on a till — the deployment problem .NET was chosen to avoid); SQL Server Express (heavy, Windows-only); LiteDB (loses SQL); DuckDB (column-oriented, built for scans not row updates).

The weak-typing objection is real and is handled by 004\.  
**004 — Decimal discipline**  
**01/09/2026**

Monetary columns are `TEXT` or `INTEGER`, mapped to `decimal` in C\#. Never `REAL`. Minor units as integers or fixed-scale strings. Decided once, enforced in schema and domain layer, covered by tests. Floating-point money is the canonical silent error.

*Narrowed 11/09/2026 by D-031: `INTEGER` only, and the C# side is `Money` rather than a bare `decimal`. The rule above is unchanged in substance — this one just closed the options it left open.*  
**005 — DuckDB for statistics tiers 2–3**  
**01/09/2026**

A supérette produces \~4–5 M line-item rows per year — small data. DuckDB's single-writer embedded limitation is a non-issue because one file per tenant is what tenant isolation already requires.

Rejected: ClickHouse (advantage starts near a billion rows, costs a server, and its strength is cross-tenant scanning which the privacy design forbids); Postgres \+ TimescaleDB (row-oriented, slower for wide scans); Parquet \+ Polars alone (would mean rebuilding query planning by hand).  
**006 — Admin is two surfaces, one codebase**  
**01/09/2026**

If Admin were served only by the store server, then at Basic tier the retailer would lose it whenever the shop is closed and the machine is off. Local Admin (LAN, full capability including identifiers) and Cloud Admin (anywhere, no screen showing a customer name, phone or email). Mobile is the cloud Admin as a PWA — no second codebase, no app store.

The absence of customer identifiers remotely is the pseudonymisation boundary behaving as designed, and it is a pitch line rather than a limitation.  
**007 — Nine .NET projects**  
**01/09/2026**

A project's reference list is compiler-enforced architecture. The three that unambiguously earn separation are `Domain` (testability), `Pseudonymisation` (legal defensibility) and `Hardware` (develop with nothing plugged in).

Recorded risk: nine projects is a lot for one person and premature layering is a real failure mode. The others may be merged and re-split later if time pressure bites.  
**008 — POS talks HTTP to StoreServer even at Basic tier**  
**01/09/2026**

One code path instead of two; localhost HTTP costs under a millisecond. The alternative — in-process at Basic, HTTP at Pro — saves nothing and doubles the surface to reason about.  
**009 — Phase-boundary scoping instead of date-based scoping**  
**01/09/2026**

The product is never shown mid-phase. Each phase ends demo-able; work proceeds as far as time allows and the demo is whatever phase last completed. Nothing designed in Stage 2 is deleted — the schema and contracts ship whole even where features are dark.  
**010 — The outbox pattern**  
**01/09/2026**

The store always writes locally and never writes remotely. Domain rows and the outbox row commit in one transaction. Online and offline then run identical code, differing only in how fast the outbox drains — so there is no bug that appears only when the internet drops.  
**011 — Sequence numbers, never wall-clock time**  
**01/09/2026**

A till may have the wrong date; a cashier may change it. No correctness decision in sync depends on the clock. The store assigns a gapless increasing sequence to every outbox row; cloud and store each track a checkpoint. Timestamps are recorded for display and for Article 41 bis 3, never for ordering.  
**012 — ULIDs generated in application code**  
**01/09/2026**

Two terminals offline at Level 2 would each generate transaction \#4501 under autoincrement, and those cannot be merged. ULID over UUIDv4 because it sorts by creation time, which keeps indexes well-behaved and makes debugging easier.  
**013 — Cloud Admin queues intents, never mutates operational state**  
**01/09/2026**

The cloud emits "the retailer asked for X"; the store validates and applies. This is why the classic two-writer conflict never arises — not because it is solved cleverly, but because there is one writer.

Cost: a price change made from a phone does not take effect until the store syncs. It could not have applied at the till before then anyway.  
**014 — Fixed expiry windows per intent type**  
**01/09/2026**

Rejected re-confirmation. If the store is offline, the Cloud Admin the retailer is looking at is already stale; asking him to re-confirm gives him a second chance to decide on the same stale picture. It looks like a safeguard and is not one.

An expired intent does not vanish — it becomes a fresh decision request at the store, computed against current data. Windows tabulated in `Waymark_Sync_Design` §6.3, organised by cost of applying late.  
**015 — The mapping lives in its own SQLite file** — ~~superseded 11/09/2026 by D-039~~  
**01/09/2026**

`waymark-identity.db`, separate from `waymark-store.db`. SQLite foreign keys cannot cross files, so the boundary is physically impossible to violate. It gets its own encryption key, which is what actually mitigates DPIA risk R5. Backup and sync exclusion become file-level configuration rather than a rule to remember at every call site.

Supersedes the `pseudonym_key` column on `Customers` in DB\_design\_v5. The DPIA needs correcting before submission.  
**016 — Mapping row written first** — ~~no longer applies; one file, one transaction (D-039)~~  
**01/09/2026**

Two files means two transactions and a window between them. Crash after the mapping and before the customer leaves a harmless orphan. The reverse leaves a customer with no pseudonym, and the first sale then has nothing to translate — forcing a dropped event, a blocked sale, or a second pseudonym minted at the till.

General rule: order writes so failure leaves garbage, not a gap.  
**017 — The engine splits: cloud fits, store compares**  
**01/09/2026**

What settled it: a store-side evaluator is being built regardless, because expiry detection is the headline claim and it is pure date arithmetic on local batch data. If the engine lived entirely in the cloud, the strongest single claim would require an internet connection to perform a subtraction.

Once the evaluator exists, adding "is stock below this number" costs nearly nothing. The discipline that stops the split from doubling the work: no formula is implemented twice.

Side benefit: loyalty and tier rules evaluated at the store mean those customers' events never cross the boundary at all.  
**018 — Erasure unlinks rather than destroying the mapping** — *stands; the mapping-row deletion alone is dropped (D-039)*  
**01/09/2026**

Destroying the mapping leaves the history linked to itself: all of that person's baskets still share a pseudonym, so the profile survives, merely unattributed. In a shop with 200 regulars, a year of timestamped baskets is distinctive enough to single someone out.

Instead: null the pseudonym on cloud transaction rows, ~~delete the mapping row,~~ purge identity columns in `processing_log`. Legally simpler, no singling-out exposure, and the engine loses nothing — only the Customer department cares who bought what, and that customer asked to be forgotten. *(D-039: there is no mapping row to delete. The reasoning above is why the rest stands — nulling the cloud pseudonym was always the load-bearing action.)*

Requires an append-only erasure ledger outside the erased data, re-applied on every restore.  
**019 — Manual hot-replica promotion**  
**01/09/2026**

Automatic failover rejected. If a LAN switch fails and partitions the terminals, both sides may promote themselves, and two authoritative stores then diverge with real transactions on each — the only genuine data-loss scenario in the design.

Manual promotion with a loud prompt, offered only after minutes of unreachability, with the promoter and time recorded. The alternative is a consensus protocol: serious engineering and a poor use of the remaining weeks.  
**020 — mTLS with a per-store client certificate**  
**01/09/2026**

Per store, not per retailer: a compromised store must not reach another store's data. The certificate is bound to the machine and gives cryptographic store identity at the transport layer, rather than something application code must check and might forget on one endpoint.

Authorisation stays separate: every write carries `store_id`, verified against the certificate in one middleware. Cloud Admin authenticates separately, as a person with a session — conflating them would mean a stolen phone carried store-level cloud access.  
**021 — Customer department is entitlement-gated, not deferred**  
**02/09/2026**

Customer records and compliance tooling move into Phase 1; customer intelligence into Phase 2, behind a bundle carrying its own legal obligations. Replaces the earlier deferral to Phase 8\.

Consequence: holding a name and a phone number requires the Article 32 notice, recorded consent with notice version, the objection flag and rights tooling with the ten-day clock — from the first customer record, not from Phase 4\. Phase 1 grows accordingly.  
**022 — React for both Admin surfaces**  
**02/09/2026**

Chosen to maximise leverage from Claude Code, which writes React best by a wide margin, and because its ecosystem covers every specific need (TanStack Table, TanStack Query, react-hook-form \+ zod, Recharts) without further evaluation. Also transfers directly to club work at ENSIA.

Rejected: Svelte (least code per feature and easiest to learn, but Svelte 4/5 patterns are mixed in training data, and the table and chart ecosystems are thin); Vue (solid middle ground, less leverage); Angular (steepest curve, most ceremony); Blazor (would share `Money` and `Contracts` directly with the store server and remove TypeScript entirely, but ships a multi-megabyte runtime to a PWA opened on phones over poor connections — the worst case user on the heaviest option).

Headless components over a styled library, because the Almanac card specification — four weights, label before colour, cyan as edge never fill — would fight any opinionated design system.

## 11\. Open items

| Item | Blocking |
| :---- | :---- |
| Which scale protocols to implement | Founding cohort hardware survey |
| Cloud hosting provider | Carried over from Operating\_Rules |

---

*Companion to Project\_Organization, System\_Architecture, Waymark\_Operating\_Rules and Waymark\_DPIA\_v1.*  
