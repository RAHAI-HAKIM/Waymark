# Status

Where the work stands, what is wrong, and what comes next. Rewrite this file as work
lands; it is the only document that is allowed to go stale in a week. Last pass:
**15/09/2026**, Phase 0's build closed (W10 S10).

---

## 1. Snapshot

| | |
| :---- | :---- |
| Phase | **0, Foundation: build complete.** Next: Hakim's final test and analysis, then the findings and open questions below, then Phase 0.5 |
| Build | `dotnet build src/Waymark.sln`, 15 projects, **0 warnings** |
| Tests | **592 passing**: Domain 129 · Integration 177 · Generator 215 · Hardware 43 · Application 28 |
| Schema | 60 tables (all STRICT), 85 indexes, 14 append-only triggers, 4 migrations: `InitialSchema`, `AddRoundingVariance`, `ProcessingRegisterAndRecommendationType`, `AddRoundingPolicies` |
| Recap | `docs/recaps/phase-0.md`: everything Phase 0 built, decided and found. Read it only when a question reaches back into Phase 0 |

## 2. Phase 0 work items

All done. Outcomes and the W10 step history are in the recap.

| | Item | Decision |
| :---- | :---- | :---- |
| W1 | `Currency`, `Money`, rounding, allocation, TVA, cash tender | D-031–D-035, D-037 |
| W2 | `Quantity`, `QuantityDelta`, `UnitPrecision` | D-036 |
| W3 | `IIdGenerator`, `UlidGenerator`, `SeededIdGenerator` on a `TimeProvider` | D-038 |
| W4 | `rounding_variance`; `rounding_policy` on stores and transactions | D-032, D-034, D-053 |
| W5 | Money wired into the model; Quantity stays `long` | D-041 |
| W6 | Domain package allowlist (IL and deps file) | D-038, D-050 |
| W7 | `Waymark.Pseudonymisation`, tenant key, exclusion test; the database key deferred (F-1) | D-039, D-042, D-051 |
| W8 | `Waymark.Contracts` | D-044, D-049 |
| W9 | Command executor, `processing_log` writer | D-045, D-050 |
| W10 | Synthetic store generator, S0–S10; guide at `src/Waymark.Generator/README.md` | D-046, D-054 |
| W11 | Fake hardware | D-052 |

## 3. Phase 0 exit criteria (Build Plan)

| Criterion | State |
| :---- | :---- |
| Solution builds; architecture tests pass and fail when a forbidden reference is added | ✅ (D-012), including POS→DB (`Pos_cannot_reach_the_store_database`) and the generator's isolation |
| Migration creates the store database from empty, **encrypted**, every trigger in place | ⚠️ Triggers yes; **encrypted no**. Deferred to the post-Phase 0 revision (F-1, O-20) |
| The generator produces a plausible year of data that loads | ✅ Full grocery year: 392,896 rows, `report.md` reviewed by Hakim (15/09), StoreServer opens it and answers `/health` |
| Value-object tests: both policies, exact allocation, TVA from TTC, currency mismatch, cash step, level/change algebra | ✅ |
| The tenant key cannot reach a backup payload or the outbox, proved by a failing-when-removed test | ✅ for the DB file, WAL and outbox. No backup job exists yet to test |

---

## 4. Findings register

Each item is either **fix** (Claude can do it, no decision needed) or **decide** (Hakim,
usually an `O-` entry). Close an item by deleting its row.

| # | Sev. | Finding | Where | Action |
| :---- | :---- | :---- | :---- | :---- |
| F-1 | **High** | **`waymark-store.db` is created in plaintext.** D-042's database key has no code: no key file, no `PRAGMA key`, and StoreServer's connection string has no key. That misses the Phase 0 "encrypted" criterion, and DPIA §5.4 says the store DB is encrypted at rest. SQLCipher's random page salt also rules out byte-identical files, which the generator already avoids by comparing a canonical dump (D-046) | `Waymark.StoreServer/Program.cs`, `UseWaymarkSqlite` | **Deferred** to the post-Phase 0 revision (O-20) |
| F-6 | Low | **The scanner's custom terminator is broken.** A terminator other than CR/LF is appended to the buffer and never ends a scan. Separately, `Accept` returns false for a scan's *digits*, so they still reach the text box; only Enter is swallowed, which is less than the doc comment claims. A CRLF scanner leaks the `\n` | `KeyboardWedgeScanner` | **Deferred to Phase 0.5**, fix before the POS cart |
| F-14 | Low | **Constraint names in `AddRoundingPolicies` disagree with the model.** The model names the CHECK on *both* tables `ck_store_rounding_policy` (a copy-paste on `transactions`, against the `ck_<table>_<column>` convention). The migration's `Up()` says `ck_transactions_…` and `ck_stores_…`, and its `Down()` says `ck_store_…` on both. Harmless on SQLite, where the rebuild takes names from the model, but misleading, and it will surface as a diff the next time either table is rebuilt | `TransactionConfiguration`, `StoreConfiguration`, the migration | **Decide**: rename in the model at the next migration that touches either table (a rename alone is another rebuild), or regenerate `AddRoundingPolicies` now, while no store holds data |
| F-15 | Low | **One unexplained integration failure.** On 14/09 a full-solution `dotnet test`, run straight after a build, failed one integration test, and the name was not captured. Every run since has been green. Possibly a fixture racing on a shared temp path | `Waymark.Integration.Tests` | **Watch**: if it recurs, capture the test name (`--logger "console;verbosity=detailed"`) before anything else |
| F-16 | Medium | **Nothing records an on-account debt being settled.** `on_account` is a payment method, but `credit_movements` is store credit (issue/redeem, balance never negative) and no table holds a customer's tab or its repayment. The generator writes on-account sales only, so every tab grows for ever | Schema: `transaction_payments`, `credit_movements` | I created a receivable ledger `receivable_movements` with it's trigger, 2b reviwed b4 solving other flaws |
| F-17 | Low | **`returns` has no column for its refund transaction.** A return points at the original line; the refund transaction points at the original transaction; nothing joins the return to its refund except moment, cashier and batch | Schema: `returns` | The transaction-level link is already reachable — transactions.original_transaction_id joins refund to original. What you we couldn't do is match a return row to the refund line that paid for it: a refund containing the same variant twice, at different prices or from different batches, can't be reconciled by variant matching, a `"refund_transaction_item_id"` was added to `returns`. 2b reviwed b4 solving other flaws |

## 5. Open questions

The full text is in `docs/decisions.md`, "Open — waiting on Hakim".

| # | Question | Disposition |
| :---- | :---- | :---- |
| O-18 | Who sets the ACL on `%ProgramData%\Waymark\keys`, and where does the install id live? LocalMachine DPAPI is unwrappable by any local process, so the ACL is the real control, and nothing sets it | Deferred to the post-Phase 0 revision. The DPIA describes the control as in place |
| O-20 | When does the database key land (F-1)? | Deferred to the post-Phase 0 revision. The generator's determinism is already a logical dump, so encryption will not break it |

---

## 6. Next

1. **Hakim: the final test and analysis of Phase 0.**
2. **The post-Phase 0 revision:**
   - F-1/O-20 (database key);
   - O-18 (keys ACL, install id);
   - F-16 and F-17 (one session each);
   - F-14 (decide).
   F-6 waits for the POS cart in Phase 0.5, and F-15 is watched.
3. **Phase 0.5, the walking skeleton.**

The generator is ready for use. Run it with:

```bash
dotnet run --project src/Waymark.Generator -- --config src/Waymark.Generator/inputs/configs/grocery-dz.json --out artifacts/generated/seed-42
```

The guide is `src/Waymark.Generator/README.md`.

## 7. After that: Phase 0.5, the walking skeleton

One thin cut (Build Plan): scan → cart → complete sale → transaction, stock movement and
tier 1 → pseudonymise → outbox → stub cloud reads it → expiry evaluator flags a batch →
envelope → Integration Layer role check → card in Local Admin → accept → decision written
and logged.

| Hop | Exists | Missing |
| :---- | :---- | :---- |
| Scan, cart | `KeyboardWedgeScanner` (F-6); a generated store as product data | POS window, HTTP client, product lookup endpoint |
| Complete sale | Money, TVA, cash tender, Quantity, executor, schema; the generator's `SaleWriter` shows the rows a sale must write | `CompleteSale` handler; StoreServer DI for UoW, log, executor, `TimeProvider`; API endpoint |
| Pseudonymise → outbox | `IPseudonymiser`, `TenantKeyStore`, `AnonymousBasketRecord`, `OutboundEnvelope`; generated outboxes with real payloads | Install id and key paths wired in the host (O-18); the emit step; outbox sequence assignment |
| Tier 2 | nothing | The local DuckDB writer (D-043). *Stub or real for 0.5? Decide* |
| Stub cloud | Generated outboxes under `offline_stretch` to drain | A local process that reads the outbox. *Language and shape: decide* |
| Expiry evaluator | Schema (batches, parameter registry), cold-start markdown path; generated batches with expiries | Store-side evaluator (compare only, CLAUDE.md §5); near-expiry windows per category, *Hakim* |
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
