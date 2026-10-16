# Status

Where the work stands, what is wrong, and what comes next. Rewrite this file as work lands; it is
the only document allowed to go stale in a week. Last pass: **16/10/2026**.

**Phase 1, Blocks A to D are done and reviewed.** The till searches, holds and cancels tickets,
sells by weight, discounts, overrides a price, splits a payment, keeps customers and a tab, refunds,
spends store credit, moves cash and clocks people in (A, B); it sells only in an open cash session
and reads its X and Z, whole and by cashier (C); it prints its receipts, opens its drawer, records a
no-sale and reprints a duplicate, on the fake printer (D). **Next is Block E** (catalogue, the first
Admin batch), behind design gate G2. Block K, what needs real hardware, waits for the devices.

Phase 1's decisions and what each review found are in `recaps/phase-1.md` (till Block D). **How
the code is laid out and how a feature crosses the projects** is `recaps/implementation-workflow.md`.
The plan is `phase-1-plan.md`.

---

## 1. Snapshot

| | |
| :---- | :---- |
| Phase | **1, the till runs a shop**, opened 22/09/2026. Phase 0.5 closed 21/09, Phase 0 on 17/09 (`recaps/`) |
| Build | `dotnet build src/Waymark.sln`, 16 projects, **0 warnings**, no vulnerable package. Build and test one node at a time (`-m:1`) on a machine short of memory. **A running StoreServer locks its `bin/Debug`**: stop it, or build and test with `-c Release` |
| Tests | **2764**, all green (Integration 900 · Pos 781 · Domain 712 · Generator 238 · Hardware 83 · Application 50), last full run 16/10 in Release. Pos includes the till's window, headless (D-086). Off Windows, 5 `StoreCalendarTests` fail by design (D-067) |
| Schema | 63 tables (all STRICT), 92 indexes, 38 triggers, **19 migrations**. The last four: `NoSaleAlertCount` (D-126), `ReceiptFooter` (D-125), `TaxRegimeNoSaleAndReprints` (D-122, D-123), `BlindCountAndClosedSession` (D-119, D-120). Each one's decision says what it changed; `schema_current.sql` is the whole schema |
| Encryption | `waymark-store.db` is SQLCipher-encrypted by StoreServer, which imports a plaintext store rather than open it (D-056). The generator's output is plaintext by design |
| Admin | `waymark-admin`: Node 24.19.0 LTS, 0 vulnerabilities; `tsc --noEmit` and `vite build` clean |

---

## 2. Findings register

Each item is **fix** (Claude, no decision needed) or **decide** (Hakim, often with an `O-` entry).
Close an item by deleting its row.

| # | Sev. | Finding | Where | Action |
| :---- | :---- | :---- | :---- | :---- |
| F-30 | **Med** | **Lowering a count after "Encaisser" asks nobody and leaves no trace.** D-106 covers a strike; five units made one is the same theft | `Cart.SetCount`, `CompleteSale` | **Decide** with O-34 |
| F-42 | **Med** | **No test holds "outstanding debt never reaches the outbox"** (D-043, D-055). True by construction today | the outbox writers | **Fix**: a test over every outbox payload of a day with tabs |
| F-31 | Low | **A sale costs about 50 ms a line**: each query opens its own connection (pooling is off, §3.4) | `CompleteSaleHandler`, `ProductLookup` | **Fix** before a pilot: one connection held for a command, then measure |
| F-32 | Low | **Refusals with no code still show the server's English** behind "Refusé par le serveur" (about 35, the ones the till's screens prevent) | `Application/Sales` | **Fix** as each is met |
| F-33 | Low | **Every Arabic string added since the board is `// ar: à relire`** | `Pos/Screen/TillText.cs` | **Hakim reads** the Arabic |
| F-34 | Low | **A restock asked for an expired batch is refused silently**: the quote does not say which lines will not go back on the shelf | `RefundSale.cs`, `RefundAnswer` | **Fix** at the next refund change |
| F-37 | Low | **A refused paid-out still bounds the drawer for somebody counting blind** (`drawer_short`). The count itself is no longer an oracle (D-119) | `RecordCashMovementHandler` | **Decide** before a pilot |
| F-50 | Low | **A row can be written against a closed session in the database** (no code does; its Z would say it no longer reconciles). Triggers that read another table break SQLite rebuilds (D-120) | `triggers.sql` | **Decide** (schema mechanics); deferred by Hakim 12/10 |
| F-52 | Low | **Two reads still pass a list of ids as SQL variables** (32 766 at most): the expiry evaluator's batches and a day's past tickets | `ExpiryLedger`, `PastTickets` | **Fix** when touched: a subquery |
| F-53 | Low | **A sale refused as `no_open_session` does not bring the opening panel** | `TillWindow` | **Fix**: ask the drawer again on that refusal |
| F-54 | Low | **A PIN typed to read a report stands at the server until midnight** for anyone at that till who cites it | `TillSessions.AuthorisedBy` | **Decide** with O-33 |
| F-41 | Low | **A line's own discount and its share of a ticket discount are one figure** on its row (D-112); the total is exact | `transaction_items` | **Decide** whether the split matters (a rebuild) |
| F-44 | Low | **One's own column beside the session's gives a second person's away** by subtraction when two worked | `ShiftReportEndpoints.Cashiers` | **Decide** |
| F-45 | Low | **Somebody no longer active is named "?" in a past Z** | `ShiftReportEndpoints.AnswerAsync` | **Fix** with H1 |
| F-43 | Low | **No test holds "`processing_log` is not read by analytics"**; no analytics code exists yet | `ArchitectureTests` | **Fix** with the first tier-2 writer |
| F-40 | Low | **No key takes cash to the safe or tops the float up** (`drop`, `float_add`, `float_remove` exist) | `CashMovements.cs` | **Decide** before a pilot; "Petite caisse" covers both meanwhile |
| F-27 | Low | **Two B3 pieces of the G1 board are not built**: the F2 "Poids / PLU" key and the "Articles sans code-barres" grid | `Pos/Ui/TillViews.cs` | **Hakim brings the design** (O-25) |
| F-25 | Low | **The Almanac card's "1 / 3" takes a touch only on its figures** | `TillViews.Almanac` | **Decide** at the next rail design |
| F-18 | Low | **Admin's colour tokens have drifted from the design system** | `waymark-admin/src/index.css` | **Fix** with block E |
| F-15 | Low | **Rare unexplained integration failures under the full suite**, never reproduced alone. Two leads: a stale test assembly (a file restored with its old mtime), and StoreServer startup tests under load | `Waymark.Integration.Tests` | **Watch**: capture the name (`--logger "console;verbosity=detailed"`) and check the DLL is newer than the source |

## 3. Open questions

The full text is in `decisions.md`, "Open — waiting on Hakim".

| # | Question | Disposition |
| :---- | :---- | :---- |
| O-38 | **High priority.** Are the Z's flags and figures by cashier in the DPIA, and is reading them logged? | The DPIA is Hakim's. **Before G7** |
| O-23 | How is statistics tier 2 (DuckDB) encrypted, and with what key? | Phase 2, with the real tier-2 writer |
| O-25 | A product created at the till, tentative until confirmed in Admin | Schema change; after E1 |
| O-28 | Does a recommendation have an Adjust answer? | Ajuster shown unavailable until decided |
| O-29 | Should a sign-in end when idle, and a lockout survive a restart? | **Before a pilot** |
| O-31 | May a terminal that is not `active` sign in and sell? | **H2**, before a pilot |
| O-32 | Does an archived product stop its variants selling? | **E1** |
| O-33 | Is the person deciding a card the session's person? | The till's half is an hour's work |
| O-34 | Does lowering a count after "Encaisser" need the cancel's PIN, and is it recorded? | **Before a pilot** (F-30) |
| O-35 | What of suppliers does Phase 1 build? | **Before F1 opens** |

## 4. Tests carried into Phase 1

None blocks the flow; take each when its code is next changed. Key custody under pressure (two
processes creating a key at once; a truncated key file; a blob from another machine). Executor
edges (cancellation between staging and commit; a throw after staging a log entry). Converters (a
non-UTC offset; two rows in one second; `DateOnly` at the year's edges; a nullable `Money` zero
against null). Architecture (only the host names the key stores; only Persistence opens the store's
`SqliteConnection`). A generated store is checked with `python tools/verify-store/verify_store.py <run>`.

---

## 5. Phase 1: where it stands

The plan, its pace and its gates are `phase-1-plan.md`; the Build Plan stays the *what*.

| Block | What | State |
| :---- | :---- | :---- |
| **A** | The floor: prices, sessions, PIN and permissions, reason codes, the till shell, sign-in | Done 24/09, reviewed 25/09 |
| **B** | Checkout depth: search, weight, discounts, override, split tender, tab, voids, refunds, cash in/out | Done 02/10, reviewed 03/10 |
| **C** | Shift: counted float and close, X and Z, the report by cashier | Done 11/10, reviewed 12/10 |
| **D** | Receipts and the drawer on the fake printer: content, printing, no-sale, reprint | Done 15/10, reviewed 16/10 |
| **E** | Catalogue, first Admin batch: CRUD, bulk price, CSV import | **Next**; design gate **G2** first |
| **F** | Stock: receiving, adjustments, counts | Suppliers to plan first (O-35) |
| **G** | Customers and compliance: consents, objection, loyalty, rights, `processing_log` | Design gate **G3**; likely to grow |
| **H** | Staff and store; the Z in Admin and the consumer's facture (H3) | |
| **I** | Platform: Admin over the LAN, offline, backup and restore, the recovery code, the nightly evaluator | |
| **J** | The done-when: the simulated day, the consent-to-erasure walkthrough, the restore drill | |
| **K** | What needs the device (D-124): real printer and drawer, Arabic receipts, scanner, scale | When the hardware is there |

Where a feature's code is: follow its decision's names, or the nine places of
`recaps/implementation-workflow.md` §2. ✍ in a file's doc comment marks a piece Hakim wrote.

**Waiting on Hakim:**
- **O-38** (high priority), then F-37 and F-54 with O-33, F-30 with O-34, F-40, F-41, F-44.
- **Block D on a real screen**: the bar's `BarWarning` colours (`TillPalette`); migration
  `NoSaleAlertCount` to read; Entrée on a past ticket is still "Fermer", not the board's Rembourser.
- **Where C3 left the board**: columns in the order of first operation; the narrow layout's strip of
  reports; F-44.
- The Arabic marked `// ar: à relire` (F-33); the narrow layout on a real 1024 × 768 screen; the dark
  focus ring `#C6B6EE` (D-082).
- **An accountant**, before a pilot: the IFU ceiling and the facture's mentions (D-122).

---

## 6. Running it

Generate a store, import it into StoreServer (paths must be absolute: `dotnet run` starts in the
project directory), set a PIN, then start the till. From the repository root, in Git Bash:

```bash
dotnet run --project src/Waymark.Generator -- --config src/Waymark.Generator/inputs/configs/grocery-dz.json --out artifacts/generated/seed-42
dotnet run --project src/Waymark.StoreServer --no-launch-profile -- --urls=http://localhost:5290 "--Waymark:Storage:DataDirectory=$PWD/artifacts/server/data" "--Waymark:Storage:KeysDirectory=$PWD/artifacts/server/keys" "--Waymark:Storage:ImportPlaintextFrom=$PWD/artifacts/generated/seed-42/waymark-store.db" --Waymark:Store:StoreId=01JCWEQNC0W9W3YV7F0CPNDDC9 --Waymark:Store:Currency=DZD
```

Once imported, drop `ImportPlaintextFrom`. **Nobody can sign in until somebody has a PIN**: with
StoreServer stopped, add `--set-pin=01JCWEQNC0W9W3YV7F0CPNDDCB` (seed-42's first cashier; the owner is
`…DDCA`) to the same command; it asks twice and exits. The synthetic store has two cashiers, an
owner and no manager. Then the till:

```bash
dotnet run --project src/Waymark.Pos -c Release -- --terminal=01JCWEQNC0W9W3YV7F0CPNDDCD
```

**Maintenance switches**, each run on StoreServer's command line, sets its value and exits (until
Admin, block H):

| Switch | Sets |
| :---- | :---- |
| `--tax-regime=subject\|not-subject` | Whether the store charges TVA (D-122). seed-42 is subject; a store left alone is not |
| `--receipt-footer="Merci !\|Ouvert 7j/7"` | The store's text on its receipts; `\|` breaks the line (D-125) |
| `--scale-format=` | How scale labels are read (D-090) |
| `--customer-module`, `--max-credit-limit`, `--credit-overdue-days`, `--tab-as-part`, `--credit-expiry-days` | The tenant's customers and tab (D-096, D-101) |
| `--refund-min-rank`, `--paid-out-min-rank`, `--close-session-min-rank`, `--x-report-min-rank`, `--blind-close`, `--variance-alert-value`, `--void-alert-count`, `--void-alert-value`, `--no-sale-alert-count` | Ranks and thresholds (D-110 to D-112, D-126); `off` clears one |
| `--refund-window-days=7\|off`, `--receipt-language=fr\|ar` | The refund window (D-125); the receipts' language (D-124) |
| `--publish-information-notice`, `--notice-language` | The customers' information notice (B7) |

**The till's printer is a file** until K: `%LOCALAPPDATA%\Waymark\printer\receipts.escpos` with a
readable `receipts.txt` beside it (`--printer=<folder>`, `--paper=58`, `--no-cutter`). A Debug build
has a **Simulate scan** box, and takes `--snapshot=out.png` with `--scan=…`, `--pay`, `--staff=… --pin=…
--open`, `--lang=ar`, `--theme=dark` to draw the window to a PNG and exit.

The expiry evaluator: `POST http://localhost:5290/api/engine/expiry` with `{}`; running it twice is
safe. Local Admin: `cd waymark-admin && npm install && npm run dev` (Node 24 LTS; a terminal opened
before the install will not find `npm`). The generator's guide is `src/Waymark.Generator/README.md`.

---

## 7. Division of labour

`phase-1-plan.md` §3 is the full table.

| | Hakim | Claude |
| :---- | :---- | :---- |
| Money and stock arithmetic | **Writes** refund and void rules, discount allocation, tender, reconciliation | The code around it, the wire, the screens, the failing tests it drops into |
| Privacy | **Writes** consent, objection, rights, anything touching `consent_events` or `processing_log` | The CRUD, the forms, the rendering |
| Access | **Writes** PIN gating and permissions | Sessions, endpoints, plumbing |
| Schema | Decides it, reads every generated `Up()` | Generates, verifies, regenerates `schema_current.sql` |
| Design | **Brings the board** before a screen is built | Reviews it against CLAUDE.md §6, records where our rules won, builds |
