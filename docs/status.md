# Status

Where the work stands, what is wrong, and what comes next. Rewrite this file as work
lands; it is the only document that is allowed to go stale in a week. Last pass:
**19/09/2026**: Session A (complete sale) is built on branch `phase-0.5/session-a`, uncommitted,
waiting for Hakim: review, the provisional decisions in D-070, hand test, commit (§6).
Next: session B (outbox, tier-2 stub, stub cloud).

---

## 1. Snapshot

| | |
| :---- | :---- |
| Phase | **0.5, the walking skeleton: in progress** (opened 18/09/2026). Phase 0 complete 17/09/2026 |
| Build | `dotnet build src/Waymark.sln`, 16 projects (the new one is `Waymark.Pos.Tests`), **0 warnings**, Debug and Release. No vulnerable package |
| Tests | **936 passing**: Domain 146 · Integration 385 · Generator 235 · Hardware 64 · Application 33 · Pos 73 |
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

One thin cut (Build Plan): scan → cart → complete sale → transaction and stock movement →
outbox → stub cloud reads it → expiry evaluator flags a batch → envelope → Integration
Layer role check → card in Local Admin → accept → decision written and logged.

**Reframed 19/09 (D-069): a thin slice, four sessions, one commit each.** Every hop ships:
- the minimum that makes the path real: one store, cash, the happy path;
- a happy-path test, plus tests on its **one risky rule**, proven by breaking it;
- a short "how it works" guide;
- Hakim's review and a commit.

Anything more goes on the Phase 1 list below. Pseudonymisation is not exercised in 0.5 (D-069).

| Session | Hop | Thin version | Risky rule, tested | ✍ Hakim | State |
| :---- | :---- | :---- | :---- | :---- | :---- |
| — | 1 Scan, cart | The lookup (D-066), the store's date (D-067), the till (D-068) | Price in force, store scoping, exact figures | The lookup query | ✅ Committed `f3fe0fb` |
| A | 2 Complete sale | Built (D-070): `CompleteSale` stages the transaction, items (TVA from TTC), stock movements with batch levels, the cash payment and the tender rounding; `POST /api/sales`; the till's Pay button | All-or-nothing, the money figures, gapless invoices: 10 tests, 8 breaks caught | Review; the provisional decisions in D-070 | ⏳ **Waiting for Hakim** |
| **B** | 3–5 Outbox, tier-2 stub, stub cloud | The emit step builds the `AnonymousBasketRecord` (D-043, D-064) and stages the outbox row in the sale's unit of work, with a gapless sequence. The tier-2 port gets a no-op writer. The stub cloud is a test-side reader that parses what left and acknowledges it | The outbox row commits with the sale, or neither does; no identifier in the basket; no gap in the sequence after a rollback | The emit mapping | |
| C | 6 Expiry evaluator | Compare only (CLAUDE.md §5): batches with stock, days to expiry, against one **7-day placeholder** window in `parameter_registry`. Writes a recommendation with its Because block and computed-at time. Runs on request | No fitting, no history; the window boundary; the figures in the Because block | The comparison | |
| D | 7–8 Integration Layer, Local Admin | A **manager** may see and decide inventory cards; a **cashier** is refused (from `roles`; staff named per request, no login). The `waymark-admin` scaffold (Vite, React, TS, Tailwind, RTL): one page of pending cards; Accept writes `recommendation_decisions` and the log. Accepting does not apply the markdown | The decision and its log entry are written together; a cashier is refused | The role check | |

**Phase 1 picks this up** (left out of the slice on purpose):
- from hop 1: search by name; weighted items and scales; promotional prices; O-24 (the TVA
  rate when categories disagree); bundling the fonts (a download, so it needs Hakim's
  permission); POS styling, and a brand decision on colours for POS notices; the notice
  wording when `localhost` refuses (it reads "did not answer within 3 s");
- from the thin hops: on-account payment and its credit-limit check (D-055); more than one
  tender; returns and voids; the Level-2 cache; the pending-queue UI; applying an accepted
  markdown to the price; logins.

**Phase 2, already planned:** the real tier 2, encrypted (D-065, O-23); the customer period
record and its spend bands (D-064); pseudonym resolution with `objection_flag` (the
Integration Layer, both halves); the per-category near-expiry windows.

### 6.1 Hop 1, read in the order a scan travels

1. **The keystroke:** `Waymark.Hardware/KeyboardWedgeScanner.cs`. `Accept` holds each
   character; `EndBurst` classifies it (8 or more is a scan, anything shorter was typed).
2. **The window:** `Waymark.Pos/TillWindow.cs`. The tunnel `OnTextInput` and `OnKeyDown`
   feed the scanner; `Submit` hands the code to the session; `Render` draws. `App.cs` wires
   it together.
3. **What to do with a code:** `Pos/Checkout/TillSession.cs`. `_tail` keeps scan order;
   `Handle` is one switch over the four answers.
4. **The cart:** `Pos/Checkout/Cart.cs`, `WireFigures.cs`. Figures are read exactly;
   `LineTotal = UnitPrice * Count`; `ExceedsStockOnHand`.
5. **The HTTP call:** `Pos/Server/StoreServerClient.cs`, which returns `Answered` or
   `ServerUnavailable`.
6. **The wire shape:** `Contracts/Pos/ProductLookup.cs`.
7. **The server's door:** `StoreServer/Program.cs` (DI, the startup checks and the time
   zone, then `MapGet("/api/products/lookup")`) and `StoreServer/Catalogue/ProductLookupWire.cs`.
8. **The store's date:** `Application/Time/StoreTimeZones.cs`, `StoreCalendar.cs`.
9. **The rules:** `Persistence/Catalogue/ProductLookup.cs`, whose port is
   `Domain/Catalogue/IProductLookup.cs`. Store scoping is the global filter in
   `WaymarkDbContext` (`HasQueryFilter`); the query never names a store.
10. **The whole path:** `StoreServerStartupTests.A_generated_store_is_imported_served_and_reopened_after_a_restart`.

Each file has its tests beside it, under `src/tests/`, with the same name plus `Tests`.
To watch a code cross every stop, set breakpoints in `TillSession.Handle` and in the
`MapGet` lambda, run both processes, and scan `2000000000015`.

### 6.1b Session A, read in the order a sale travels

1. **The Pay button:** `Waymark.Pos/TillWindow.cs` (`Pay()`), then
   `Pos/Checkout/TillSession.cs` (`PayAsync` → `PayAfter`). It waits for scans still
   being looked up, sends codes and counts, and reacts to one of three answers: completed
   (the cart empties, `LastSale`), refused (a notice, the cart kept), unknown (a warning,
   the cart kept).
2. **The HTTP call:** `Pos/Server/StoreServerClient.cs` (`CompleteSaleAsync`). Any answer
   it can't read is `Unknown`, never `Completed`.
3. **The wire:** `Contracts/Pos/Sale.cs`.
4. **The door:** `StoreServer/Program.cs`. The DI block "Commands (D-050)" (the unit of
   work is both `IUnitOfWork` and `IStaging`), then `MapPost("/api/sales")` with its
   one-sale-at-a-time gate. `StoreServer/Sales/SaleWire.cs` maps the result;
   `StoreServer/WireText.cs` formats the figures.
5. **The executor:** `Application/Commands/CommandExecutor.cs`. The handler stages, the
   executor commits once, or discards on any exception.
6. **The handler, the heart of it:** `Application/Sales/CompleteSale.cs`. Read
   `HandleAsync` top to bottom: re-price each line → session → per line, the batches →
   per batch, `SaleArithmetic.Line` → stage the item, the movement, the level → invoice →
   stage the transaction, the payment and the variance.
7. **The rules it calls:**
   - `Domain/Sales/SaleArithmetic.cs`: TVA from TTC; the same code as the generator;
   - `Domain/Inventory/BatchAllocation.cs`: first in, first out, and the shortfall;
   - `Money.ToCashTender()`: the 5 DZD step.
8. **The reads:** `Domain/Sales/ISalesLedger.cs`, implemented in
   `Persistence/Sales/SalesLedger.cs`. Store-scoped by the filter; `AdjustLevel` changes a
   tracked `inventories` row.
9. **The proof:** `Integration.Tests/CompleteSaleTests.cs`: atomicity, money, invoice,
   session, batches. `Domain.Tests/BatchAllocationTests.cs`. And
   `StoreServerStartupTests.AssertSale`, a sale on the real process.

To watch one sale: breakpoints in `CompleteSaleHandler.HandleAsync` and in
`CommandExecutor.ExecuteAsync` (on `CommitAsync`), then scan and press Pay.

### 6.2 Running it

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
`ImportPlaintextFrom` switch: the encrypted copy reopens as it is. In PowerShell (seed-42
already imported), wait for `Now listening on: http://localhost:5290`:

```powershell
dotnet run --project src/Waymark.StoreServer --no-launch-profile -- --urls=http://localhost:5290 "--Waymark:Storage:DataDirectory=$PWD\artifacts\server\data" "--Waymark:Storage:KeysDirectory=$PWD\artifacts\server\keys" --Waymark:Store:StoreId=01JCWEQNC0W9W3YV7F0CPNDDC9 --Waymark:Store:Currency=DZD
```

Then start the till in a second terminal. It finds StoreServer at `http://localhost:5290/`
by default. To **pay**, it also needs its terminal and staff ids (seed-42's till and first
cashier are below); without them, Pay says so and sends nothing:

```bash
dotnet run --project src/Waymark.Pos -- --terminal=01JCWEQNC0W9W3YV7F0CPNDDCD --staff=01JCWEQNC0W9W3YV7F0CPNDDCB
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
