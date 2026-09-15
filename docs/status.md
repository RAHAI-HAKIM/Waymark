# Status

Where the work stands, what is wrong, and what comes next. Rewrite this file as work
lands; it is the only document that is allowed to go stale in a week. Last pass:
**13/09/2026**, a full review of the repository before closing Phase 0.

---

## 1. Snapshot

| | |
| :---- | :---- |
| Build | `dotnet build src/Waymark.sln`, 15 projects, **0 warnings** |
| Tests | **592 passing**: Domain 129 · Integration 177 · Generator 215 · Hardware 43 · Application 28 |
| Schema | 60 tables (all STRICT), 85 indexes, 14 append-only triggers, 4 migrations: `InitialSchema`, `AddRoundingVariance`, `ProcessingRegisterAndRecommendationType`, `AddRoundingPolicies` |
| Uncommitted | W10 S0–S5 (generator, its tests, inputs, `tools/enrich-catalogue`); W11 was committed in `0912232` |

## 2. Phase 0 work items

| | Item | State | Decision |
| :---- | :---- | :---- | :---- |
| W1 | `Currency`, `Money`, rounding, allocation, TVA, cash tender | Done | D-031–D-035, D-037 |
| W2 | `Quantity`, `QuantityDelta`, `UnitPrecision` | Done | D-036 |
| W3 | `IIdGenerator`, `UlidGenerator`, `SeededIdGenerator` | Done; seeded ids follow a clock (F-3 fixed) | D-038 |
| W4 | `rounding_variance`; `rounding_policy` on stores and transactions | Done (the policies landed later, D-053) | D-032, D-034, D-053 |
| W5 | Money wired into the model | Done; Quantity stays `long` | D-041 |
| W6 | Domain package allowlist (IL and deps file) | Done | D-038, D-050 |
| W7 | `Waymark.Pseudonymisation`, tenant key, exclusion test | Done; the database key is deferred (F-1) | D-039, D-042, D-051 |
| W8 | `Waymark.Contracts` | Done | D-044, D-049 |
| W9 | Command executor, `processing_log` writer | Done | D-045, D-050 |
| W11 | Fake hardware | Done, uncommitted | D-052 |
| **W10** | **Synthetic store generator** | **In progress: S0–S9 done**. Next S10 with Hakim | D-046, D-054 |

## 3. Phase 0 exit criteria (Build Plan)

| Criterion | State |
| :---- | :---- |
| Solution builds; architecture tests pass and fail when a forbidden reference is added | ✅ (D-012), including POS→DB (`Pos_cannot_reach_the_store_database`) |
| Migration creates the store database from empty, **encrypted**, every trigger in place | ⚠️ Triggers yes; **encrypted no**. Deferred to the post-Phase 0 revision (F-1, O-20) |
| The generator produces a plausible year of data that loads | ⏳ W10 |
| Value-object tests: both policies, exact allocation, TVA from TTC, currency mismatch, cash step, level/change algebra | ✅ |
| The tenant key cannot reach a backup payload or the outbox, proved by a failing-when-removed test | ✅ for the DB file, WAL and outbox. No backup job exists yet to test |

---

## 4. Findings register

From the 13/09 review. Each item is either **fix** (Claude can do it, no decision needed)
or **decide** (Hakim, usually an `O-` entry). Close an item by deleting its row.

| # | Sev. | Finding | Where | Action |
| :---- | :---- | :---- | :---- | :---- |
| F-1 | **High** | **`waymark-store.db` is created in plaintext.** D-042's database key has no code: no key file, no `PRAGMA key`, and StoreServer's connection string has no key. That misses the Phase 0 "encrypted" criterion, and DPIA §5.4 says the store DB is encrypted at rest. SQLCipher's random page salt also conflicts with D-046's "byte-identical runs" | `Waymark.StoreServer/Program.cs`, `UseWaymarkSqlite` | **Deferred** to the post-Phase 0 revision (O-20). Does not block W10: its determinism test compares a canonical dump (D-046) |
| F-6 | Low | **The scanner's custom terminator is broken.** A terminator other than CR/LF is appended to the buffer and never ends a scan. Separately, `Accept` returns false for a scan's *digits*, so they still reach the text box; only Enter is swallowed, which is less than the doc comment claims. A CRLF scanner leaks the `\n` | `KeyboardWedgeScanner` | **Deferred to Phase 0.5**, fix before the POS cart |
| F-14 | Low | **Constraint names in `AddRoundingPolicies` disagree with the model.** The model names the CHECK on *both* tables `ck_store_rounding_policy` (a copy-paste on `transactions`, against the `ck_<table>_<column>` convention). The migration's `Up()` says `ck_transactions_…` and `ck_stores_…`, and its `Down()` says `ck_store_…` on both. Harmless on SQLite, where the rebuild takes names from the model, but misleading, and it will surface as a diff the next time either table is rebuilt | `TransactionConfiguration`, `StoreConfiguration`, the migration | **Decide**: rename in the model at the next migration that touches either table (a rename alone is another rebuild), or regenerate `AddRoundingPolicies` now, while no store holds data |
| F-16 | Medium | **Nothing records an on-account debt being settled.** `on_account` is a payment method, but `credit_movements` is store credit (issue/redeem, balance never negative) and no table holds a customer's tab or its repayment. The generator writes on-account sales only, so every tab grows for ever | Schema: `transaction_payments`, `credit_movements` | **Deferred** to the post-Phase 0 revision, in its own session (Hakim, 15/09). Decide there: a receivables ledger, or signed `credit_movements` with a separate type. Does not block W10: the generator writes on-account sales only |
| F-17 | Low | **`returns` has no column for its refund transaction.** A return points at the original line; the refund transaction points at the original transaction; nothing joins the return to its refund except moment, cashier and batch | Schema: `returns` | **Deferred** to the post-Phase 0 revision, in its own session (Hakim, 15/09). Likely a nullable `refund_transaction_id` (an `ADD COLUMN`, no rebuild). Does not block W10: tests match a return to its refund by moment, cashier and batch |
| F-15 | Low | **One unexplained integration failure.** On 14/09 a full-solution `dotnet test`, run straight after a build, failed one integration test, and the name was not captured. Ten further full runs and three isolated runs were all green. Possibly a fixture racing on a shared temp path | `Waymark.Integration.Tests` | **Watch**: if it recurs, capture the test name (`--logger "console;verbosity=detailed"`) before anything else |

---

## 5. Now: W10, the synthetic store generator

Spec D-046; as built, D-054. Reviewed by Hakim every two or three steps.

| Step | What | State |
| :---- | :---- | :---- |
| S0 | `SeededIdGenerator` on a `TimeProvider` (F-3) | Done |
| S1 | Projects, solution entries, architecture rules, CLI | Done |
| S2 | Addressed randomness, distributions, simulated clock, calendar | Done |
| S3 | Catalogue enrichment, configuration, loading and validation | Done, reviewed |
| S4 | Commissioning: reference data, customers, opening stock, manifest | Done |
| S5 | Day loop and sales: demand, baskets, TVA, cash tender, FIFO, cash sessions, shifts, latent-demand CSV | Done, reviewed |
| S6 | `NaiveShopkeeperPolicy`, purchase orders, receipts, expiry write-offs, stockouts | Done, **awaiting Hakim's review** |
| S7 | Returns, voids, discounts, store credit, on-account, cash movements, stock counts | Done, **awaiting Hakim's review** |
| S8 | Connectivity: outbox, drain profiles, `sync_state` | Done, **awaiting Hakim's review** |
| S9 | Report, manifest completion, determinism, the full-year run | Done, **awaiting Hakim's review** |
| S10 | Hakim's distribution review of `report.md`; D-054 completed; Phase 0 exit ticked | Next, together |

S3–S4 review closed (Hakim, 14/09): no 0% VAT class for now; sensitive flags wait for the
phase that uses them; the catalogue is uniform (one selling unit) but adequate for what the
generator is for; Ramadan 1447 and Aïd al-Adha 2026 dates confirmed; objectors without
consent and legal basis `contract` confirmed.

S5 review closed (Hakim, 14/09):
- Baskets 1–50 units, P(<7) = P(>7) = 0.45 (read from "0.9", which cannot sum), P(7) = 0.10,
  mean 7.2.
- Attach share 0.18; cash 0.95, card 0.04, wallet 0.01; substitution and float kept.
- A quarter of variants priced off the 5 DZD step, so tender rounding happens.
- Ramadan Fridays open; the staff rota confirmed.

A full year (seed 42, always on): **about 70 s, 392,896 rows**, plus `latent-demand.csv`
(145,635 rows), `report.md` and `manifest.json`.
- 22,548 sales; 95.3% of wanted units sold, lost demand 2–6% a month and creeping up. The
  naive rule remembers stockout days as slow sales.
- 642 deliveries; spoilage about 0.9%, with an April spike from Ramadan over-ordering.
- 440 returns, 208 voids, 14,328 rounding-variance rows.
- 22,548 anonymous baskets emitted, all acknowledged.
- StoreServer starts on it and answers `/health`.

The S7 year took 32.8 s, while the same code timed at over 70 s on 15/09. A profile shows
`SaveChanges` is ~90% of the day loop and the drain costs milliseconds, so the swing is the
machine and its disk, not S8. Switching off EF's automatic change detection halved save time
in a 120-day test, with identical output.

**For Hakim to review from S6–S7** (the `supply` and `mess` sections of `grocery-dz.json`; D-054):
- The ordering rule: 14 days' memory, reorder at 4 days of pace, up to 12, slow movers under
  0.3 a day ignored until empty, ×1.5 from two weeks before Ramadan.
- Suppliers: up to 2 days late; 92% of lines in full, short lines never backordered.
- The mess:
  - discounts on 2% of lines (5–20%);
  - voids on 1% of baskets;
  - returns on 0.3% of lines, 40% restocked;
  - on-account 15% of a regular's baskets, scaled by the payday effects;
  - paid-outs about twice a week;
  - a 20,000 DZD drop threshold;
  - miscounts at 8% of closes, up to 300 DZD;
  - a weekly cycle count of one subcategory.
- **The basket change cut footfall to ~58 baskets a day** from ~150: demand (420 units a day)
  is unchanged, so larger baskets mean fewer of them. Raise `TARGET_UNITS_PER_DAY` if the
  shop should still see ~150 customers.

**For Hakim to review from S8–S9** (the `connectivity` section of `grocery-dz.json`; D-054):
- The drain: every 3 minutes while open and at close, 500 a batch, no backoff modelled.
  `flaky` is 70% of hours up; `offline_stretch` is 21 days from day 150.
- The basket payload: product lines, not variants; hour, not time; `mixed` for split
  tenders; refunds and voids not emitted; no customer period stream until spend bands
  exist (D-049).
- Read `report.md` from a full run: that review is S10.

**Verified (15/09).**
- Full solution: 592 tests passing, 0 warnings.
- Mutations:
  - the S6–S7 suite catches all 29 of its breaks;
  - the six S5 breaks whose code moved in S7 are caught again;
  - S8–S9: 17 of 17 caught.
- P16 (the dump ignoring its table exclusion) at first survived: the profiles' sync tables
  had all converged by day 60. It is now caught by comparing a run that ends offline with
  one that ends online.

Nothing is committed.

Run it:
`dotnet run --project src/Waymark.Generator -- --config src/Waymark.Generator/inputs/configs/grocery-dz.json --out <new directory>`

## 6. After that: Phase 0.5, the walking skeleton

One thin cut (Build Plan): scan → cart → complete sale → transaction, stock movement and
tier 1 → pseudonymise → outbox → stub cloud reads it → expiry evaluator flags a batch →
envelope → Integration Layer role check → card in Local Admin → accept → decision written
and logged.

| Hop | Exists | Missing |
| :---- | :---- | :---- |
| Scan, cart | `KeyboardWedgeScanner` (F-6) | POS window, HTTP client, product lookup endpoint |
| Complete sale | Money, TVA, cash tender, Quantity, executor, schema | `CompleteSale` handler; StoreServer DI for UoW, log, executor, `TimeProvider`; API endpoint |
| Pseudonymise → outbox | `IPseudonymiser`, `TenantKeyStore`, `AnonymousBasketRecord`, `OutboundEnvelope` | Install id and key paths wired in the host (O-18); the emit step; outbox sequence assignment |
| Tier 2 | nothing | The local DuckDB writer (D-043). *Stub or real for 0.5? Decide* |
| Stub cloud | nothing | A local process that reads the outbox. *Language and shape: decide* |
| Expiry evaluator | Schema (batches, parameter registry), cold-start markdown path | Store-side evaluator (compare only, CLAUDE.md §5); near-expiry windows per category, *Hakim* |
| Envelope, Integration Layer | Contracts, recommendation tables | Role gating, pending queue, pseudonym resolution with `objection_flag` and a log write |
| Local Admin card, accept | Brand rules | `waymark-admin` scaffold (React, Vite, Tailwind); `AcceptRecommendation` handler |

**Decisions Phase 0.5 needs from Hakim** (CLAUDE.md §7): the sale's outbox payload at emit
(D-043 fields, spend bands); stub cloud and tier 2 shape; near-expiry windows; the
minimal role model for the skeleton; O-18 enough to place the install id.

---

## Division of labour

| | Hakim | Claude |
| :---- | :---- | :---- |
| Arithmetic, schema, privacy boundary, sync rules, engine methods | Decides, reviews line by line | Drafts, tests first, proves each test fails |
| Generator | Writes the parameter spec, reviews distributions | Writes the code |
| Migrations | Reads the generated `Up()` before it runs | Generates, verifies, regenerates `schema_current.sql` |
| UI, CRUD, glue, scaffolding | Reviews the result | Writes |
