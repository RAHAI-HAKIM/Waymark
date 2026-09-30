# Status

Where the work stands, what is wrong, and what comes next. Rewrite this file as work
lands; it is the only document that is allowed to go stale in a week. Last pass:
**27/09/2026**: **block A is done and reviewed** (§9); **B2 and B1 are done** (§10, §11); **B3 is done** (§12):
weighed goods typed or read from a scale label, a label's price exact (D-090). **B4 is done** (§13): a
discount given at the counter, rank 2, a manager's PIN for a cashier (D-091). **B5 is done** (§14): a
price typed at the counter, rank 3, within a band (D-092). Phase 0.5's recap is `recaps/phase-0.5.md`. The plan is `phase-1-plan.md`.

---

## 1. Snapshot

| | |
| :---- | :---- |
| Phase | **1, the till runs a shop: opening 22/09/2026.** Phase 0.5 closed 21/09/2026; Phase 0 closed 17/09/2026 |
| Build | `dotnet build src/Waymark.sln`, 16 projects, **0 warnings**. No vulnerable package. **Stop StoreServer before building**: a running host holds `src/Waymark.StoreServer/bin` and the copy fails with `MSB3021`, which reads like a code error and is not one |
| Tests | **1864**, all green in Debug and Release (Integration 652 · Pos 528 · Domain 341 · Generator 237 · Hardware 65 · Application 41). **Pos includes the till's window**, headless (D-086). **Off Windows**, 5 `StoreCalendarTests` fail by design (D-067) and the StoreServer, keys-directory and DPAPI tests return without running |
| Schema | 61 tables (all STRICT), 88 indexes, 29 triggers, **8 migrations**: `WeighedGoods` (B3, `quantity_source`, `scale_label_format`) and `OverridesAndDiscountReasons` (B5: `list_price` and the override's reason and authoriser on `transaction_items`; the ticket discount's reason, authoriser and note on `transactions`; a discount note on `transaction_items`) |
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
| F-27 | Low | **Two B3 pieces of the G1 board are not built**: the "Poids / PLU · saisie manuelle" key (F2) beside the field, and the rail's "Articles sans code-barres" grid (Tomates 180,00 /kg, Œufs 25,00 /u… and "Nouvel article"). B3 sells them by PLU typed in the field | G1 board, `Pos/Ui/TillViews.cs` | **Hakim brings the design**: what F2 does beyond focusing the field, which products the grid shows and in what order, and what "Nouvel article" is (O-25) |
| F-25 | Low | **The Almanac card's "1 / 3" takes a touch only on its 12 px figures**, the defect D-084 fixed for keys. It is a label that acts, not a key | `Pos/Ui/TillViews.cs`, `Almanac` | **Decide** at the next rail design: a key, or a larger target |
| F-18 | Low | **Admin's colour tokens have drifted from the design system.** `waymark-admin/src/index.css` has ink `#1a1a1f`, muted `#5c5c66`, critical `#a4243b`, warning `#b4690e`; the design system has `#14101F`, `#6B6478`, critical `#C03F44`/`#93292F`, warning `#BA8823`/`#7C580A`. The till's brushes already match the design system | `waymark-admin/src/index.css` | **Fix** with block E, from the design system's tokens |
| F-15 | Low | **One unexplained integration failure.** On 14/09 a full-solution `dotnet test`, run straight after a build, failed one integration test, and the name was not captured. Every run since has been green. **New lead, 22/09:** a stale test assembly can do exactly this. Restoring a source file with `mv` (or any copy that keeps the original mtime) leaves it older than the built DLL, MSBuild skips the project, and `dotnet test` runs the *previous* code — a failure with no matching source. Cost an hour in A1 | `Waymark.Integration.Tests` **29/09: a second suspect.** `StoreServerStartupTests.A_generated_store_is_imported_served_and_reopened_after_a_restart` failed in two full runs (28/09 Debug, after 3 min; 29/09 Release) and passed alone every time (36–48 s): a real process under the whole suite's load, likely its startup deadline. **30/09:** its sibling `It_refuses_a_store_whose_currency_disagrees_with_its_configuration` failed once in a full Debug run and passed alone. The same day a full Release run failed 440 integration tests at 1 ms each, and Pos reported nothing; both projects were green on the rerun, and the error was not captured | **Watch**: if it recurs, capture the test name (`--logger "console;verbosity=detailed"`) before anything else, and check the DLL is newer than the source |

Phase 0.5's own findings were all closed inside the phase; `recaps/phase-0.5.md` §5 lists
them and what settled each.

## 4. Open questions

The full text is in `decisions.md`, "Open — waiting on Hakim".

| # | Question | Disposition |
| :---- | :---- | :---- |
| O-23 | *How* is statistics tier 2 (local DuckDB) encrypted, and with what key? *Whether* is settled: it is (D-065) | DuckDB's encryption is not SQLCipher. Decide with the real tier-2 writer in Phase 2. The DPIA states the gap meanwhile (§5.4) |
| O-25 | A product created at the till, tentative until the owner confirms it in Admin | Schema change. Replaces D-081's Divers when decided; after E1 |
| O-28 | Does a recommendation have an Adjust answer (design system) or not (D-074)? | Ajuster is shown unavailable until decided |
| O-29 | Should a sign-in end when the till is idle, and a lockout survive a restart? Both are memory today (D-083) | Nothing in Phase 1's flow. **Before a pilot** |
| O-31 | May a terminal that is not `active` sign in and sell? Today a retired till can | An access rule. **H2**, before a pilot |
| O-32 | Does an archived product stop its variants selling? Today only the variant's status is read | A D-066 rule. **E1** |
| O-33 | Is the person deciding a card at the till the session's person? Today the till sends `staff_id` and the server believes it | CLAUDE.md §3.10 against D-083's gap. The till's half is an hour's work |

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

**≈47 sessions of 4 h (A5 added 23/09), one or two a day, every day: 24–47 days.**

| Block | What | Sessions | State |
| :---- | :---- | :--: | :---- |
| **A** | The floor: O-24 and promotional prices, sessions and PIN and permissions, reason codes, the till shell, sign-in | 5 | **Done 24/09, reviewed 25/09** (§9) |
| **B** | Checkout depth: search, quantity, weighted, discounts, override, split tender, on-account, voids, refunds, paid-in/out | 10 | **B1–B5 done** (§10–§14) |
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

**Nobody can sign in until somebody has a PIN** (A5, D-083): every generated person has the
unusable `synthetic:no-login`. Set one for seed-42's first cashier with StoreServer stopped; it
asks twice, without echo, and exits:

```bash
dotnet run --project src/Waymark.StoreServer --no-launch-profile -- "--Waymark:Storage:DataDirectory=$PWD/artifacts/server/data" "--Waymark:Storage:KeysDirectory=$PWD/artifacts/server/keys" --Waymark:Store:StoreId=01JCWEQNC0W9W3YV7F0CPNDDC9 --Waymark:Store:Currency=DZD --set-pin=01JCWEQNC0W9W3YV7F0CPNDDCB
```

Then start StoreServer as above, and the till in a second terminal. It finds StoreServer at
`http://localhost:5290/` by default and needs its terminal id (seed-42's till); who sells is
whoever signs in:

```bash
dotnet run --project src/Waymark.Pos -- --terminal=01JCWEQNC0W9W3YV7F0CPNDDCD
```

In a Debug build the till has a **Simulate scan** box that feeds a code through the real
scanner. `2000000000015` is a milk at 143.00 DZD with stock; `2000000000039` is one with
none.

The till's window has its own tests, headless, with the rest of the suite (`TillWindowTests`,
D-086): no server and no display needed.

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

## 9. Block A — done 24/09, reviewed 25/09

Each session's reasoning is its decision; the files are where to start reading. ✍ marks Hakim's
pieces, each with its rules in its doc comment and its tests beside it.

| Session | What it did | Decision | Start at |
| :---- | :---- | :---- | :---- |
| A1 | The TVA rate from the product's categories, 19% marked `StandardFallback` when they are silent or disagree; a promotional price beats a retail one | D-075, D-076 | ✍ `Domain/Catalogue/TvaRate.cs`, `Persistence/Catalogue/ProductLookup.cs` |
| A2 | Who may do what, from `roles.rank`; a synthetic PIN never signs in | D-077 | ✍ `Domain/Organisation/StaffPermissions.cs`, ✍ `StaffPin.IsUsable` |
| A3 | Reason codes read as data: active, ordered, what the foreign key accepts | D-079 | `Persistence/Reference/ReasonCodes.cs`, `GET /api/reason-codes` |
| A4 | The till's shell to G1: a tested screen model, one palette file, French and Arabic | D-080–D-082 | `Pos/Screen/TillScreen.cs`, `Pos/Ui/TillPalette.cs`, `Pos/TillWindow.cs` |
| A5 | Sign-in: Argon2id in StoreServer, a session token that names the seller, five wrong PINs lock five minutes, `--set-pin` | D-083 | ✍ `StaffPin.IsWellFormed`, ✍ `SignInLockout.cs`, ✍ `StoreServer/Security/Argon2PinHasher.cs`, `TillSessions.cs`, `Pos/Checkout/SignInFlow.cs` |
| Review | The window keeps the cashier's place and the scanner's focus; an unconfirmed sale closes Encaisser; the window is tested in CI | D-084–D-086 | `Pos/Screen/CartFollow.cs`, `TillSession.Unconfirmed`, `Pos.Tests/TillWindowTests.cs` |

Every session was broken on purpose (D-012), each mutation caught by its own tests.

**Carried out of block A, and where each goes:**

- `StaffPermissions.May` has no caller yet, and there is no reason picker on the till: **B4, B5, B8**.
- The − / + stepper under a line and parked tickets as tabs (asked at the G1 review): **done in B2**.
- Clock-in and "Pointer sans ouvrir la caisse": **B10**. Admin sign-in: **I1**.
- Rounding moves no earlier: a weight inferred from a price is **B3** (D-090), a ticket discount
  spread with `Allocate` is **B4**, only the cash portion rounds in **B6**, counted cash is **C2**.
- seed-42 has no promotion (**E2**); listing products sold on the TVA fallback is **E**.
- Still Hakim's to confirm: the dark focus ring `#C6B6EE` (D-082), and the Arabic strings marked
  `// ar: à relire` in `Pos/Screen/TillText.cs`.

**To look at the till:** a Debug build takes `--snapshot=out.png`, with `--scan=code,...`,
`--select`, `--pay`, `--staff=<id> [--pin=digits [--open]]`, `--theme=light|dark` and `--lang=ar`;
it renders the window to a PNG and exits. `--pay` sells for real on whichever store is open.

---

## 10. Session B2 — more than one ticket (D-087)

| Where | What |
| :---- | :---- |
| `Pos/Checkout/Cart.cs` | `CartLine.LineId`; `TakesAnotherScan`, the merge rule B3–B5 extend; `SetCount`, never below one |
| `Pos/Checkout/TillSession.cs` | `Parked` and `Drafts` (in memory, the till's); `Park`, `CancelTicket`, `ResumeParked`, `ResumeDraft`, `SetCount`; a change of cashier parks the ticket |
| `Pos/Screen/TillScreen.cs` | The tabs (`TopBar.Parked`), the stepper (`LineActions`), the rail's `OperationKey`s and `Rail.Drafts` |
| `Pos/Screen/QuantityEntry.cs` | A count typed between − and +: 1 to 9 999, digits 0 to 9, Entrée confirms |
| `Pos/Ui/TillViews.cs` | The tab strip (scrolls when full), the stepper, the operation tiles, the drafts panel, built from the G1 kit |

**The tests:** `TillHoldTests` (on hold, drafts, the date boundary in the till's zone, a change of
cashier), `CartTests` and `TillScreenTests` (the B2 sections), and three `TillWindowTests` through
the real keys: F3 and a tab, the stepper, Annuler → Brouillons → Reprendre. **Broken on purpose**,
eleven mutations, each caught by its own tests.

**For Hakim's review:** the drafts panel has no board; it is built from the kit and is yours to
correct. The Arabic words are `// ar: à relire`. **To look at it:** `--scan=code,code,|,code`
parks at each `|`, `--cancel` cancels the ticket, `--drafts` opens the list.

**Carried out:** the "QTÉ × n" multiplier and opening an old ticket are **B1**; what a cancellation
leaves behind, and the manager PIN on "Annuler ticket", are **B8**; the rail's other keys arrive
with their sessions.

---

## 11. Session B1 — done 26/09 (D-088, D-089)

**✍ Hakim's piece:** `Persistence/Sales/PastTickets.cs`, the rules in its doc comment, its 20 tests
in `PastTicketsTests`. Money is merged in memory: a converted column cannot be grouped in SQL.

| Where | What |
| :---- | :---- |
| `Domain/Catalogue/NameSearch.cs` | Every word typed starts a word of the name; accents and case folded; whole words first; 2 characters, 20 results |
| `Persistence/Catalogue/ProductLookup.cs` | Barcode, then PLU; `SearchAsync` answers each result as a scan would; `no_code` |
| `StoreServer/Sales/TicketsWire.cs` | Who may see (today at one's till, else rank 2), the store's day as instants, the wire |
| `GET /api/products/search`, `/api/tickets`, `/api/tickets/one` | The search; a day's list; one ticket, the rank asked of its own day and till |
| `Pos/Screen/FieldInput.cs` | What the field holds: a code, a name, a ticket number, or `3*` |
| `Pos/Checkout/TillSession.cs` | `NextCount`, `AddFoundAsync`, `View` / `CloseView` (nothing sold under a past ticket) |
| `Pos/Screen/TillScreen.cs`, `Ui/TillViews.cs` | The floating results, the field's chip, the Tickets panel, the past ticket read-only |

**Broken on purpose**, twelve mutations, each caught by its own tests; the ranking one was not at
first, and its test was rewritten until it was. **For Hakim's review:** the results and the Tickets
panel have no board; they are built from the kit; the Arabic words are `// ar: à relire`.

---

## 12. Session B3 — done 28/09 (D-090)

**`Domain/Sales/WeighedLine.cs`**, the O-26 arithmetic: a weight's line, a price label's quantity
worked back and rounded once by the store's policy, its price split over batches, and whether a
stored row recomputes. Written by Claude on Hakim's word (28/09) after his start; the rules are its
doc comment, its 29 tests `WeighedLineTests`.

| Where | What |
| :---- | :---- |
| `Domain/Values/RationalRounding.cs` | The one rounding implementation, lifted out of `Money.Times`, which now calls it |
| `Domain/Catalogue/ScaleLabelFormat.cs` | The mask, the presets, the EAN-13 check digit, a PLU compared without its zeros |
| `Persistence/Catalogue/ProductLookup.cs` | Barcode, then PLU, then the store's label format; a typed weight; the new refusals |
| `Application/Sales/CompleteSale.cs` | A weighed line takes its weight; a price label is split with `Allocate`; `quantity_source` on every row |
| Migration `WeighedGoods` | `transaction_items.quantity_source` (a CHECK, so the table is rebuilt: its columns come back in alphabetical order), `stores.scale_label_format` |
| `StoreServer --scale-format=` | Sets the store's format; `list` prints the presets (`Application/Organisation/SetScaleLabelFormat.cs`) |
| `Pos/Checkout/Cart.cs`, `TillSession.cs` | `LineWeight`, `AddWeighed`, `SetWeight`; `Weighing`, `ConfirmWeight`, `PreviewWeightAsync`, `Reweigh` |
| `Pos/Screen/WeightEntry.cs`, `TillScreen.cs`, `Ui/TillViews.cs` | What a typed weight is; the weight card; a weighed line's row, chip and "Poids" |

**The tests:** `ScaleLabelFormatTests`, `WeighedGoodsTests` (lookup, sale rows, the format command),
`TillWeighTests`, `WeightEntryTests`, and the B3 sections of `CartTests`, `TillScreenTests`,
`TillWindowTests`, `ProductLookupWireTests`, `SaleWireTests`. **Broken on purpose**, 29 mutations,
each caught by its own tests; a split rounded part by part survived at first, and a three-way split
test was added until it did not.

**For Hakim's review:** the weight is typed in the field, with a card where the results float,
rather than the pad agreed; the Arabic words are `// ar: à relire`. **To try it:** seed-42 has
nothing weighed. `python tools/dev-weighed/add_weighed.py <generated>/waymark-store.db` adds tomatoes
(PLU 4011, weight labels) and olives (PLU 537, price labels) before the import, and prints a label of
each to scan. An already imported store is migrated at StoreServer's next start, but has no weighed
product: import a fresh copy into a new data directory to try one.

---

## 13. Session B4 — done 29/09 (D-091)

**✍ Hakim's piece, finished by Claude on his word (29/09):** `Domain/Sales/Discounts.cs`: a line's
discount from a percent or an amount, a ticket's worked out once and split over the lines, and a
line's spread over its batch rows. The rules are its doc comment; its 18 tests are `DiscountsTests`,
broken on purpose five ways (an even split over rows survived at first, until a test of unequal rows).

| Where | What |
| :---- | :---- |
| `Application/Sales/CompleteSale.cs` | Two passes: batches and gross, then the discounts; the reason checked (D-079); `discount_total`; the basket's flag |
| `StoreServer/Security/TillSessions.cs` | `AuthoriseAsync`, `AuthorisedBy`: sign-in's PIN check and lockout, the rank through `StaffPermissions.May`, approvals per session |
| `POST /api/till/authorise`, `Contracts/Pos/Authorisation.cs` | The seller alone, or a manager's PIN; the sale's `DiscountRequest` cites the answer |
| `Pos/Checkout/Cart.cs` | `CounterDiscount`, `SetDiscount`, `SetTicketDiscount`; `Subtotal`, `DiscountTotal`, `Total`; the store's `Policy` |
| `Pos/Screen/DiscountEntry.cs`, `TillScreen.cs`, `Ui/TillViews.cs` | What a value is; the "REMISE" rows; the panel and the manager step; Sous-total and Remises |
| `Pos/TillWindow.cs` | F4 on a line, F6 on the ticket; the value typed in the field; the manager's digits to the dots, never the field |

**The tests:** `DiscountsTests`, `TillDiscountTests`, and the B4 sections of `CompleteSaleTests`,
`TillSessionsTests`, `SaleWireTests`, `TillWindowTests`. **Broken on purpose**, 16 mutations, each caught; which reason a row keeps survived at first, and a two-reason test was added until it did not. **For Hakim's review:** the panel and the
manager step have no board of their own (built from the kit and 09-pin); the manager is picked by
name; the Arabic words are `// ar: à relire`.

---

## 14. Session B5 — done 29/09 (D-092)

**✍ Hakim's piece:** `Domain/Sales/PriceOverride.cs`: whether a typed unit price may replace the
price in force, below cost said; its 16 tests are `PriceOverrideTests`. The ceiling is rounded down by
whole-number division, not by a `Rounding` policy, which has no truncation on purpose: a limit is not
a charged amount. **The manager step is compact and never scrolls** (Hakim, 29/09): a scroll viewer
rebuilt on each digit jumped back to the top; a window test holds it.

| Where | What |
| :---- | :---- |
| Migration `OverridesAndDiscountReasons` | The override on the row, the ticket discount's reason on the ticket, the notes (F-28, closed) |
| `Application/Sales/CompleteSale.cs` | The band checked again, the line sold at the new price with `list_price` kept; override reasons, and a note when a reason asks for one |
| `POST /api/till/authorise` | Now also `override_price`, rank 3; a discount's authorisation allows no override |
| `Persistence/Catalogue/ProductLookup.cs` | The oldest batch in stock's unit cost, for the below-cost warning |
| `Pos/Checkout/Cart.cs`, `TillSession.cs` | `PriceOverride`, `ChargedPrice`, `SetOverride`, `OverridePrice`; a discount's `Note` |
| `Pos/Screen/TillScreen.cs`, `Ui/TillViews.cs`, `TillWindow.cs` | "Prix" under a line; the price panel (B4's, without % and DA); "PRIX MODIFIÉ" on the line; the note step |

**The tests:** `PriceOverrideTests`, `TillOverrideTests`, and the B5 sections of `CompleteSaleTests`,
`SaleWireTests`, `TillWindowTests`. **Broken on purpose**, 11 mutations, each caught. **For Hakim's
review:** the price panel and the note step have no board (built from the kit); the Arabic words are
`// ar: à relire`.

**Three fixes, 30/09, before B6** (D-093):
- **The manager step floats** over the screen on a scrim, closed by a ✕. It no longer runs into
  the bottom bar.
- **A PIN is typed at the pace it is touched.** A second touch inside the double-tap time was
  a `DoubleTapped`, and was lost. `TillKey` now reads the touch itself.
- **The last character typed shows at once.** The scanner's silence timer could fire early
  and give up, so the character waited for the next key.

Each fix has a test that failed before the fix, and five mutations are each caught.

