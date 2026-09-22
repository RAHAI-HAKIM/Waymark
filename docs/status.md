# Status

Where the work stands, what is wrong, and what comes next. Rewrite this file as work
lands; it is the only document that is allowed to go stale in a week. Last pass:
**22/09/2026**: **Phase 1 is open** and **session A1 is in flight** — O-24 is answered by
D-075, the promotional price rule is D-076, and the tests are written and red. Phase 0.5's
recap is `recaps/phase-0.5.md`. The plan is `phase-1-plan.md`.

**A1 is green** (§9). **A3 is green and A2 is mid-handover** (§10): `StaffPermissions` and
`StaffPin` are stubs and are ✍ Hakim's, with 18 red tests waiting. Everything else passes.

---

## 1. Snapshot

| | |
| :---- | :---- |
| Phase | **1, the till runs a shop: opening 22/09/2026.** Phase 0.5 closed 21/09/2026; Phase 0 closed 17/09/2026 |
| Build | `dotnet build src/Waymark.sln`, 16 projects, **0 warnings**. No vulnerable package. **Stop StoreServer before building**: a running host holds `src/Waymark.StoreServer/bin` and the copy fails with `MSB3021`, which reads like a code error and is not one |
| Tests | **1075: 1057 passing, 18 red on purpose** — Integration 468 · Generator 237 (1 red) · Domain 190 (17 red) · Pos 75 · Hardware 64 · Application 41. Every red one is A2's two stubs; see §10 |
| Schema | 61 tables (all STRICT), 88 indexes, 29 triggers, 6 migrations. **Unchanged by Phase 0.5** — the skeleton needed no migration |
| Encryption | `waymark-store.db` is SQLCipher-encrypted by StoreServer, which refuses a plaintext store and imports one instead (D-056). The generator's output is plaintext by design |
| Admin | `waymark-admin`: Node 24.19.0 LTS, 82 packages, 0 vulnerabilities. `tsc --noEmit` and `vite build` both clean |
| Recaps | `recaps/phase-0.md` and `recaps/phase-0.5.md`. Read one only when a question reaches back into a finished phase |

## 2. Phase 0.5, closed

The walking skeleton: **scan → cart → sale → outbox → stub cloud → expiry evaluator → role
check → card → decision**, built in four sessions and eight hops (D-069), closed 21/09/2026.

Everything in it is real except the cloud. `recaps/phase-0.5.md` holds what was built, the
twelve decisions with their rejected alternatives, and **§5, the seven defects the thin cut
found** — among them a time-zone resolution that could not work on Windows, a barcode with a
slash looked up as `%2F`, writes never checked against the current store (F-21, closed by
D-071), a role code the store might not have, and a role check written inverted.

**Confirmed 21/09/2026:** D-074's two points — no `processing_log` entry for a card whose
subject is a batch, and accept/dismiss only. **Seven rules are still provisional**; the recap
§6 names where each is settled, and three of them are settled by Phase 1 itself.

---

## 3. Findings register

Each item is either **fix** (Claude can do it, no decision needed) or **decide** (Hakim,
usually an `O-` entry). Close an item by deleting its row.

| # | Sev. | Finding | Where | Action |
| :---- | :---- | :---- | :---- | :---- |
| F-17 | Low | The number *29* is hardcoded into every count check, This seems to be edited at every schema change | `Waymark.Integration.Tests.TriggerApplicationTests.cs` | Saved for claude to answer outside phase01 sessions |
| F-15 | Low | **One unexplained integration failure.** On 14/09 a full-solution `dotnet test`, run straight after a build, failed one integration test, and the name was not captured. Every run since has been green. **New lead, 22/09:** a stale test assembly can do exactly this. Restoring a source file with `mv` (or any copy that keeps the original mtime) leaves it older than the built DLL, MSBuild skips the project, and `dotnet test` runs the *previous* code — a failure with no matching source. Cost an hour in A1 | `Waymark.Integration.Tests` | **Watch**: if it recurs, capture the test name (`--logger "console;verbosity=detailed"`) before anything else, and check the DLL is newer than the source |

Phase 0.5's own findings were all closed inside the phase; `recaps/phase-0.5.md` §5 lists
them and what settled each.

## 4. Open questions

The full text is in `decisions.md`, "Open — waiting on Hakim".

| # | Question | Disposition |
| :---- | :---- | :---- |
| O-23 | *How* is statistics tier 2 (local DuckDB) encrypted, and with what key? *Whether* is settled: it is (D-065) | DuckDB's encryption is not SQLCipher. Decide with the real tier-2 writer in Phase 2. The DPIA states the gap meanwhile (§5.4) |

---

## 5. Tests carried into Phase 1

The final test's remaining ideas, still open after the skeleton. None blocks Phase 1; take
each as the code it touches is next changed.

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

## 6. Next: Phase 1 — the till runs a shop

**The plan is `phase-1-plan.md`** — the pace, the work split, the design gates, the tool ramp
and the session backlog. It is the *how*; `Waymark_Build_Plan.md` is the *what* and the
definition of done. What follows is only the shape and the progress.

**Phase 1 is Phase 0.5 widened.** Every session names the Phase 0.5 file it expands, so the
starting point is always code already reviewed. §7 below is the map.

**≈46 sessions of 4 h, one or two a day, every day: 23–46 days.**

| Block | What | Sessions | State |
| :---- | :---- | :--: | :---- |
| **A** | The floor: O-24 and promotional prices, sessions and PIN and permissions, reason codes, the till shell | 4 | **A1 and A3 done** (§9, §10). **A2 mid-handover**; A4 needs gate G1 |
| **B** | Checkout depth: search, quantity, weighted, discounts, override, split tender, on-account, voids, refunds, paid-in/out | 10 | |
| **C** | Shift: counted float, X and Z reports, handover | 3 | |
| **D** | Receipts and hardware: content, real ESC/POS, the drawer, reprint | 3 | |
| **E** | Catalogue, first Admin batch: CRUD, bulk price, CSV import | 4 | Needs design gate **G2** |
| **F** | Stock: receive against a PO, adjustments, counts, views | 4 | |
| **G** | Customers and compliance: consents, objection, loyalty, rights tooling, `processing_log` | 7 | Needs design gate **G3**. The block most likely to grow |
| **H** | Staff and store | 2 | |
| **I** | Platform: Admin over the LAN, offline and the Level-2 cache, backup and restore, the recovery code, the evaluator nightly | 6 | |
| **J** | The done-when: the simulated day, the consent-to-erasure walkthrough, the restore drill | 3 | |

**Two gates before code:** design **G1** before block B (the till) and **G2**/**G3** before
blocks E and G. Hakim brings the design; Claude reviews it against CLAUDE.md §6 first.

**Hakim writes the decision-bearing core this phase** — refunds and voids, discount
allocation, tender and change, cash reconciliation, consent and rights, PIN and permissions.
The handover is **tests first**: Claude writes the failing tests and a guiding comment, Hakim
makes them green. Session D's inverted role check is why.

---

## 7. The Phase 0.5 code, read in the order it runs

The map Phase 1 expands from. Each guide walks one session's work in the order data moves
through it; each file has its tests beside it under `src/tests/`, with the same name plus
`Tests`.

### 7.1 Hop 1, read in the order a scan travels

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

### 7.2 Session A, read in the order a sale travels

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

### 7.3 Session B, read in the order a basket travels

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

### 7.4 Session C, read in the order a batch becomes a card

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

### 7.5 Session D, read in the order a card reaches a person

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

---

## 8. Running it

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

---

## Division of labour

Phase 1 moves Hakim from reviewing to writing the core. `phase-1-plan.md` §3 is the full
table; this is the short form.

| | Hakim | Claude |
| :---- | :---- | :---- |
| Money and stock arithmetic | **Writes** refund and void rules, discount allocation, tender and change, cash reconciliation | The code around it, the wire, the screens, the failing tests it drops into |
| Privacy | **Writes** consent, objection and rights logic, and anything touching `consent_events` or `processing_log` | The CRUD, the forms, the rendering |
| Access | **Writes** PIN gating and permissions | Sessions, endpoints, plumbing |
| Schema and migrations | Reads the generated `Up()` before it runs | Generates, verifies, regenerates `schema_current.sql` |
| Design | **Brings the design** before any screen is built | Reviews it against CLAUDE.md §6, then builds |
| UI, CRUD, glue, scaffolding | Reviews the result | Writes |

---

## 9. Session A1, read in the order a barcode is priced

**In flight, 22/09/2026.** O-24 answered by D-075; the promotional price rule is D-076. It
expands `Persistence/Catalogue/ProductLookup.cs` (D-066) — steps 3 and 4 of the five.

### ✍ Hakim's piece

**`Domain/Catalogue/TvaRate.cs`** — one function, `Resolve`, pure, no database and no clock,
on the `NearExpiry` model (D-073). The class comment states D-075's four cases and the trap;
`TvaRateTests.cs` argues each one separately. In short:

1. one distinct rate and no category silent → that rate, `FromCategory`;
2. no categories → 19%, `StandardFallback`;
3. **any null among the rates** → 19%, `StandardFallback`, *even beside a stated rate*;
4. two or more distinct rates → 19%, `StandardFallback`.

**The trap is case 1.** Before D-075 a product whose categories disagreed was refused at the
till and somebody fixed the catalogue; now it sells. Written one step too wide, the fallback
fires on products whose categories agreed, and every 9% line in the shop — bread, milk,
pharmacy — is taxed at 19%. Every receipt still recomputes from its own row and every total
still adds up. The second trap is the source: deriving it from the rate makes
`The_standard_rate_stated_by_a_category_is_not_the_fallback` pass by accident and hides every
miscategorised standard-rated product from block E.

**Broken on purpose** (D-012): pointing the promotional row at this store instead of the
other one flips `Another_stores_promotion_is_invisible` from 120.00 to 90.00, so the store
filter on the row that now decides the price is genuinely under test. The two rule mutations
worth repeating if `TvaRate` is ever touched: fire the fallback on a single agreed rate
(`One_category_with_a_rate_is_that_rate` and `The_rate_comes_from_the_products_category`
must fail), and return `FromCategory` whenever the rate is 19%
(`A_silent_category_beside_a_standard_one_is_still_the_fallback` must fail).

### The rest, in the order it runs

1. **The rates leave the database:** `Persistence/Catalogue/ProductLookup.cs`, step 3. The
   join keeps nulls — the skeleton dropped them, which answers 9% for [9%, null]. `Distinct`
   stays because SQL keeps one null and one of each value, so both signals survive it.
2. **The rule:** `Domain/Catalogue/TvaRate.cs`. Above.
3. **The price:** same file, step 4 (D-076). Both `retail` and `promotional` rows in force
   come back; `Latest` picks the later `valid_from` within a type, and promotional beats
   retail. `is_tax_inclusive` is checked on **whichever row won**, with no falling back to
   retail — that would charge full price for a product on promotion and say nothing.
4. **What crosses:** `Domain/Catalogue/IProductLookup.cs` — `ProductForSale` gains
   `TvaRateSource` and `IsPromotionalPrice`, and `NotSellableReason` **loses** `NoTaxRate` and
   `ConflictingTaxRates`. `Contracts/Pos/ProductLookup.cs` mirrors both, and
   `StoreServer/Catalogue/ProductLookupWire.cs` maps them.
5. **The cashier:** `Pos/Checkout/Cart.cs` (`CartLine.IsPromotionalPrice`, re-read on every
   scan) and `Pos/TillWindow.cs` (`LineRow`, the words `PROMOTIONAL PRICE` in neutral slate).
   The TVA source is deliberately **not** shown: a cashier cannot fix a catalogue, and block E
   lists it instead.
6. **The proof:** `Domain.Tests/TvaRateTests.cs`, then `ProductLookupTests.cs` — its TVA
   section checks the query hands the rule the right facts, and its promotional section is
   D-076 rule by rule.

Nothing was needed from `CompleteSale`: it re-prices every line through `IProductLookup`
itself (`CompleteSale.cs:249`), so the preview and the receipt changed together and cannot
drift.

### Carried out of A1

- The generator seeds no promotional rows, so **seed-42 cannot demo a promotion**. Picked up
  at **E2**; teaching the generator would change every canonical dump.
- **D-075's other three rows** (composite, mixed, indivisible supply) are not implementable
  against this schema and are Admin catalogue guidance at **block E**. The reasoning is in
  D-075.
- Nothing lists products selling on `standard_fallback` yet. That screen is **block E**.

---

## 10. Sessions A2 and A3, one commit

**A3 is green. A2 is ✍ Hakim's and its tests are red on purpose.** The two share no file.

### ✍ A2 — what to write

Two stubs, both pure Domain, no database and no clock.

**1. `Domain/Organisation/StaffPermissions.cs`** — permissions from `roles.rank`, no table
(D-077 is yours to write). `RequiredRank(Capability)` maps each capability to a minimum rank;
`May(long? staffRank, Capability)` answers whether somebody may do it.

- **The trap is the comparison**, the same one that went in backwards in session D (D-074):
  a cashier could decide a manager's card and an owner could not. Inverted here **nothing
  refuses** — a cashier discounts, overrides and voids all day, every receipt prints, and the
  first sign is the month's takings.
- **The second trap is null.** A staff member who is not active, or whose role is not an
  active row, has **no** rank. Zero would make them the most junior person in the shop rather
  than an error, and a junior person is still somebody (D-037;
  `RecommendationBoard.StaffAsync` already returns null for exactly this).
- `Deciding_a_card_asks_the_same_question_CardAudience_does` holds the new rule against
  D-074's. They are one comparison, and the test fails if they ever disagree — so
  `CardAudience.MayDecide` is a candidate to become a caller rather than a twin.
- The **ranks are yours**. The tests assert only that every capability has a positive one,
  that an unmapped member throws rather than defaulting, and that the three actions the build
  plan already calls manager-gated (B5's override, B8's void, `ICashDrawer`'s no-sale) ask
  for more than the shop floor.
- The `Capability` list is a starting set, every member justified by code that already exists.
  Extend it as B4, B5 and B8 land.

**2. `Domain/Organisation/StaffPin.cs`** — `IsUsable(storedHash)`, asked **before** any PIN is
checked. False for `"synthetic:no-login"` and for anything absent or blank, true otherwise.
It is deliberately **not** the verifier and knows no algorithm: the KDF is still your choice
and belongs in infrastructure, since Domain has zero dependencies (§2.1). The danger is the
shape of the mistake — a verifier that merely hashes the offered PIN and finds it unequal to
the sentinel refuses today *by luck*, and starts accepting the day the stored format changes.
`Waymark.Generator.Tests/SyntheticPinTests` holds the generator's constant and Domain's
together, because nothing that ships may reference the generator (D-054).

**Still yours to decide, and untested until you do:** whether a till login session is a row or
in-memory in StoreServer (nothing in the schema holds one today; B10's clock in/out and I2's
offline cache may want the row), and which KDF hashes a PIN.

When it is green, **break it on purpose** (D-012): invert the comparison and watch
`Exactly_the_required_rank_is_allowed_and_so_is_anything_above_it` and
`Below_the_required_rank_is_refused` fail together; then make `May(null, …)` true and watch
`No_rank_at_all_is_never_permission` fail.

### A3 — what was built, in the order a reason travels

1. **The port:** `Domain/Reference/IReasonCodes.cs`. `ForAsync(appliesTo)` → the active
   reasons, ordered. `ReasonCodeChoice` carries `requires_note` and `requires_manager` and
   decides nothing with either.
2. **The reader:** `Persistence/Reference/ReasonCodes.cs`. Active only, `display_order` then
   **code** — ties are otherwise returned in whatever order SQLite likes, and a dialog that
   reshuffles is one a cashier stops reading. No store filter, because the vocabulary is the
   tenant's, and the comment says so out loud.
3. **The wire:** `Contracts/Reference/ReasonCodes.cs`, mapped by
   `StoreServer/Reference/ReasonCodeWire.cs`. `ReasonCodeOption` mirrors `reason_codes` in
   `ContractsMirrorTheSchemaTests`, with a reason for each of the four columns that stay behind.
4. **The door:** `GET /api/reason-codes?applies_to=discount`. An unknown kind is a **400**,
   not an empty list.
5. **The proof:** `Integration.Tests/ReasonCodeTests.cs` (nine, against a real database),
   `ReasonCodeWireTests.cs` (the mapping and every kind), and `AssertReasonCodes` inside
   `StoreServerStartupTests` — the only thing that proves the DI and the route exist, run
   against a real generated store on the real process.

**Broken on purpose** (D-012), three mutations, each failing only what it should: dropping
`IsActive` failed `A_retired_reason_is_not_offered`; dropping the code tie-break failed
`Reasons_that_share_a_display_order_are_still_in_a_fixed_order`; dropping the kind filter
failed three, including `A_reason_for_another_kind_never_appears`.

**Deliberately not done:** no UI. The till's picker waits for gate **G1** and the A4 shell
(§6), and B4, B5 and B8 are the consumers. Nothing enforces `requires_manager` yet — that is
A2's rank check, and B4/B5 wire the two together.
