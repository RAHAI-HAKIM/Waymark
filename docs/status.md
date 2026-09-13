# Status

Where the work stands, what is wrong, and what comes next. Rewrite this file as work
lands; it is the only document that is allowed to go stale in a week. Last pass:
**13/09/2026**, a full review of the repository before closing Phase 0.

---

## 1. Snapshot

| | |
| :---- | :---- |
| Build | `dotnet build src/Waymark.sln`, 13 projects, **0 warnings** |
| Tests | **372 passing**: Domain 129 · Integration 172 · Hardware 43 · Application 28 |
| Schema | 60 tables (all STRICT), 85 indexes, 14 append-only triggers, 4 migrations: `InitialSchema`, `AddRoundingVariance`, `ProcessingRegisterAndRecommendationType`, `AddRoundingPolicies` |
| Uncommitted | W11 (hardware project, tests, solution entry), including the EAN-13 check digit and injected drawer clock |

## 2. Phase 0 work items

| | Item | State | Decision |
| :---- | :---- | :---- | :---- |
| W1 | `Currency`, `Money`, rounding, allocation, TVA, cash tender | Done | D-031–D-035, D-037 |
| W2 | `Quantity`, `QuantityDelta`, `UnitPrecision` | Done | D-036 |
| W3 | `IIdGenerator`, `UlidGenerator`, `SeededIdGenerator` | Done; see F-3 before W10 | D-038 |
| W4 | `rounding_variance`; `rounding_policy` on stores and transactions | Done (the policies landed later, D-053) | D-032, D-034, D-053 |
| W5 | Money wired into the model | Done; Quantity stays `long` | D-041 |
| W6 | Domain package allowlist (IL and deps file) | Done | D-038, D-050 |
| W7 | `Waymark.Pseudonymisation`, tenant key, exclusion test | Done; the database key is deferred (F-1) | D-039, D-042, D-051 |
| W8 | `Waymark.Contracts` | Done | D-044, D-049 |
| W9 | Command executor, `processing_log` writer | Done | D-045, D-050 |
| W11 | Fake hardware | Done, uncommitted | D-052 |
| **W10** | **Synthetic store generator** | **Next. Unblocked** | D-046 |

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
| F-3 | Medium | **`SeededIdGenerator` ignores the simulated clock.** It adds 1 ms per id from `clockStart`, so a year of generated rows gets ULID timestamps packed into the first minutes | `SeededIdGenerator` | **Fix** at W10 step 3, as noted in D-046 |
| F-6 | Low | **The scanner's custom terminator is broken.** A terminator other than CR/LF is appended to the buffer and never ends a scan. Separately, `Accept` returns false for a scan's *digits*, so they still reach the text box; only Enter is swallowed, which is less than the doc comment claims. A CRLF scanner leaks the `\n` | `KeyboardWedgeScanner` | **Deferred to Phase 0.5**, fix before the POS cart |
| F-14 | Low | **Constraint names in `AddRoundingPolicies` disagree with the model.** The model names the CHECK on *both* tables `ck_store_rounding_policy` (a copy-paste on `transactions`, against the `ck_<table>_<column>` convention). The migration's `Up()` says `ck_transactions_…` and `ck_stores_…`, and its `Down()` says `ck_store_…` on both. Harmless on SQLite, where the rebuild takes names from the model, but misleading, and it will surface as a diff the next time either table is rebuilt | `TransactionConfiguration`, `StoreConfiguration`, the migration | **Decide**: rename in the model at the next migration that touches either table (a rename alone is another rebuild), or regenerate `AddRoundingPolicies` now, while no store holds data |
| F-15 | Low | **One unexplained integration failure.** On 14/09 a full-solution `dotnet test`, run straight after a build, failed one integration test, and the name was not captured. Ten further full runs and three isolated runs were all green. Possibly a fixture racing on a shared temp path | `Waymark.Integration.Tests` | **Watch**: if it recurs, capture the test name (`--logger "console;verbosity=detailed"`) before anything else |

---

## 5. Next: W10, the synthetic store generator

Spec: D-046. Hakim writes and reviews the parameter file and output distributions; Claude
writes the generator.

**Before starting:** F-3 (it hits the generator directly). Every generated sale copies the
store's `rounding_policy` into the transaction (D-053), and the determinism test compares a
canonical dump, never file bytes (D-046), so it survives the database key landing later.

Suggested build order, each step ending in a green test:

1. **Where it lives.** A console project `tools/Waymark.Generator` (or `src/`), referencing
   Application and Persistence. *A new project is an architecture change: ask first.*
2. Config and catalogue loading, with validation. Ship the `grocery-dz` catalogue.
3. `Draw(stream, coords…)` and a simulated `TimeProvider`; the seeded id generator on that
   clock (F-3).
4. The day loop, with reference data only (stores, staff, terminals, units, categories,
   variants, suppliers, prices). Loads through `MigrateAndApplyTriggers()`.
5. Demand, then baskets, then sales through the Money/TVA/cash-tender types, then stock
   movements and FIFO batches.
6. `NaiveShopkeeperPolicy`, then POs, receipts, expiry write-offs, stockouts.
7. The mess: returns, voids, discounts with reasons, cash sessions, stock counts.
8. Connectivity: outbox, inbox, sync state.
9. Outputs: latent-demand CSV, manifest. Invariant tests (D-046 review list). Determinism
   test.
10. The **distribution review**, by Hakim: hourly and weekday shape, basket sizes,
    Ramadan, stockout and spoilage rates against the latent CSV.

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
