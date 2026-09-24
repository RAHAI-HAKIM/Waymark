# Phase 1 execution plan — The till runs a shop

**Agreed 20/09/2026; Phase 0.5 closed and Phase 1 opened 21/09/2026.** How Phase 1 is
built: the pace, who writes what, the session backlog, and what every piece of work expands.

This is the *how*; `Waymark_Build_Plan.md` remains the *what* and the definition of done, and
wins if the two disagree. Edited rarely — when the backlog is re-forecast (end of blocks B
and G), when a block is reordered, or when scope changes. Day-to-day progress lives in
`status.md`, not here.

---

## 1. Context

Phase 0.5 proved the architecture holds end to end: one thin cut from a scanned barcode to a
decided recommendation, through every layer, with nothing faked but the cloud. Phase 1 is not
a new architecture — **it is that cut, widened**. Every piece of work below names the Phase 0.5
file it expands, so the starting point is always something already reviewed and understood.

Two things change in how we work. Hakim moves from reviewing to **writing the
decision-bearing core** — refunds, voids, cash reconciliation, discounts, consent, rights,
permissions — which is what the Build Plan assigned him all along. And the till and Admin get
**real screens**, which means a design step before any of them is built.

The Build Plan budgets Phase 1 at 150–200 h and calls it "the largest and least compressible
phase". At 4 h a session, **one or two sessions a day, every day**, that is
**≈46 sessions over 23–46 days** — late October 2026 at one a day, mid-October at two.
The spread is wide on purpose: it is the honest range, not a forecast.

---

## 2. Pace and shape

| | |
| :---- | :---- |
| Session | **4 h. One or two a day, seven days a week.** One session = one commit = one reading guide, as in 0.5 |
| Backlog | **≈46 sessions**, ordered by dependency. 46 × 4 h = 184 h, inside the Build Plan's band |
| Calendar | Phase 1 opened **22/09/2026**. **23–46 days**: mid-October at two sessions a day, late October at one. Re-forecast at the end of blocks B and G |
| Two in a day | Only when the second is independent of the first. Two sessions inside one block share a file and a review; a second session is better spent opening the next block than doubling back |
| Scope | Everything the Build Plan lists. **Admin function over Admin polish** — plain screens, no polish pass until the end, first thing cut if we run long |
| Sequencing | **POS vertical** (domain → handler → endpoint → till UI in one session, so the simulated day is runnable throughout); **Admin batched per area**, after its server side is tested |

**The regression guard, every session:** the generator still loads, and the previous demo
still runs. Phase 0.5's end-to-end path is now part of what must not break.

---

## 3. How the work is shared

The Build Plan's owner split, made concrete per session:

| | Hakim writes | Claude writes |
| :---- | :---- | :---- |
| Money and stock arithmetic | refund and void rules, discount allocation, tender and change, cash reconciliation | the code around it, the wire, the screens |
| Privacy | consent, objection, rights logic, anything touching `consent_events` or `processing_log` | the CRUD, the forms, the rendering |
| Access | PIN gating, permissions, who may do what | sessions, endpoints, plumbing |
| Everything else | reviews | CRUD screens, cart UI, CSV importer, report rendering, Admin, tests, docs |

### The session contract

1. **Spec** — one paragraph: what it is, **its Phase 0.5 ancestor**, the one risky rule, who
   writes what. Ten minutes, together, before anything is written.
2. **Claude writes the tests first, and they fail.** The port, the signature, a guiding
   comment block, and red tests.
3. **Hakim makes them green.** Starting from the 0.5 file the work expands, which he has
   already read.
4. **Claude builds everything around it** — plumbing, endpoints, UI, DI, docs.
5. **Break the code on purpose** (D-012) on the session's risky rule, and all parts concerned by a test.
6. **Reading guide** appended to `status.md`, then review and **one commit**.

**Why tests-first:** in session D the role check (D-074) was written with the comparison
inverted — a cashier could decide a manager's card and an owner could not. Four tests written
*before* the code caught it in seconds. That is the safest way to hand over more writing, and
it is the method for the whole phase, not a one-off.

---

## 4. The design gate ✍ Hakim

**No screen is built before its design exists.** Hakim brings the design; Claude reviews it
against the brand rules and says so plainly *before* any code is written.

| Gate | Before | Covers |
| :---- | :---- | :---- |
| **G1** | Block B (till depth) | The till: cart, tender, discount and override dialogs, notices |
| **G2** | Block E (first Admin batch) | Admin shell: navigation, tables, forms, the card pattern |
| **G3** | Block G (customers) | Customer screens — and **what Cloud Admin hides**, which is absent, not disabled |

**Review checklist** (CLAUDE.md §6 and the brand deck): violet `#5A3AA8` is the operator and
nothing else is; cyan `#0E8C86` is Almanac only — never a button, link, text or card fill;
cyan text is `#0A5F5B`; engine cards are white with a cyan top edge; **two semantic colours
only**, critical and warning, and **there is no positive state**; **label before colour**;
Archivo for text, IBM Plex Mono for figures; RTL from logical properties, never retrofitted.

---

## 5. Tool ramp ✍ Hakim

| Tool | Needed from | Lead time | The ramp |
| :---- | :---- | :---- | :---- |
| **Avalonia** | Session A4 (~day 4) | short | It is code-only C#, no XAML (D-007). `src/Waymark.Pos/TillWindow.cs` is the whole worked example — 1–2 evenings reading it, then A4 is the first real screen |
| **React + Tailwind + TS** | Block E (~day 21) | **4 weeks** | Learn during blocks B–D. Then **session E0 is the exercise**: rebuild the hop-8 Board page and diff it against `waymark-admin/src/Board.tsx`. Small, working, and the answer can be checked |

TypeScript is needed, not optional — the Admin contracts are typed and `tsconfig` is strict.

---

## 6. The backlog — every session, and what it expands

**H** = Hakim writes the core · **C** = Claude writes · **S/M/L** = 1 / 1–2 / 2–3 sessions.

### Block A — The floor under everything (5 sessions)

Discounts, overrides and voids all need PIN and permissions; checkout needs the TVA rule.

| # | Work | Expands (0.5) | Who | Size |
| :-- | :---- | :---- | :--: | :--: |
| A1 | O-24 resolved; promotional prices in the lookup | `Persistence/Catalogue/ProductLookup.cs` (D-066) | **H** rule, C query | S |
| A2 | Sessions, staff PIN, permissions | `Domain/Engine/CardAudience.cs` (D-074) | **H** | M |
| A3 | `reason_codes` wired as data (the generator already seeds them) | `Domain/Reference/ReasonCode.cs` | C | S |
| A4 | Till shell rebuilt to **G1** | `Pos/TillWindow.cs`, `App.cs` (D-068) | C | M |
| A5 | Sign-in: PIN verification, the till login session, sign-out and switching cashier. Added 23/09; built 24/09 (D-083) | `StaffPin` (D-077), `App.cs`'s `--staff=` | **H** verifier and session rule, C screen | M |

### Block B — Checkout depth (10 sessions) — the largest block

| # | Work | Expands (0.5) | Who | Size |
| :-- | :---- | :---- | :--: | :--: |
| B1 | Search by name, PLU, stock lookup | `TillSession.Handle`, `ProductLookup` | C | M |
| B2 | Quantity edit, line removal, park and resume | `Pos/Checkout/Cart.cs` | C | M |
| B3 | Weighted items, embedded weight/price barcodes, scale | `KeyboardWedgeScanner` (D-063), `Quantity` (D-036) | **H** | M |
| B4 | Line and transaction discounts with a reason | **`SaleArithmetic.Line`'s `discount` parameter — already there, never used** | **H** | M |
| B5 | Price override behind a manager PIN | A2 | **H** | S |
| B6 | Split tender: cash with change, card, wallet | `CompleteSale`'s single cash payment, `CashTender` (D-034) | **H** | M |
| B7 | Customer by phone; on-account and the credit limit | `receivable_movements` (D-055) — **schema exists, never exercised** | **H** | M |
| B8 | Line and transaction voids; no-sale, audited | `CompleteSale` atomicity (D-070) | **H** | M |
| B9 | Refunds linked to the original; store credit | `SaleArithmetic`, `BatchAllocation` (D-070) | **H** | L |
| B10 | Paid-in and paid-out; clock in and out | `CashSession` auto-open (D-070, provisional) | C | S |

### Block C — Shift (3 sessions)

| # | Work | Expands (0.5) | Who | Size |
| :-- | :---- | :---- | :--: | :--: |
| C1 | Float count, counted close, variance — **replaces D-070's auto-opened session** | `CompleteSale.CashSession` | **H** | M |
| C2 | X-report and Z-report, reconciling to counted cash | `rounding_variance` (D-034), `cash_movements` | **H** arithmetic, C rendering | M |
| C3 | Handover | C1 | C | S |

### Block D — Receipts and hardware (3 sessions)

| # | Work | Expands (0.5) | Who | Size |
| :-- | :---- | :---- | :--: | :--: |
| D1 | Receipt content, French ASCII; recomputes from its own row | `Transaction.RoundingPolicy` (D-053) | C | M |
| D2 | Real ESC/POS printing and the drawer | the fake printer (Phase 0, D-059) | C drafts, **H verifies** against the command reference | M |
| D3 | Reprint, audited | D1 | C | S |

### Block E — Catalogue, first Admin batch (4 sessions) — **G2 first**

| # | Work | Expands (0.5) | Who | Size |
| :-- | :---- | :---- | :--: | :--: |
| E0 | React ramp: rebuild the Board page, diff against the original | `waymark-admin/src/Board.tsx` | **H** (exercise) | S |
| E1 | Product and variant CRUD, categories | `RecommendationWire`, `DecisionRequest` | C | L |
| E2 | Bulk price update, archive | `prices`, price-in-force (D-066) | C | M |
| E3 | CSV import: validation and a dry-run preview | the generator's catalogue loader | C | L |

### Block F — Stock (4 sessions)

| # | Work | Expands (0.5) | Who | Size |
| :-- | :---- | :---- | :--: | :--: |
| F1 | Receive against a PO, creating batches and inventory | `BatchAllocation` (D-070), the generator's `SupplyChain.cs` | C | M |
| F2 | Adjustments with a reason | `StockMovement` staging in `CompleteSale` | C | M |
| F3 | Physical and cycle counts | `stock_counts`, the generator's `StockCounter.cs` | C | M |
| F4 | Stock views; manual near-expiry flag | `NearExpiry` (D-073) | C | M |

### Block G — Customers and compliance (7 sessions) — **G3 first; almost entirely Hakim's**

| # | Work | Expands (0.5 / Phase 0) | Who | Size |
| :-- | :---- | :---- | :--: | :--: |
| G1 | Customer CRUD, **no address field** | `customers`, the DPIA | **H** rules, C screens | M |
| G2 | The Article 32 notice, versioned, as data | `notice_versions` | **H** | S |
| G3 | **Two consents**, append-only `consent_events`, withdrawal as a new event | D-045 | **H** | L |
| G4 | `objection_flag` checked before any customer-directed output | D-045, CLAUDE.md §4 | **H** | M |
| G5 | Loyalty, history, tier override, customer discount, credit balance | B7 | **H** rules, C screens | M |
| G6 | Rights tooling + `data_subject_requests`, **blocked-with-reason**, the 10-day clock | `erasure_ledger` (D-060) | **H** | L |
| G7 | `processing_log` at every access site | `IProcessingLog` (D-045, D-061) — and **D-074's deliberate omission now becomes real**, because a customer card names a subject | **H** | M |

### Block H — Staff and store (2 sessions)

| # | Work | Expands (0.5) | Who | Size |
| :-- | :---- | :---- | :--: | :--: |
| H1 | Staff CRUD, roles, PIN, clock, basic scheduling, deactivation | A2 | C | M |
| H2 | Store profile, terminals, roles and permissions. Single store | `stores`, D-071's write guard | C | M |

### Block I — Platform (5 sessions)

| # | Work | Expands (0.5) | Who | Size |
| :-- | :---- | :---- | :--: | :--: |
| I1 | Local Admin **served by StoreServer over the LAN**, not a Vite dev server | hop 8's Vite proxy | C | M |
| I2 | Offline Level 1 complete; the **Level-2 cache** | `StoreServerClient`'s `ServerUnavailable` (D-068); the cache (D-013) **never built** | C | L |
| I3 | Nightly backup to a second device, **keys included**; one-click restore | `DatabaseKeyStore`, `KeysDirectoryAccess` (D-057) | **H** key custody, C the job | L |
| I4 | The recovery-code screen | D-042 | **H** | M |
| I5 | The evaluator **nightly**; retire a card whose batch is gone; **carry out** an accepted markdown or write-off | D-073, and D-074's "applies nothing" | **H** the apply, C the schedule | M |

### Block J — The done-when (4 sessions)

| # | Work | Who | Size |
| :-- | :---- | :--: | :--: |
| J1 | The scripted day: float, 30+ sales incl. weighted, split, discount, override, void, no-sale, refund; close; **Z reconciles to counted cash** | both | L |
| J2 | Consent → access → rectification → erasure, **erasure blocked while a credit balance exists** | **H** | M |
| J3 | Backup, restore onto a clean machine, receipt on real hardware, drawer opens | both | M |
| J4 | `recaps/phase-1.md`, decisions to titles, CLAUDE.md, close the phase | C | S |

---

## 7. Risks, named now

| Risk | Why it is real | What we do |
| :---- | :---- | :---- |
| **Block G is underestimated** | Seven sessions for consent, objection, rights and the processing log, all Hakim's, all legally load-bearing. It is the block most likely to double | Start it earlier than comfort suggests; its screens can be plain, its rules cannot be rushed |
| **The frontend is the time sink** | The Build Plan says so outright | Admin plain, batched, and cuttable. POS function never cut |
| **Writing more is slower per feature** | That is the trade being made deliberately | Tests-first handover; a piece sized to 4 h, never a whole block |
| **Hardware may not exist yet** | Real ESC/POS, a drawer and a scale are needed for D2, B3 and J3. Scale protocols are an open item pending the hardware survey | Build against the fakes; flag B3 and D2 as blocked-on-hardware as soon as we know |
| **O-24 blocks checkout** | A1 cannot start without it | Decided in the transition session, before Phase 1 opens |
| **23–46 days is a wide range** | 184 h is the honest number; what varies is how often a second session happens, and whether Hakim's pieces land inside their 4 h | Re-forecast at the end of blocks B and G, not at the end |
| **Two sessions a day, seven days a week** | It is the pace that closed Phase 0.5, sustained for six weeks rather than three days | The second session of a day is optional by design. A block never depends on it |

---

## 8. Verification

**Per session:** the full suite green with 0 warnings in Debug and Release; the session's
risky rule proven by breaking the code (D-012); the generator still loads; the previous
demo still runs; one commit.

**Per block:** the block's own walkthrough by hand — a sale with every feature of block B, a
shift opened and closed for C, a receipt printed for D, a catalogue imported for E.

**Phase 1 is done when** the Build Plan's four conditions hold, and they are J1–J3:
a simulated day that reconciles; a customer created, consented, and exercising access,
rectification and erasure with erasure blocked by a credit balance; a backup restored onto a
clean machine that reopens the shop; a receipt on real hardware with the drawer opening.
