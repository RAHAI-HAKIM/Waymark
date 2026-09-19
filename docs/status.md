# Status

Where the work stands, what is wrong, and what comes next. Rewrite this file as work
lands; it is the only document that is allowed to go stale in a week. Last pass:
**18/09/2026**: Phase 0.5 opened; F-6 fixed (D-063). **Hop 1 (scan → cart) is built** on
branch `phase-0.5/hop-1`, uncommitted, waiting for Hakim's review, hand test and commit (§6.2).
Next is hop 2, complete sale.

---

## 1. Snapshot

| | |
| :---- | :---- |
| Phase | **0.5, the walking skeleton: in progress** (opened 18/09/2026). Phase 0 complete 17/09/2026 |
| Build | `dotnet build src/Waymark.sln`, 16 projects (the new one is `Waymark.Pos.Tests`), **0 warnings**, Debug and Release. No vulnerable package |
| Tests | **895 passing**: Domain 135 · Integration 372 · Generator 235 · Hardware 64 · Application 33 · Pos 56 |
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
| F-15 | Low | **One unexplained integration failure.** On 14/09 a full-solution `dotnet test`, run straight after a build, failed one integration test, and the name was not captured. Every run since has been green | `Waymark.Integration.Tests` | **Watch**: if it recurs, capture the test name (`--logger "console;verbosity=detailed"`) before anything else |
| F-21 | Low | **Writes are not checked against the current store.** D-062 filters reads; a context scoped to store A can still insert a row for store B. Hakim chose the read filter (17/09) | `WaymarkDbContext` | **Watch**: revisit when the first handler writes store-scoped rows (Phase 0.5) |

## 4. Open questions

The full text is in `docs/decisions.md`, "Open — waiting on Hakim".

| # | Question | Disposition |
| :---- | :---- | :---- |
| O-23 | *How* is statistics tier 2 (local DuckDB) encrypted, and with what key? *Whether* is settled: it is (D-065) | DuckDB's encryption is not SQLCipher. Decide with the real tier-2 writer (Phase 2); 0.5 stubs it. The DPIA states the gap meanwhile |
| O-24 | Which TVA rate applies when a product's categories disagree, or one has no rate? | The skeleton refuses both (D-066). Decide for Phase 1 checkout |

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

A generated store is checked with `python tools/verify-store/verify_store.py <run>`.

---

## 6. Next: Phase 0.5, the walking skeleton

One thin cut (Build Plan): scan → cart → complete sale → transaction, stock movement and
tier 1 → pseudonymise → outbox → stub cloud reads it → expiry evaluator flags a batch →
envelope → Integration Layer role check → card in Local Admin → accept → decision written
and logged.

**Decided on 18/09:** the scanner (D-063); a sale emits only the anonymous basket, and the
customer period record waits for Phase 2 (D-064); tier 2 is a stub and will be encrypted
(D-065, how: O-23); the stub cloud is deferred within the phase (hop 5).

| # | Hop | Exists | Missing (explained in §6.1) |
| :---- | :---- | :---- | :---- |
| 1 | Scan, cart | **Built** (D-063, D-066–D-068): the lookup, `GET /api/products/lookup`, the POS client, cart and window. Waiting for Hakim's review (§6.2) | Nothing |
| 2 | Complete sale | Money, TVA, cash tender, Quantity, executor, schema; the generator's `SaleWriter` writes the rows a sale must write | StoreServer DI; `CompleteSale` handler; sale endpoint; cash session to sell into |
| 3 | Emit to outbox | `AnonymousBasketRecord`, `OutboundEnvelope`; the generator's outbox with real payloads | Emit step; live sequence assignment |
| 4 | Tier 2 (stub) | `IPseudonymiser`, `TenantKeyStore`, the keys directory and its ACL (D-057) | Tier-2 port and no-op writer; `TenantKeyStore` in DI |
| 5 | Stub cloud | Generated outboxes under `offline_stretch` | Deferred within the phase |
| 6 | Expiry evaluator | `batches`, `parameter_registry`, the cold-start markdown path; generated batches with expiries | Store-side evaluator; near-expiry windows (*Hakim*) |
| 7 | Envelope, Integration Layer | Contracts, recommendation tables, `roles`, `staff` | Envelope write; role gating (*Hakim*: role model); pending queue |
| 8 | Local Admin card, accept | Brand rules | `waymark-admin` scaffold; the card; recommendations endpoint; `AcceptRecommendation` handler |

**Still needed from Hakim:** near-expiry windows (hop 6) and the minimal role model (hop 7),
plus the four points marked *Decide* in §6.1.

### 6.2 Hop 1: built, waiting for Hakim's review

Branch `phase-0.5/hop-1`, nothing committed. The plan's eight steps:

| Step | What | State |
| :---- | :---- | :---- |
| 1 | The lookup contract (`Contracts/Pos/ProductLookup.cs`) | ✅ Reviewed by Hakim |
| 2 | The lookup query and its rules (`Persistence/Catalogue/ProductLookup.cs`, D-066) | ✅ 27 tests, 13 breaks caught |
| 3 | The endpoint and StoreServer wiring (`GET /api/products/lookup?barcode=`; the store's date, D-067) | ✅ 24 tests plus a real-process smoke test, 9 breaks caught |
| 4 | The POS HTTP client (`Pos/Server/StoreServerClient.cs`), in the new `Waymark.Pos.Tests` | ✅ 13 tests, 7 breaks caught |
| 5 | D-063's UI point for hop 1 | ✅ Decided: neutral notices naming the code; the stock notice when count > stock; search by name is Phase 1 (D-068) |
| 6 | The cart and the window (`Pos/Checkout/*`, `Pos/TillWindow.cs`, D-068) | ✅ 43 tests, 12 breaks caught |
| 7 | The end-to-end run: seed-42 imported into StoreServer, the POS's code driven against it | ✅ Found and fixed the `%2F` route bug (D-066); the window starts; an outage shows as one |
| 8 | Paperwork, then **Hakim: review, test by hand, commit** | ⏳ The paperwork is done; Hakim's part is next |

**Hakim's part:**
1. Review the diff: `git diff` plus the new files (`git status`). The rules worth reading line by line are the lookup query (D-066), `StoreTimeZones` / `StoreCalendar` (D-067) and `Cart` / `WireFigures` (D-068). The window is glue.
2. Try it by hand: start StoreServer and the till (commands at the end of §6), then:
   - scan or simulate `2000000000015` twice: one line, ×2, 286.00 DZD;
   - `2000000000039`: sells, with the stock notice;
   - `0000000000000`: an unknown-code notice;
   - stop StoreServer and scan: a StoreServer-unavailable notice;
   - type a code slowly and press Enter: it is looked up; the digits never jump into the box during a real scan.
3. Commit, and push if happy.

**Carried forward from hop 1:**
- Bundle Archivo and IBM Plex Mono (named with fallbacks for now; bundling is a download, so it needs Hakim's permission).
- POS styling beyond "plain": colours for POS notices need their own brand decision (D-068).
- On Windows, a refused `localhost` connection retries until the 3 s timeout, so the notice reads "did not answer" rather than "could not be reached". The classification is correct; only the wording is imprecise.

### 6.1 The missing pieces, explained

Each piece says what it is, where it lives and what it needs first. Its **kind** says who
should write it:
- **rules**: money, stock, privacy, sync or engine. Hakim reviews line by line; these are
  the pieces worth writing yourself, and are marked ✍.
- **glue**: wiring, UI and scaffolding. Claude writes; Hakim reviews the result.

**1. Scan and cart**
- **POS window**: `Waymark.Pos`, glue. One Avalonia window with:
  - an input box that keeps focus;
  - the cart lines (product, quantity, unit price TTC, line total) and the total;
  - a *Pay cash* button.

  The scanner is wired as D-063 says: text input goes to `Accept`, `Typed` text goes into
  the box, `Flush` runs before a non-text key and `Reset` on focus change. A scan that
  matches nothing follows D-063's open UI point. The window stays code-only (D-007);
  `.axaml` arrives in Phase 1.
- **HTTP client**: `Waymark.Pos`, glue. The POS never opens the database (CLAUDE.md §2.2),
  so a small typed `HttpClient` calls StoreServer. Request and response shapes go in
  `Waymark.Contracts`, which the POS already references: no new project reference.
- **Product lookup endpoint**: StoreServer plus a query, glue with one rule in it. A barcode
  returns the variant, its name, unit, TVA rate and **current retail price**. The price is
  the `prices` row for this store and variant, `price_type = 'retail'`, with the latest
  `valid_from` that is not in the future. That comparison is the rule: get it wrong and the
  till sells at yesterday's price, silently.

**2. Complete sale**
- **StoreServer DI**: glue. StoreServer registers the context, ids, current store, currency
  and database key today. It does not yet register `IUnitOfWork`, `IProcessingLog`,
  `CommandExecutor`, `TimeProvider` or any handler. One HTTP request is one scope and one
  unit of work.
- **`CompleteSale` handler** ✍: Application, rules (money, stock). It takes the cart lines
  (variant, quantity) and the tender. It stages, in one unit of work:
  - the `transactions` row, with `rounding_policy` copied from the store (D-053);
  - `transaction_items`, TVA extracted from TTC per line (D-033);
  - `transaction_payments`;
  - the cash rounding to 5 DZD, with the difference in `rounding_variance` (D-034);
  - one `stock_movements` row per line;
  - the outbox row (hop 3).

  The arithmetic already lives in the value objects; this handler is where it is put
  together. The generator's `SaleWriter` writes exactly these rows and is the reference to
  test against.
- **A cash session to sell into**: a sale needs an open `cash_sessions` row. Seed one, or a
  tiny `OpenSession` command.
- **Sale endpoint**: glue. It posts the sale and returns the transaction id and the receipt
  figures.
- **F-21** is revisited here, since this is the first handler to write store-scoped rows.
- *Decide:* **cash only in the skeleton?** The proposal: on-account payment and its
  credit-limit check (D-055) wait for Phase 1's checkout, since the skeleton proves the path
  and not every tender.

**3. Emit to the outbox**
- **Emit step** ✍: Application, rules (privacy boundary). It builds the
  `AnonymousBasketRecord` from the staged sale (D-043, D-064):
  - date, hour bucket and weekday;
  - lines at product level, not variant;
  - payment class and the discount flag;
  - a fresh opaque basket id, not derived from the transaction id.

  It wraps the record in an `OutboundEnvelope` and stages the `outbox` row in the same unit
  of work as the sale (CLAUDE.md §3.6).
- **Sequence assignment**: rules (sync). A gapless per-store sequence (sync-design §2.2),
  assigned inside the same transaction, so a sale that rolls back uses no number. The
  generator's `Outbox` does this for generated data; the live version must hold under a
  real commit.

**4. Tier 2, a stub (D-065)**
- **The tier-2 port and a no-op writer**: glue. It stores nothing, so the Phase 2 DuckDB
  writer can replace it without touching the handler.
- **`TenantKeyStore` in StoreServer's DI**: glue, so `IPseudonymiser` can be injected.
- *Decide:* **the stub receives the pseudonym?** The proposal: when a sale has a customer,
  Application computes the pseudonym and hands the tier-2-shaped row to the stub, and a test
  inspects what it received. Since the basket carries no pseudonym (D-064), this keeps the
  pseudonymisation boundary exercised in 0.5.

**5. Stub cloud: deferred within the phase**
Nothing downstream depends on it, so its shape is decided when it is reached. It stays in
the phase's definition of done (Build Plan): deferred, not dropped.

**6. Expiry evaluator** ✍: rules (engine)
- Store-side and compare-only (CLAUDE.md §5). For each batch with stock on hand,
  `days_remaining = expiry − today`; inside its category's near-expiry window, it raises a
  recommendation. No fitting, no history.
- The windows are parameters in `parameter_registry`: **Hakim's decision**. The suggested
  action is the cold-start markdown path (20–25% / 40–50% / 60–70%, `System_Architecture`).
- The output carries its Because block (days remaining, quantity on hand, the window), its
  computed-at time, and its interval (CLAUDE.md §5). A count of days is exact, so the card
  says so rather than inventing a range.
- It runs as a StoreServer background job or on request; for the skeleton, whichever is
  simpler.

**7. Envelope and Integration Layer**: rules (Hakim's contract)
- **Envelope write**: the evaluator's output in the recommendation envelope (the contracts
  exist), written to `recommendations` and `recommendation_options`.
- **Role gating**: who may see an inventory recommendation. It needs the minimal role model,
  **Hakim's decision**; `roles` and `staff` exist.
- **Pending queue**: when nobody entitled is present, the output waits (`System_Architecture`).
- *Decide:* **pseudonym resolution now or in Phase 2?** Resolution with the
  `objection_flag` check and a log write that names its subject (D-061) applies only to
  output about a person. An expiry card is about a batch, so the skeleton's path never
  reaches it. Either build it now against a test-only, person-directed output, or leave it
  to Phase 2, where the Build Plan puts "Integration Layer, both halves".

**8. Local Admin card and accept**
- **`waymark-admin` scaffold**: glue. Vite, React, TypeScript and Tailwind, with RTL through
  logical properties from the first component (CLAUDE.md §9). One page listing pending
  recommendations.
- **The card**: glue under brand rules (CLAUDE.md §6):
  - white with a cyan top edge and a label;
  - the Because block, the interval and the computed-at age;
  - Accept, Adjust and Dismiss in violet;
  - the voice: "Suggested markdown: 25%".
- **Recommendations endpoint**: glue. StoreServer serves the pending cards.
- **`AcceptRecommendation` handler**: Application. It writes `recommendation_decisions`
  (who, when, which option) and the log entry.
- *Decide:* **does accepting apply the markdown?** The proposal: in the skeleton, accepting
  records the decision and does not change the price; applying it is Phase 1 pricing work.

The generator is ready for use:

```bash
dotnet run --project src/Waymark.Generator -- --config src/Waymark.Generator/inputs/configs/grocery-dz.json --out artifacts/generated/seed-42
```

To open a generated store in StoreServer, import it (D-056). **Paths must be absolute**:
`dotnet run` starts StoreServer in its project directory, so relative paths land under
`src/Waymark.StoreServer/` (found 18/09, with a keys directory in it). Run from the repository
root, in Git Bash:

```bash
dotnet run --project src/Waymark.StoreServer --no-launch-profile -- --urls=http://localhost:5290 "--Waymark:Storage:DataDirectory=$PWD/artifacts/server/data" "--Waymark:Storage:KeysDirectory=$PWD/artifacts/server/keys" "--Waymark:Storage:ImportPlaintextFrom=$PWD/artifacts/generated/seed-42/waymark-store.db" --Waymark:Store:StoreId=01JCWEQNC0W9W3YV7F0CPNDDC9 --Waymark:Store:Currency=DZD
```

The store id is seed-42's (`store_id` in `manifest.json`). Once imported, drop the
`ImportPlaintextFrom` switch: the encrypted copy reopens as it is. Then start the till, which
finds StoreServer at `http://localhost:5290/` by default:

```bash
dotnet run --project src/Waymark.Pos
```

In a Debug build the till has a **Simulate scan** box that feeds a code through the real
scanner. `2000000000015` is a milk at 143.00 DZD with stock; `2000000000039` is one with
none.

The guide is `src/Waymark.Generator/README.md`.

---

## Division of labour

| | Hakim | Claude |
| :---- | :---- | :---- |
| Arithmetic, schema, privacy boundary, sync rules, engine methods | Decides, reviews line by line | Drafts, tests first, proves each test fails |
| Generator | Writes the parameter spec, reviews distributions | Writes the code |
| Migrations | Reads the generated `Up()` before it runs | Generates, verifies, regenerates `schema_current.sql` |
| UI, CRUD, glue, scaffolding | Reviews the result | Writes |
