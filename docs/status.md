# Status

Where the work stands, what is wrong, and what comes next. Rewrite this file as work
lands; it is the only document that is allowed to go stale in a week. Last pass:
**12/10/2026**: **Blocks A, B and C are done and reviewed** (§9). **Block C's final review** is
`recaps/phase-1.md` §8; what it decided is D-119 to D-121. Left from it: F-37 (reduced), F-52 to F-54, and **O-38, high priority**, and F-50 (the guards, not done).
The till searches, holds and cancels tickets, sells by weight, discounts, overrides a price, splits
a payment, keeps customers and a tab, refunds, spends store credit, moves cash and clocks people in.
**It now sells only with its drawer open**: a float counted at opening, a count at close, a Z number.
**Its X and Z reports are read at the till, whole and by cashier.** **Block D is open** (receipts and hardware): its ground is laid (D-122: a store that charges no TVA; D-123: its decisions and schema), and what needs a real device is block K (D-124). D1, the receipt from its board, is under way (D-125). Phase 1's decisions so far
are in `recaps/phase-1.md` (till Block C); Phase 0.5's recap is `recaps/phase-0.5.md`. The plan is
`phase-1-plan.md`.

---

## 1. Snapshot

| | |
| :---- | :---- |
| Phase | **1, the till runs a shop: opening 22/09/2026.** Phase 0.5 closed 21/09/2026; Phase 0 closed 17/09/2026 |
| Build | `dotnet build src/Waymark.sln`, 16 projects, **0 warnings**. No vulnerable package. On a machine short of memory build and test one node at a time (`-m:1`): MSBuild's child nodes die with `MSB4166` otherwise. **Stop StoreServer before building**: a running host holds `src/Waymark.StoreServer/bin` and the copy fails with `MSB3021`, which reads like a code error and is not one |
| Tests | **2718**, all green (Integration 885 · Pos 752 · Domain 710 · Generator 238 · Hardware 83 · Application 50); the last full run, 14/10, was in Release. **Pos includes the till's window**, headless (D-086). **Off Windows**, 5 `StoreCalendarTests` fail by design (D-067) and the StoreServer, keys-directory and DPAPI tests return without running |
| Schema | 63 tables (all STRICT), 92 indexes, 38 triggers, **18 migrations**: `ReceiptFooter` (D-125: `stores.receipt_footer`, one added column, no rebuild), `TaxRegimeNoSaleAndReprints` (D-122, D-123: `stores.tax_regime` and its CHECK, which rebuilds `stores`; `no_sale_openings`, append-only; `transactions.reprint_count`), `BlindCountAndClosedSession` (D-119, D-120: `cash_sessions.pending_count` and `pending_count_moves`; a closed row carries all of its close; one drawer that is "not closed"; rebuilds `cash_sessions`), `LogbookOperations` (D-115: art. 41 bis 3's operations, the `processor` actor, `supplier` subjects, four retention entities, `processing_log.subject_count`; rebuilds `processing_log`, `erasure_ledger` and `retention_policies`), `ConsentGivenBy` (D-113: `consent_events.given_by` and its CHECK; rebuilds `consent_events`), `CashSessionThresholds` (C2: the three thresholds frozen on `cash_sessions` at a close; three added columns, no rebuild), `CashSessionClose` (C1: `cash_sessions.closed_authorised_by` and its CHECK, one open session and one Z number per till as unique indexes; rebuilds `cash_sessions`; three triggers make a closed session final), `LinesStrikesAndTabRounding` (block B review: `transaction_items.line_number` and `removed_authorised_by` with their CHECKs; `rounding_variance` may name a tab's movement; rebuilds both tables), `SalesVoid` (B8: a cancel's `payment_opened_at` and `void_authorised_by` on `transactions`; a struck line's `removed_at`, `removed_by` on `transaction_items`; their CHECKs), `CustomersAndTab` (B7: `credit_limit_events`, append-only; the charge's override, the tab's freeze and the customer's collection notice; the `information` notice type), `WeighedGoods` (B3, `quantity_source`, `scale_label_format`) and `OverridesAndDiscountReasons` (B5: `list_price` and the override's reason and authoriser on `transaction_items`; the ticket discount's reason, authoriser and note on `transactions`; a discount note on `transaction_items`) |
| Encryption | `waymark-store.db` is SQLCipher-encrypted by StoreServer, which refuses a plaintext store and imports one instead (D-056). The generator's output is plaintext by design |
| Admin | `waymark-admin`: Node 24.19.0 LTS, 82 packages, 0 vulnerabilities. `tsc --noEmit` and `vite build` both clean |
| Recaps | `recaps/phase-0.md`, `recaps/phase-0.5.md`, and `recaps/phase-1.md` (**till Block C**: every Phase 1 decision so far in full). Read one only when a question reaches back |

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
| F-30 | **Med** | **Lowering a count after "Encaisser" asks nobody and leaves no trace.** D-106 covers a line struck; five units made one is the same theft, and a lowered count is recorded nowhere, before or after payment opened | `Pos/Checkout/Cart.cs` `SetCount`, `CompleteSale` | **Decide**: the same PIN, and the difference recorded as a struck row; or leave it |
| F-31 | Low | **A sale costs about 50 ms a line**: roughly ten queries, each opening its own connection since pooling is off (§3.4). A hundred lines took 5 s on the review machine. A sale now waits 20 s, not 3 (`StoreServerClient.SaleTimeout`), so it is no longer shown as unconfirmed | `CompleteSaleHandler`, `ProductLookup` | **Fix** before a pilot: hold one connection open for the length of a command (`Database.OpenConnection()`), then measure |
| F-32 | Low | **Refusals with no code still show the server's English** behind "Refusé par le serveur" (about 35, the ones the till's own screens prevent), and so does the unconfirmed-sale card's detail (D-107) | `Application/Sales`, `TillSession` | **Fix** as each is met; nothing a cashier reaches in ordinary work |
| F-33 | Low | **Every Arabic string added since the board is `// ar: à relire`**, the refusals' sentences (D-107) among them | `Pos/Screen/TillText.cs` | **Hakim reads** the Arabic; fix what reads wrong |
| F-34 | Low | **A restock asked for an expired batch is refused silently** (D-098): the quote does not say which lines will not go back on the shelf, so the cashier is not told | `RefundSale.cs`, `RefundAnswer` | **Fix** with D3 (the refund's receipt needs the same fact) |
| F-37 | Low | **A refusal still bounds the drawer for somebody counting blind**: a paid-out or a cash refund too large is refused as `drawer_short` and writes nothing, so each try halves what the drawer may hold. The count itself is no longer an oracle (D-119) | `RecordCashMovementHandler`, `RefundSaleHandler` | **Decide** before a pilot: accept it, or record a refused paid-out |
| F-50 | Low | **A row can still be written against a closed session in the database** (no code does; its Z would then say it no longer reconciles). The two triggers that refused it were taken out: a trigger on `cash_movements` or `transactions` that reads `cash_sessions` makes SQLite refuse every later rebuild of `cash_sessions`, forward or back (D-120) | `triggers.sql`, `MigrateAndApplyTriggers` | **Decide** (schema mechanics): drop such triggers before a migration and re-apply them after, or leave it to the Z's reconciliation |
| F-52 | Low | **Two other reads still pass a list of ids as SQL variables**, the fault the review fixed in the session's ledger (SQLite takes 32 766): the expiry evaluator's batches and a day's past tickets. Neither is near it today | `ExpiryLedger`, `PastTickets` | **Fix** when either is next touched: a subquery |
| F-53 | Low | **The till learns its drawer is shut only at sign-in, or from a close sent twice.** A sale refused as `no_open_session` says so in words and does not bring the opening panel: reachable when a close's answer is lost and the cashier leaves the panel instead of sending again | `TillWindow` | **Fix**: ask the drawer again on that refusal, whichever key met it |
| F-54 | Low | **A PIN typed to read a report stands at the server until midnight**, for anyone at that till who cites it. D-112 and D-118 say it is forgotten when the report closes, and only the till forgets (checked live: the next cashier read the flags on it). O-33's gap: the server trusts what the till sends | `TillSessions.AuthorisedBy` | **Decide** with O-33: spent when the report is read, or left as D-105 has every approval |
| F-41 | Low | **A report cannot split a line's own discount from its share of a ticket discount**: a row stores one figure and one reason, so a line discounted on a discounted ticket is counted whole with the lines (D-112). The total given away is exact. A line's own discount under the same reason as its ticket's is counted with the ticket's | `transaction_items`, `ShiftReport.ReductionsOf` | **Decide** whether the split matters: it needs a column on `transaction_items`, a rebuild of a large table |
| F-44 | Low | **One's own column beside the session's total gives a second person's figures away by subtraction** when the session has two people: a manager reads the synthesis and their own column (the board, D-118), so the other's is the difference. With three or more, nothing is learnt about any one | `ShiftReportEndpoints.Cashiers` | **Decide**: keep the board's own column, or send nobody but the owner any column |
| F-45 | Low | **Somebody the store no longer lists as active is named "?"** in a past Z, on a flag (C2) and on their column (C3): `TillDirectory.DescribeAsync` describes active staff only. Two such people in one Z cannot be told apart | `ShiftReportEndpoints.AnswerAsync` | **Fix** with H1 (staff): a name read whatever the person's status, for a report |
| F-42 | Med | **No test holds "outstanding debt never reaches the outbox"** (D-043, D-055, CLAUDE.md §3.9). Today it is true by construction, since the basket is the only thing written there and it has no customer; nothing fails if a later writer adds a balance | `Application/Sync`, the outbox writers | **Fix**: a test over every outbox payload of a day with tabs, as `TenantKeyNeverLeavesTests` does for the key |
| F-43 | Low | **No test holds "`processing_log` is not read by analytics"** (D-045, DPIA §5.5). No analytics code exists yet to read it | `ArchitectureTests` | **Fix** with the first tier-2 writer (Phase 2): an architecture rule on who may name `ProcessingLogEntry` |
| F-40 | Low | **The till cannot take cash to the safe or top the float up**: `drop`, `float_add` and `float_remove` are in the schema and in `Drawer.Expected`, and no key writes them. **Until then cash leaves between a close and the next float with no row** (counted 7 210,00, next float 0,00), and the report by cashier has no line for a drop (a generated store has 342) | `CashMovements.cs` | **Decide** whether a shop needs them before a pilot; "Petite caisse" covers both meanwhile with a reason |
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
| O-34 | Does lowering a line's count after "Encaisser" need the cancel's PIN, and is it recorded? (D-106 covers a strike only) | An access rule. **Before a pilot** (F-30) |
| O-38 | **High priority.** Are the Z's flags and the figures by cashier in the DPIA, and is reading them logged? They are figures about named staff; no read of staff data is logged today | The DPIA is Hakim's. **Before G7** |
| O-35 | What of suppliers does Phase 1 build, and what stays Phase 2's? F1 receives "against a PO" that no Phase 1 screen can make; deliveries with no PO, what is owed to a supplier and goods sent back are unplanned | Raised 07/10, left open on purpose: it needs planning. **Before F1 opens** |

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
starting point is always code already reviewed. `recaps/phase-0.5.md` §9 is the map.

**≈48 sessions of 4 h (A5 added 23/09, H3 added 12/10), one or two a day, every day: 24–48 days.**

| Block | What | Sessions | State |
| :---- | :---- | :--: | :---- |
| **A** | The floor: O-24 and promotional prices, sessions and PIN and permissions, reason codes, the till shell, sign-in | 5 | **Done 24/09, reviewed 25/09** (§9) |
| **B** | Checkout depth: search, quantity, weighted, discounts, override, split tender, on-account, voids, refunds, paid-in/out | 10 | **Done 02/10, reviewed 03/10** (§9) |
| **C** | Shift: counted float, X and Z reports, the report by cashier | 3 | **Done 11/10, reviewed 12/10** (§9) |
| **D** | Receipts and hardware: content, real ESC/POS, the drawer, reprint | 3 | |
| **E** | Catalogue, first Admin batch: CRUD, bulk price, CSV import | 4 | Needs design gate **G2** |
| **F** | Stock: receive against a PO, adjustments, counts, views | 4 | **Suppliers to plan first (O-35)**; may grow |
| **G** | Customers and compliance: consents, objection, loyalty, rights tooling, `processing_log` | 7 | Needs design gate **G3**. The block most likely to grow |
| **H** | Staff and store; the Z in Admin (H3) | 3 | |
| **I** | Platform: Admin over the LAN, offline and the Level-2 cache, backup and restore, the recovery code, the evaluator nightly | 6 | |
| **J** | The done-when: the simulated day, the consent-to-erasure walkthrough, the restore drill | 3 | |
| **K** | What needs the device on the desk (D-124): the real printer and drawer, Arabic receipts, a real scanner, the scale | 5 | Outside the order: when the hardware is there |

**Two gates before code:** design **G1** before block B (the till) and **G2**/**G3** before
blocks E and G. Hakim brings the design; Claude reviews it against CLAUDE.md §6 first.

**Hakim writes the decision-bearing core this phase** — refunds and voids, discount
allocation, tender and change, cash reconciliation, consent and rights, PIN and permissions.
The handover is **tests first**: Claude writes the failing tests and a guiding comment, Hakim
makes them green. Session D's inverted role check is why.

---

## 7. The Phase 0.5 code map

The reading guides (a scan, a sale, a basket, a batch, a card, each in the order data moves) are
in `recaps/phase-0.5.md` §9. Phase 1's own map is §9 below.

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

**The synthetic store has two cashiers, an owner and no manager.** To close the drawer or read a
report as a cashier, set the owner's PIN as well (`01JCWEQNC0W9W3YV7F0CPNDDCA`), or hand both down
with `--close-session-min-rank=1 --x-report-min-rank=1` (see F-46 before adding `--blind-close=on`).

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

## 9. Phase 1 so far: Blocks A, B and C

What each session decided and why is in `recaps/phase-1.md`; this is only where to start
reading. ✍ marks Hakim's pieces, each with its rules in its doc comment and its tests beside it.
Every rule was broken on purpose (D-012) and caught by its own tests.

| Session | What it did | Decision | Start at |
| :---- | :---- | :---- | :---- |
| A1 | TVA rate from the categories, 19 % fallback; a promotional price beats retail | D-075, D-076 | ✍ `Domain/Catalogue/TvaRate.cs`, `Persistence/Catalogue/ProductLookup.cs` |
| A2 | Who may do what, from `roles.rank` | D-077 | ✍ `Domain/Organisation/StaffPermissions.cs`, ✍ `StaffPin` |
| A3 | Reason codes read as data | D-079 | `Persistence/Reference/ReasonCodes.cs` |
| A4 | The till's shell: a tested screen model, one palette file, French and Arabic | D-080–D-082 | `Pos/Screen/TillScreen.cs`, `Pos/Ui/TillPalette.cs`, `Pos/TillWindow.cs` |
| A5 | Sign-in: Argon2id, a session that names the seller, a lockout, `--set-pin` | D-083 | ✍ `SignInLockout.cs`, `StoreServer/Security/TillSessions.cs`, `Pos/Checkout/SignInFlow.cs` |
| A review | The window keeps its place and the scanner's focus; an unconfirmed sale; headless window tests | D-084–D-086 | `Pos/Screen/CartFollow.cs`, `Pos.Tests/TillWindowTests.cs` |
| B1 | One field: scan, code or name; past tickets read-only | D-088, D-089 | `Domain/Catalogue/NameSearch.cs`, `Persistence/Sales/PastTickets.cs` |
| B2 | Tickets on hold, drafts, the count stepper | D-087 | `Pos/Checkout/Cart.cs`, `Pos/Checkout/TillSession.cs` |
| B3 | Weighed goods, scale labels | D-090 | ✍ `Domain/Sales/WeighedLine.cs`, `Domain/Catalogue/ScaleLabelFormat.cs` |
| B4 | A discount at the counter, the manager's PIN step | D-091, D-093 | ✍ `Domain/Sales/Discounts.cs`, `POST /api/till/authorise` |
| B5 | A price override within a band | D-092 | ✍ `Domain/Sales/PriceOverride.cs` |
| B6 | Split tender; the floating payment panel | D-094, D-095 | ✍ `Domain/Sales/Tender.cs`, `PaymentReference.cs`, `Pos/Screen/Payment.cs` |
| B7 | Customers and the tab | D-096, D-099, D-100 | ✍ `Domain/Customers/Tab.cs`, `Application/Customers/CustomerCommands.cs`, `Pos/Screen/Customers.cs` |
| B8 | Cancels and struck lines, recorded | D-097 | ✍ `Domain/Sales/Voids.cs`, `VoidTicketHandler` in `CompleteSale.cs` |
| B9 | Refunds; store credit spent and given back | D-098, D-101 | ✍ `Domain/Sales/Refunds.cs`, ✍ `Domain/Customers/StoreCredit.cs`, `Application/Sales/RefundSale.cs` |
| B10 | Cash in and out with no sale; the clock | D-102 | `Application/Sales/CashMovements.cs` |
| B review | Seven decisions and the design steps, below | D-103–D-109 | `Domain/Sales/SaleArithmetic.cs`, `Contracts/Pos/Refusal.cs`, `Pos/Screen/RefusalText.cs` |
| C1 | The drawer opened with a counted float and closed with a count; a tenant key sets a rank | D-110, D-111 | ✍ `Domain/Organisation/Drawer.cs`, `Application/Sales/CashSessionCommands.cs`, `Persistence/Organisation/CashSessionLedger.cs`, `StoreServer/Sales/CashSessionEndpoints.cs`, `Pos/Screen/Drawer.cs` |

**The Block B review (03/10).** Every feature was run against a live encrypted StoreServer and its
rows audited; every screen was walked headlessly; the rules were taken out one at a time. What it
found and fixed is `recaps/phase-1.md` §5. What it decided:

| | |
| :---- | :---- |
| D-103 | A line is priced once, then split over its batches |
| D-104 | `transaction_items.line_number` gives a ticket its order |
| D-105 | An approval is the till's, for the store's day |
| D-106 | A strike after "Encaisser" needs the cancel's PIN; a ticket struck empty is cancelled |
| D-107 | A refusal is a code; the sentence is the till's |
| D-108 | A tab repaid in cash rounds like a cash sale |
| D-109 | A manager's PIN opens an earlier day's ticket for a cashier |

**The till on a narrow screen** (below 1200 px, `TillTheme.Narrow`): a 400 px rail, a line's keys
on two rows, the carnet's tiles two by two, payment methods three abreast at most. At any width a
line's chips go under its name when the name needs the line, and tickets on hold are counted on
the bar.

| C2 | The X and Z reports, recomputed from the session's rows; thresholds frozen at the close | D-112 | `Domain/Organisation/ShiftReport.cs`, `Application/Sales/ShiftReports.cs`, `StoreServer/Sales/ShiftReportEndpoints.cs`, `Pos/Screen/Report.cs`, `Pos/Ui/TillViews.Report.cs` |

| C3 | The same report by cashier, behind a tab; the owner's, and one's own column to anyone else | D-118 | `ShiftReport.ByCashierOf`, `ShiftReportEndpoints.WhoReads` and `Cashiers`, `Pos/Screen/Report.cs` (`Table`), `Pos/Ui/TillViews.Report.cs` (`Cashiers`) |

| C review | A blind count kept by the server; a close that is whole; the settings pair, the clock, a note's length | D-119–D-121 | `Drawer.StandingCount`, `KeepBlindCountHandler` in `CashSessionCommands.cs`, `CashSessionConfiguration`, `SetTenantSettingsHandler`, `SaleLimits.NoteFits` |

| D1 | The receipt from its board: a sale, a refund, a duplicate, a repayment slip, at 32 and 42 columns; the store's text; the refund window | D-125 | `Persistence/Sales/Receipts.cs`, `StoreServer/Sales/ReceiptsWire.cs`, `Pos/Receipts/ReceiptLayout.cs`, `Hardware/EscPosEncoder.cs`, `Refunds.WithinWindow`, `RefundWire.AsksOwner` |

**D1 (14/10), how to read it.** A receipt, in the order data moves: `Receipts.FindAsync` reads the
ticket's rows (lines merged as the ticket view has them, the rounding from `rounding_variance`, a
refund's sale and reason); `/api/receipts/one` answers it to whoever may open the ticket, through
`ReceiptsWire` (the store's day and hour, a first name); the till's `ReceiptLayout.For` turns it
into a `ReceiptDocument`, and `EscPosEncoder` into bytes (French amounts, accents folded, Code 128).
Nothing prints by itself yet: D2 wires it after a sale. **The refund window**: the refund endpoint
asks `RefundWire.AsksOwner` before anything is worked out, and `RefundSaleHandler` refuses the same
refund again with `refund_window_passed` if it is ever sent.

**The block C review's fixes (12/10), how to read them.** A close by somebody not shown the figures:
`CashSessionEndpoints` first runs `KeepBlindCount`, which writes the count on the session with how
many times cash has moved (`CashSessionLedger.CashMovesAsync`) or answers the one that stands
(`Drawer.StandingCount`); the close is then asked with that count. The session's state sends
`standing_count` to who counts blind, and `TillWindow.OpenCloseAsync` shows it to be confirmed.

**C3 (11/10), how to read it.** `CashSessionLedger.ReportRowsAsync` now also gives who struck each
line and each cash movement with its person; `ShiftReports` adds the opening and the close as
operations with no figure and asks `ShiftReport.ByCashierOf`, then `SharesReconcile`;
`ShiftReportEndpoints.WhoReads` says who reads and `Cashiers` sends every column or the reader's
own; `ReportScreen.Table` lays the lines out and `TillViews.Cashiers` draws three stacks (labels,
people, session) so the people's scroll sideways past six. No migration. Written by Claude (the
plan gives C3 to Claude); reviewed by Hakim 12/10.

**C2 (09/10), how to read it.** A report, in the order data moves: `CashSessionLedger.ReportRowsAsync`
reads the session's tickets, lines and payments; `ShiftReports` asks `ShiftReport` for the takings,
the activity, the reductions and the flags, and `Drawer.Expected` for the drawer; `ShiftReportEndpoints`
decides who reads and whether the flags are theirs; `Pos/Screen/Report.cs` lays the sections out and
`TillViews.Report` draws them over the whole window. `ShiftReport` was written by Claude this time
(reviewed by Hakim 12/10): its rules are in its doc comment, its tests in `ShiftReportTests`.

**C1 (06/10), how to read it.** A close, in the order data moves: `Pos/Screen/Drawer.cs` builds the
three panels on the floating form's kit; `TillWindow.SendCloseAsync` sends the count;
`CashSessionEndpoints` asks `StaffPermissions` whether who counted closes alone and hides the
figures from a blind count; `CloseCashSessionHandler` reads the session's rows through
`CashSessionLedger.MovementsAsync`, asks `Drawer.Expected`, `Variance` and `NeedsNote`, and stages
the close; `triggers.sql` then refuses the row any further write. The shop's four keys are set from
the command line until H2: `--close-session-min-rank`, `--x-report-min-rank`, `--blind-close`,
`--variance-alert-value`. **Running it:** after `--set-pin`, the till asks for a float at sign-in.

**Waiting on Hakim:**

- **Where C3 left the board**: the columns are in the order of first operation, where the board
  draws the manager last; on a narrow screen the list of reports is a strip above the report, not
  the board's drop-down; the day still reads "05/10/2026". **F-44 is the one to decide.**

- **The report on a real screen**: built from the board, checked on headless frames only. It covers the whole window; its total and its keys are in its own foot, not
  in the till's bar; the tenders are in the enum's order (Avoir before Carnet); "Par caissier" is C3.
- **The C1 panels on a real screen**: built from the board and checked on headless frames only. The
  count's expected lines sit under its field, not beside the pad; "Plus…" is still the rail's keys
  and not the board's menu, so "Changer d'utilisateur" stays the staff chip's; "Rapport X" and "Voir
  le rapport Z" are shown unavailable until C2.
- **O-38, high priority**: the DPIA and the staff figures a Z shows. Then F-37 as reduced, and F-54 with O-33.
- **Migration `BlindCountAndClosedSession` to read** (D-119, D-120).
- **F-50, deferred (Hakim, 12/10)**: the guards on what hangs from a closed session wait for a decision on how triggers that read another table live through a migration. Nothing to do until then.
- **The UI pass on block C's review fixes** (the blind count that stands, the opening panel after a lost close), put off to 13/10.
- F-40, F-41, and the Arabic of the drawer's and the reports' 163 strings (F-33).
- O-34 (F-30): a count lowered after "Encaisser".
- The Arabic strings marked `// ar: à relire` in `Pos/Screen/TillText.cs` (F-33).
- The narrow layout on a real 1024 × 768 screen: it was judged from headless screenshots only.
- The dark focus ring `#C6B6EE` (D-082); the drafts panel and the weight card, built from the kit
  with no board of their own; the manager chosen by name before the PIN.

**To look at the till without a server's data in the way:** a Debug build takes
`--snapshot=out.png` with `--scan=code,code,|,code`, `--select`, `--pay`, `--cancel`, `--drafts`,
`--staff=<id> [--pin=digits [--open]]`, `--theme=light|dark` and `--lang=ar`; it draws the window
to a PNG and exits. `--pay` sells for real on whichever store is open.
