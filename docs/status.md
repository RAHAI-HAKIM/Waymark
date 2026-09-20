# Status

Where the work stands, what is wrong, and what comes next. Rewrite this file as work
lands; it is the only document that is allowed to go stale in a week. Last pass:
**20/09/2026**: All four sessions of Phase 0.5 are built and reviewed on branch
`phase-0.5/session-a`, **uncommitted and green**. `CardAudience.MayDecide` is written
(Hakim, 20/09) and Local Admin has now been installed, type-checked and built for the first
time. That closes every hop of Phase 0.5; what is left is the commit and the full revision
pass before Phase 1.

---

## 1. Snapshot

| | |
| :---- | :---- |
| Phase | **0.5, the walking skeleton: in progress** (opened 18/09/2026). Phase 0 complete 17/09/2026 |
| Build | `dotnet build src/Waymark.sln`, 16 projects (the new one is `Waymark.Pos.Tests`), **0 warnings**, Debug and Release. No vulnerable package |
| Sessions | **A** cash sale ✅ · **B** outbox, tier-2 stub, stub cloud ✅ · **C** expiry evaluator ✅ · **D** role check, Local Admin ✅. All four reviewed, all four uncommitted on `phase-0.5/session-a` |
| Tests | **995 passing**: Domain 161 · Integration 421 · Generator 235 · Hardware 64 · Application 41 · Pos 73 |
| Admin | `waymark-admin`: Node 24.19.0 LTS, 82 packages, 0 vulnerabilities. `npx tsc --noEmit` clean, `vite build` clean (Tailwind compiles). Installed 20/09 |
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
| A | 2 Complete sale | Built (D-070, D-071): the sale's rows, the money, the batches, `POST /api/sales`, the till's Pay button, and the write guard that refuses another store's row | All-or-nothing, the figures, gapless invoices, cross-store writes: 21 tests, 11 breaks caught | Reviewed 20/09; D-071 decided | ✅ Built, in review |
| B | 3–5 Outbox, tier-2 stub, stub cloud | Built (D-072): the sale stages its anonymous basket as an outbox row in the same transaction, with a gapless sequence; tier 2 is a port with a writer that keeps nothing; the stub cloud is a reader in the tests | The basket commits with the sale or not at all; no identifier in it; no gap after a refusal: 12 tests, 7 breaks caught | The emit mapping (written by Claude; review it) | ✅ Built, reviewed 20/09 |
| C | 6 Expiry evaluator | Built (D-073): the comparison is `NearExpiry` in Domain, pure; the window is a 7-day cold-start placeholder in `parameter_registry`, installed at start and never repaired; a card carries its Because block, its window version and its computed-at time; `POST /api/engine/expiry` | The window's edge, the figures, cross-store reads, and running it twice: 23 tests, 9 breaks caught | The comparison (written by Claude; review it) | ✅ Built, reviewed 20/09 |
| **D** | 7–8 Integration Layer, Local Admin | Built (D-074): the board and the decision both go through one role check, compared on `roles.rank`; a cashier is told how many cards are above their rank, never shown them; accepting writes the decision **and** closes the card in one transaction, and applies nothing. `waymark-admin` scaffolded (Vite, React, TS, Tailwind, RTL, brand tokens), one page, installed and built 20/09 | The decision and the card's status written together; a cashier, a stranger, another store's manager and a suspended manager all refused: 17 tests, 8 breaks caught | ✍ **`CardAudience.MayDecide`** — written 20/09 (first attempt inverted; four tests caught it) | ✅ Built, reviewed 20/09 |

**Phase 1 picks this up** (left out of the slice on purpose):
- from hop 1: search by name; weighted items and scales; promotional prices; O-24 (the TVA
  rate when categories disagree); bundling the fonts (a download, so it needs Hakim's
  permission); POS styling, and a brand decision on colours for POS notices; the notice
  wording when `localhost` refuses (it reads "did not answer within 3 s");
- from the thin hops: on-account payment and its credit-limit check (D-055); more than one
  tender; returns and voids; the Level-2 cache; the pending-queue UI; applying an accepted
  markdown to the price; logins;
- from session C: running the evaluator nightly instead of on request; retiring a card whose
  batch has sold out or been written off (it keeps its card today, D-073); carrying out an
  accepted markdown or write-off;
- from session D: logins (the staff member is named per request today); adjust and snooze;
  a card's delivered-at ever being set; generating the TypeScript contracts from the C# ones
  instead of hand-writing them; bundling Archivo and IBM Plex Mono (a download, so it needs
  Hakim); the rest of the Admin stack (Radix, TanStack Table and Query, zod, Recharts).

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

### 6.1c Session B, read in the order a basket travels

1. **Where it starts:** `Application/Sales/CompleteSale.cs`. The sale loop now also collects
   `sold` (product, quantity, line total) and `tier2Lines` (variant grain); at the end,
   `EmitBasket` and the tier-2 call. Both are inside the command, so the executor commits
   the outbox row with the sale's rows, or neither (CLAUDE.md §3.6).
2. **What crosses:** `Application/Sync/AnonymousBasket.cs`. A pure function: product grain,
   the store's hour, the weekday, the payment class, a fresh opaque basket id. The
   comment says what must not be there and why.
3. **The figures:** `Contracts/Figures.cs`, now the one place stored integers become wire
   text (the till's answers use it through `StoreServer/WireText.cs`).
4. **The hour:** `Domain/IStoreCalendar.cs` (`Now`, `Today`, `HourOfDay`) and
   `Application/Time/StoreCalendar.cs`.
5. **The sequence:** `Domain/Sync/IOutboxSequence.cs`, implemented in
   `Persistence/Sync/OutboxSequence.cs`. Read inside the sale's transaction; the unique
   index on `outbox.sequence_number` is the backstop.
6. **Tier 2, stubbed:** `Domain/Statistics/ITier2Writer.cs` and
   `Application/Statistics/NullTier2Writer.cs` (D-065).
7. **The write guard (D-071):** `Persistence/WaymarkDbContext.cs`,
   `RefuseAnotherStoresRows`, called by both `SaveChanges` overrides.
8. **The proof:** `Integration.Tests/CompleteSaleTests.cs`, section "the outbox", including
   `StubCloud`, the hop-5 reader that parses a pending row and acknowledges it;
   `Application.Tests/AnonymousBasketTests.cs`; `Integration.Tests/StoreWriteScopeTests.cs`;
   and the sale on the real process in `StoreServerStartupTests`.

To watch one basket: a breakpoint in `EmitBasket`, then sell. Afterwards the row is visible
in the encrypted store (the outbox keeps it until something acknowledges it).

### 6.1d Session C, read in the order a batch becomes a card

1. **The rule, and the only file that matters:** `Domain/Engine/NearExpiry.cs`. Pure, no
   database. `Evaluate` returns a finding or **null** — null is "there is nothing to say",
   which is most batches. Read the three early returns first: no shelf life, nothing left,
   still outside the window. Then the two figures: the quantity, which needs one unit, and
   the value at cost, which does not.
2. **What it reads through:** `Domain/Engine/IExpiryLedger.cs` — the window, the shelf, the
   board as it stands, and the one row it changes. Implemented in
   `Persistence/Engine/ExpiryLedger.cs`: three queries, each scoped by the global filter
   (`batch_items` through its batch, D-062).
3. **The window:** `Persistence/Engine/ColdStartParameters.cs`. Seven days, installed at
   start when the registry has none, never repaired. `Program.cs` calls it in the startup
   block, after the time zone.
4. **The card:** `Application/Engine/EvaluateExpiry.cs`. `HandleAsync` is the whole hop in
   thirty lines: window → shelf → board → per finding, supersede then write. Then read
   `Because` (three figures, each with its unit) and `Headline` (a rendering of them).
   The class comment says why a card here has no interval.
5. **Who it is addressed to:** `DecidingRoleAsync` in the ledger — the rung above the shop
   floor, read off `roles.rank` rather than named in the code (D-073).
6. **The door:** `StoreServer/Program.cs`, `MapPost("/api/engine/expiry")`.
7. **The proof:** `Domain.Tests/NearExpiryTests.cs` (the window's edge, first section) and
   `Integration.Tests/ExpiryEvaluatorTests.cs` (what it flags, what it stays quiet about,
   running it twice). `StoreServerStartupTests.AssertExpiryEvaluation` runs it twice on the
   real process over a generated store.

To watch one batch: a breakpoint in `NearExpiry.Evaluate`, then post to the endpoint. On
seed-42 the first run flags around a hundred batches out of about three hundred — a year of
trading leaves a lot past its date — and the second run flags the same number and supersedes
exactly that many.

### 6.1e Session D, read in the order a card reaches a person

1. ✍ **Hakim's piece:** `Domain/Engine/CardAudience.cs`. One function, one comparison —
   `staffRank >= requiredRank`, "this rank and anything above it". Written 20/09, after a first
   attempt with the comparison inverted, which four tests caught: a cashier passed and an owner
   was locked out, both failure modes D-074 names, at once.
2. **What the board reads:** `Domain/Engine/IRecommendationBoard.cs`, implemented in
   `Persistence/Engine/RecommendationBoard.cs`. Note `StaffAsync`: active staff only, and a
   role that is not an active row gives **no rank** rather than rank zero (D-037).
3. **The board itself:** `Application/Engine/PendingCards.cs` — twelve lines. It filters with
   `CardAudience` and reports what it held back as a **count**. The comment says why an empty
   board is a lie.
4. **The one door out:** `Application/Engine/DecideRecommendation.cs`. `HandleAsync` is six
   numbered checks, then two staged writes: the decision row, and the card's move to
   `decided`. They commit together or neither does, which is the risky rule of the hop.
5. **The wire:** `StoreServer/Engine/RecommendationWire.cs` (a card crosses as the envelope of
   D-044; the Because block and the option payloads are re-parsed here, not passed through as
   strings) and `Contracts/Recommendations/DecisionRequest.cs`.
6. **The doors:** `StoreServer/Program.cs`, `MapGet("/api/recommendations")` and
   `MapPost("/api/recommendations/decide")`.
7. **The screen:** `waymark-admin/src/Board.tsx`, and `src/index.css` for the brand tokens.
   The page decides nothing — it renders the board the server already filtered.
8. **The proof:** `Domain.Tests/CardAudienceTests.cs` (yours, three skipped) and
   `Integration.Tests/RecommendationBoardTests.cs` (thirteen, four skipped).

With StoreServer and the admin app running, the board fills for the owner and stays empty —
with a count of what was withheld — for the cashier.

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

To run the expiry evaluator against the running server (PowerShell):

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5290/api/engine/expiry -ContentType application/json -Body '{}'
```

It answers with how many batches it read, how many it flagged, how many earlier cards it
replaced, and the window it used. Running it twice is safe and is worth doing: the second
run should flag the same number and supersede exactly that many.

Local Admin (hop 8) needs Node — 24.19.0 LTS, installed 20/09 via
`winget install OpenJS.NodeJS.LTS`. **It lands on the machine PATH, so a terminal opened
before the install will still say `npm: not recognized`; open a new one.** With StoreServer up
on 5290:

```bash
cd waymark-admin && npm install && npm run dev
```

The dev server proxies `/api` to StoreServer. Type a staff id into the box: seed-42's cashier
`01JCWEQNC0W9W3YV7F0CPNDDCB` sees only a count of withheld cards; the owner sees the
near-expiry cards themselves.

The guide is `src/Waymark.Generator/README.md`.

---

## Division of labour

| | Hakim | Claude |
| :---- | :---- | :---- |
| Arithmetic, schema, privacy boundary, sync rules, engine methods | Decides, reviews line by line | Drafts, tests first, proves each test fails |
| Generator | Writes the parameter spec, reviews distributions | Writes the code |
| Migrations | Reads the generated `Up()` before it runs | Generates, verifies, regenerates `schema_current.sql` |
| UI, CRUD, glue, scaffolding | Reviews the result | Writes |
