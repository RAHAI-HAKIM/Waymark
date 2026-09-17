# Status

Where the work stands, what is wrong, and what comes next. Rewrite this file as work
lands; it is the only document that is allowed to go stale in a week. Last pass:
**17/09/2026**: **Phase 0 is complete**: the revision, the final test and Hakim's decisions
on its findings. Next is Phase 0.5.

---

## 1. Snapshot

| | |
| :---- | :---- |
| Phase | **0, Foundation: complete** (17/09/2026). Next: **0.5, the walking skeleton** |
| Build | `dotnet build src/Waymark.sln`, 15 projects, **0 warnings**, Debug and Release. No vulnerable package |
| Tests | **774 passing**: Domain 135 · Integration 326 · Generator 235 · Hardware 50 · Application 28 |
| Schema | 61 tables (all STRICT), 88 indexes, 29 triggers, 6 migrations: `InitialSchema`, `AddRoundingVariance`, `ProcessingRegisterAndRecommendationType`, `AddRoundingPolicies`, `AddReceivables`, `RenameRoundingPolicyChecks` |
| Encryption | `waymark-store.db` is SQLCipher-encrypted by StoreServer, which refuses a plaintext store and imports one instead (D-056). The generator's output is plaintext by design |
| Recap | `docs/recaps/phase-0.md`: everything Phase 0 built, decided and found. §8 covers the revision, the final test and how the phase closed. Read it only when a question reaches back into Phase 0 |

## 2. Phase 0, closed

All eleven work items are done (W1–W11). The revision fixed F-1, F-16, F-17 and O-18/O-20
(D-055–D-057). The final test fixed four flaws:
- the REPLACE bypass (D-058);
- the keys allowlist (D-057);
- printer command injection (D-059);
- a generator drawer bug.

Hakim decided the other four:
- F-14: the constraints renamed;
- F-18: the erasure ledger's facts fixed and its outcome forward only (D-060);
- F-19: child rows filtered through their parent (D-062);
- F-20: an operation about one person names them (D-061).

Every exit criterion is met:

| Criterion | State |
| :---- | :---- |
| Solution builds; architecture tests pass and fail when a forbidden reference is added | ✅ (D-012), including POS→DB and the generator's isolation |
| Migration creates the store database from empty, **encrypted**, every trigger in place | ✅ (D-056): `DatabaseEncryptionTests`, and `StoreServerStartupTests` on the real process |
| The generator produces a plausible year of data that loads | ✅ Full grocery year, `report.md` reviewed by Hakim, `tools/verify-store` 39/39; StoreServer imports it, serves it and reopens it after a restart |
| Value-object tests: both policies, exact allocation, TVA from TTC, currency mismatch, cash step, level/change algebra | ✅, and against an independent reference (`MoneyPropertyTests`) |
| The tenant key cannot reach a backup payload or the outbox, proved by a failing-when-removed test | ✅ for the DB file, WAL and outbox; the keys directory's ACL is enforced at start (D-057). No backup job exists yet to test |

---

## 3. Findings register

Each item is either **fix** (Claude can do it, no decision needed) or **decide** (Hakim,
usually an `O-` entry). Close an item by deleting its row.

| # | Sev. | Finding | Where | Action |
| :---- | :---- | :---- | :---- | :---- |
| F-6 | Low | **The scanner's custom terminator is broken.** A terminator other than CR/LF is appended to the buffer and never ends a scan. Separately, `Accept` returns false for a scan's *digits*, so they still reach the text box; only Enter is swallowed, which is less than the doc comment claims. A CRLF scanner leaks the `\n` | `KeyboardWedgeScanner` | **Phase 0.5**, fix before the POS cart |
| F-15 | Low | **One unexplained integration failure.** On 14/09 a full-solution `dotnet test`, run straight after a build, failed one integration test, and the name was not captured. Every run since has been green | `Waymark.Integration.Tests` | **Watch**: if it recurs, capture the test name (`--logger "console;verbosity=detailed"`) before anything else |
| F-21 | Low | **Writes are not checked against the current store.** D-062 filters reads; a context scoped to store A can still insert a row for store B. Hakim chose the read filter (17/09) | `WaymarkDbContext` | **Watch**: revisit when the first handler writes store-scoped rows (Phase 0.5) |

## 4. Open questions

The full text is in `docs/decisions.md`, "Open — waiting on Hakim".

| # | Question | Disposition |
| :---- | :---- | :---- |
| O-23 | Is statistics tier 2 (local DuckDB) encrypted at rest, and with what key? | Raised by F-1: DuckDB's encryption is not SQLCipher. Decide with the tier-2 writer (Phase 0.5); the DPIA states the gap meanwhile |

---

## 5. Tests carried into Phase 0.5

The final test's remaining ideas. None blocks the skeleton; take them as the code they touch
is next changed.

1. **F-15 hunt**: the whole suite five times in a row straight after a clean build, with
   `--logger "console;verbosity=detailed"`, and once in Release.
2. **Key custody under pressure**:
   - two processes creating the tenant key and the database key at once; the loser must
     fail, never overwrite;
   - a truncated or zero-length key file;
   - a blob copied from another machine (needs a second Windows machine or a VM).
3. **Executor edges**:
   - cancellation between staging and commit;
   - a handler that throws after staging a log entry, then a second command on the same scope;
   - `SaveChanges` failing on a CHECK, with the context clean afterwards.
4. **Converters**:
   - a timestamp with a non-UTC offset;
   - two rows in one second, which are stored to the second: do they still order by id?
   - `DateOnly` at the year's edges;
   - a nullable `Money` column holding zero versus null.
5. **Contracts against the new schema**: whether any contract should carry `on_account`
   refunds or the receivables vocabulary, and whether the basket's payment classes still
   match `transaction_payments` (D-043, D-049).
6. **Architecture**:
   - only the host names `DatabaseKeyStore`, `DpapiKeyProtector` or `KeysDirectoryAccess`;
   - only Persistence builds a `SqliteConnection` for the store database.
7. **Scanner (F-6)**: pin the known failures with tests before fixing them.

A generated store is checked with `python tools/verify-store/verify_store.py <run>`.

---

## 6. Next: Phase 0.5, the walking skeleton

One thin cut (Build Plan): scan → cart → complete sale → transaction, stock movement and
tier 1 → pseudonymise → outbox → stub cloud reads it → expiry evaluator flags a batch →
envelope → Integration Layer role check → card in Local Admin → accept → decision written
and logged.

| Hop | Exists | Missing |
| :---- | :---- | :---- |
| Scan, cart | `KeyboardWedgeScanner` (F-6); a generated store as product data | POS window, HTTP client, product lookup endpoint |
| Complete sale | Money, TVA, cash tender, Quantity, executor, schema; the generator's `SaleWriter` shows the rows a sale must write, including the on-account charge (D-055) | `CompleteSale` handler; the credit-limit check before an on-account payment; StoreServer DI for UoW, log, executor, `TimeProvider`; API endpoint |
| Pseudonymise → outbox | `IPseudonymiser`, `TenantKeyStore`, `AnonymousBasketRecord`, `OutboundEnvelope`; generated outboxes with real payloads; the keys directory and its ACL in StoreServer (D-057) | `TenantKeyStore` wired into StoreServer's DI; the emit step; outbox sequence assignment |
| Tier 2 | nothing | The local DuckDB writer (D-043). *Stub or real for 0.5, and encrypted how (O-23)? Decide* |
| Stub cloud | Generated outboxes under `offline_stretch` to drain | A local process that reads the outbox. *Language and shape: decide* |
| Expiry evaluator | Schema (batches, parameter registry), cold-start markdown path; generated batches with expiries | Store-side evaluator (compare only, CLAUDE.md §5); near-expiry windows per category, *Hakim* |
| Envelope, Integration Layer | Contracts, recommendation tables | Role gating, pending queue, pseudonym resolution with `objection_flag` and a log write that names its subject (D-061) |
| Local Admin card, accept | Brand rules | `waymark-admin` scaffold (React, Vite, Tailwind); `AcceptRecommendation` handler |

**Decisions Phase 0.5 needs from Hakim** (CLAUDE.md §7): the sale's outbox payload at emit
(D-043 fields, spend bands); stub cloud and tier 2 shape; near-expiry windows; the
minimal role model for the skeleton; O-23 with the tier-2 shape.

The generator is ready for use:

```bash
dotnet run --project src/Waymark.Generator -- --config src/Waymark.Generator/inputs/configs/grocery-dz.json --out artifacts/generated/seed-42
```

To open a generated store in StoreServer, import it (D-056):

```bash
dotnet run --project src/Waymark.StoreServer -- --Waymark:Storage:DataDirectory=artifacts/server/data --Waymark:Storage:KeysDirectory=artifacts/server/keys --Waymark:Storage:ImportPlaintextFrom=artifacts/generated/seed-42/waymark-store.db --Waymark:Store:StoreId=<store id from manifest.json>
```

The guide is `src/Waymark.Generator/README.md`.

---

## Division of labour

| | Hakim | Claude |
| :---- | :---- | :---- |
| Arithmetic, schema, privacy boundary, sync rules, engine methods | Decides, reviews line by line | Drafts, tests first, proves each test fails |
| Generator | Writes the parameter spec, reviews distributions | Writes the code |
| Migrations | Reads the generated `Up()` before it runs | Generates, verifies, regenerates `schema_current.sql` |
| UI, CRUD, glue, scaffolding | Reviews the result | Writes |
