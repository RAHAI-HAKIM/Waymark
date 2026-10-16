# How the till is built: the implementation workflow (Phase 1, till Block D)

For a developer joining the code. CLAUDE.md gives the rules and `decisions.md` the reasons; this
file gives the **shape**: which project holds what, how a feature crosses them, and the patterns that
repeat because the till grew to thirty-odd endpoints and a hundred screens' worth of states. It is a
snapshot of 16/10/2026; where it and the code disagree, the code is right.

---

## 1. The projects, and which way they point

```
Waymark.Pos (Avalonia till) ──HTTP──▶ Waymark.StoreServer (ASP.NET Core host)
        │                                     │
        ▼                                     ▼
Waymark.Contracts (wire records)      Waymark.Application (commands, handlers)
        ▲                                     │
        │                                     ▼
        └──────────────── both ────── Waymark.Domain (rules, entities, ports) ◀── Waymark.Persistence (EF Core, SQLite)
                                              ▲
                                       Waymark.Hardware (ESC/POS, sinks)
```

| Project | Holds | Never |
| :---- | :---- | :---- |
| `Domain` | Entities (plain classes, `init` properties), value types (`Money`, `Quantity`), **pure rules as static classes** (`Drawer`, `Refunds`, `TvaRate`, `ShiftReport`, `StaffPermissions`), and **ports**: the interfaces Persistence implements (`ISalesLedger`, `ICashSessionLedger`, `IReceipts`…) | A package, a database type, the clock (`TimeProvider` is injected) |
| `Persistence` | `WaymarkDbContext`, one `XConfiguration` per entity (`Configurations/`), converters, the port implementations (`Sales/`, `Organisation/`…), migrations, `triggers.sql`, `schema_current.sql` | Business rules: it reads and stages, the rules are Domain's |
| `Application` | One command record and its handler per action (`RecordNoSale` → `RecordNoSaleHandler`), `CommandExecutor`, the refusal exceptions | HTTP, SQL |
| `Contracts` | The wire: request and answer records, outcome strings, refusal codes, capability names | A reference to anything, Domain included (D-044) |
| `StoreServer` | `Program.cs` (DI, startup checks, maintenance switches, most endpoints), `XEndpoints` extension classes, **`XWire` static classes** (pure mapping and host-side rules), `TillSessions` | Rules a test could not reach without a server |
| `Pos` | `StoreServerClient` (`ITillServer`), `TillSession` (the cart and the ticket's state), the **pure screen models** (`Screen/`), the views (`Ui/`), `TillWindow` (the orchestration), `Receipts/` (layout, printer) | The database: it talks HTTP only (CLAUDE.md §2.2) |
| `Hardware` | `ReceiptDocument`'s encoder (`EscPosEncoder`), the sinks (`FileEscPosSink`), `EscPosPrinter` (printer and drawer, one device) | Knowing what a receipt says: the till lays it out |

The architecture tests (`ArchitectureTests`) fail when an arrow above is broken.

---

## 2. A feature from the rule to the screen

Every till feature since Block B crossed the same nine places, bottom up. The no-sale (D-126) as the
example:

| # | Where | What it was for the no-sale |
| :-- | :---- | :---- |
| 1 | **Domain, the rule** | `ShiftReport.NoSaleFlagsOf`: pure, tested with no database (`ShiftReportNoSaleTests`) |
| 2 | **Domain, the entity and the port** | `NoSaleOpening` (`IStoreScoped`); `ICashSessionLedger.NoSalesAsync` |
| 3 | **Persistence** | `NoSaleOpeningConfiguration` (columns, indexes, FKs: everything a rebuild must keep); `DbSet` in the context; the three append-only guards in `triggers.sql`; `CashSessionLedger.NoSalesAsync` |
| 4 | **Migration** | `dotnet ef migrations add …`, read, `schema_current.sql` regenerated (§5) |
| 5 | **Application** | `RecordNoSale` (record) and `RecordNoSaleHandler`: checks, stages one row, returns; refusals thrown with a code |
| 6 | **Contracts** | `NoSaleRequest`, `NoSaleAnswer`, `RefusalCodes.NoSaleNeedsManager`, `Capabilities.NoSale`; on the report, `NoSalesWire` |
| 7 | **StoreServer** | `POST /api/cash/no-sale` in `Program.cs`; the handler registered (`AddScoped`); the report's mapping in `ShiftReportEndpoints.ToWire` |
| 8 | **Pos** | `StoreServerClient.NoSaleAsync` (and its default on `ITillServer`); `CustomerPanelKind.NoSale` and its panel in `Screen/Customers.cs`; the text in `TillText` (French and Arabic); `TillWindow.SendNoSaleAsync`, F9 in the key handler |
| 9 | **Tests at each level** | Domain rule; integration on a real database (`CompleteSaleTests.NoSale.cs`); the window, headless, against a fake server (`TillWindowTests.Printing.cs`) |

A read follows the same path without step 5: a port, its implementation, an endpoint, a wire record.

---

## 3. The patterns that repeat

### 3.1 Wire records (Contracts)
- `sealed record` with **every property named in snake_case** by `[property: JsonPropertyName("…")]`:
  TypeScript (Admin) and Python (engine) read the same JSON.
- **Amounts and quantities are invariant decimal text**, "1836.56", "0.742": written by
  `WireText.Figure` (StoreServer), read by `WireFigures.Money` / `Quantity` (Pos) and
  `WireText.TryHundredths` (server). Never a float on the wire.
- An answer carries an **`Outcome` string** from a static class (`TicketOutcomes`,
  `CashMovementOutcomes`…): `found`, `refused`, `pin_required`, `not_signed_in`. A refusal adds
  `Refusal(code, args)`. **A null answer at the till means the server could not be reached**, never
  a refusal.
- New parameters go **at the end, with a default**: older callers and older tests keep compiling.
- `ContractsMirrorTheSchemaTests` lists every record that mirrors a table; a new one is added there
  with a one-line reason, or that test fails.

### 3.2 `XWire` classes and endpoints (StoreServer)
A rule that lives at the host (who may see a ticket, what a refund window asks) is a **pure static
method** in an `XWire` class (`TicketsWire.MaySee`, `RefundWire.AsksOwner`, `ReceiptsWire.FirstName`,
`ShiftReportEndpoints.WhoReads`), so it is tested without a server. An endpoint is then thin, always
in this order:

```
token = request header "X-Waymark-Session"  → sessions.Resolve(token) or not_signed_in
rank  = staff.StaffAsync(session.StaffId).Rank
mayAlone = StaffPermissions.May(rank, Capability.X [, tenant's raised rank])
cited    = sessions.AuthorisedBy(token, request.Authorisation, Capability.X)
if !mayAlone && cited is null → pin_required with its RefusalCode
await oneSaleAtATime  (anything that writes beside sales)
executor.ExecuteAsync(handler, command) → answer via XWire mapping
catch the handler's refusal exception → refused, SaleWire.Coded(code, args)
```

Who acts is **always the session's person**, never a field the till sends (CLAUDE.md §3.10).
Endpoints grow in `Program.cs`; a family with many routes gets an `XEndpoints.MapX(this
WebApplication)` class (`CashSessionEndpoints`, `ShiftReportEndpoints`, `CustomerEndpoints`).

### 3.3 Commands and handlers (Application)
`record X(…) : ICommand<TResult>` and `XHandler : ICommandHandler<X, TResult>`. The handler **stages
and returns**; `CommandExecutor` commits once or discards everything (D-050). Ids come from
`context.NewId()`, never `Ulid.NewUlid()`. A refusal is an exception carrying a `RefusalCodes` code
and its args (`SaleRefusedException`, `CashMovementRefusedException`, `TenantSettingRefusedException`):
nothing is staged. **Something that must outlive a refused command is a second command** run first
(`KeepBlindCount` before the close, D-119). The handler re-checks what the endpoint checked, so a
caller that is not the till meets the same rule.

### 3.4 Domain and Persistence
- Entities have `init` properties; Persistence changes one through the tracked entry:
  `context.Entry(row).Property(r => r.X).CurrentValue = …` (`StoreSettings`, `Reprints`).
- Reads **project to Domain records** (`PastTicket`, `ReceiptFacts`, `ReportNoSale`); no EF type
  leaves Persistence. `Money` is an `INTEGER` behind a converter, so it is **summed in memory**,
  never in SQL.
- **Store scoping is automatic**: an `IStoreScoped` entity gets the global query filter; a child
  table is filtered through its parent (`ApplyParentScope`). A query never says `where store_id`.
- A list of ids passed to `Contains` becomes one SQL variable each and SQLite stops at 32 766: use
  a subquery (`IQueryable`) when the list can grow (F-52).
- Timestamps are UTC text, sortable; the store's day and hour come from `IStoreCalendar`
  (`Today`, `DayOf`), never from `DateTime.Now`.

### 3.5 Settings: a tenant key or a store column
| | A tenant key | A store column |
| :---- | :---- | :---- |
| For | What all the owner's shops share: customers and the tab, ranks, thresholds, the refund window, the receipt language | What one shop is: its tax regime, its receipt text, its scale labels |
| Lives in | `system_config` rows | a column on `stores` (a migration) |
| Domain | `TenantSettings` record: the value with its default, and `XKey` const | `Store.X` |
| Read / write | `TenantConfigurationStore`: read with **"unreadable reads as the default"**, `Put` on stage | `IStoreSettings.StageXAsync`, `StoreSettings` |
| Command | `SetTenantSettings(X, XOff)`: a value, or `Off` to clear; validated in the handler | `SetX` and `SetXHandler` |
| Until Admin | `TenantSwitch` (`--x=`, parsed with the others) | its own `XSwitch`, run from `Program.cs` before the host starts |

A threshold a past report is judged by is **frozen on the row** at the time it applies (the Z's,
D-112, D-126), so changing the key later rewrites nothing.

### 3.6 Refusals and their sentences
The server sends a **code** (`RefusalCodes.X`, with args); the till owns the sentence (D-107):
`TillText.Refusal(code, args)` in French and in Arabic, said by `RefusalText.Say`. Adding a code means
the constant, the throw, and two sentences. A refusal says nothing a blind counter must not learn.

### 3.7 The PIN step
A gated action is a `Capability` (Domain) on the ladder in `StaffPermissions` (`IsConfigurable` if a
tenant key may move it), a wire name in `Capabilities`, a line in `TillStaffWire.Capability`. The
till, met with `pin_required`, calls `AuthoriseCustomerAsync(CounterKind.X, title, summary)`: the
approvers are listed, a PIN goes to `/api/till/authorise`, and `Give` sends the action again citing
the authorisation. `TillSessions.AuthorisedBy` honours it **for that till and that store day only**
(D-105). `CapabilityOf` maps a `CounterKind` to its capability; Échap's list in the key handler
must name a new kind.

### 3.8 The till: model, view, window
- `TillSession` holds the cart and the ticket's life (paid, viewing, parked, notice). `TillWindow`
  holds the rest (panels, the drawer, printing) in fields.
- Each frame, `TillWindow.Render` builds one `ScreenState` and `TillScreen.Build` turns it into
  regions (top bar, notice, cart, rail, bottom bar, floating panels). **The screen model is pure
  and decides what shows**; `TillScreen.Compare` redraws only what changed; `TillViews` draws.
- A floating panel is a `CustomerPanelKind` with a `FormPanel` builder (`Screen/Customers.cs`,
  `Drawer.cs`); a rail panel is a `Rail.X` record; the PIN step is `Approval`. What freezes the
  ticket floats and scans are ignored under it (D-094); F-keys check `Frozen()`.
- Every word is a `TillText` member, French and Arabic (`// ar: à relire` until Hakim reads it).
  Colours exist only in `TillPalette`.
- The client: one method per endpoint in `StoreServerClient`, and the same method on `ITillServer`
  with a default (`Task.FromResult(null)`), so a test's fake implements only what it uses.

### 3.9 Paper and the drawer
The server reads what a receipt says (`IReceipts`); the till lays it out (`ReceiptLayout`, pure,
into a `ReceiptDocument`) and prints through `TillPrinter`, which wraps `EscPosPrinter` and turns a
failure into `Fault`. `DrawerKicks` says when cash moved. The order is always **row, pulse, paper**;
a print never fails what was written. Until block K the sink is a file and its `.txt` rendering.

---

## 4. Tests

| Kind | Where | How |
| :---- | :---- | :---- |
| A rule | `Waymark.Domain.Tests` | No database, no clock: the static method and its cases |
| A handler, a reader | `Waymark.Integration.Tests`, `CompleteSaleTests.<Feature>.cs` (one partial class) | A real SQLite file (`MigratedDatabaseFixture`). `new Shop(database)` makes a store, a till, a cashier, two products and an open session with unique ids; `Run(shop, (context, work) => handler, command)` executes through the real `CommandExecutor`; `Sell`, `SellAt`, `Refund`, `FixedClock`, `FixedCalendar` |
| A wire mapping | `XWireTests` (integration project, `[SupportedOSPlatform("windows")]`: StoreServer is Windows-only) | The pure `XWire` methods |
| The window | `Waymark.Pos.Tests`, `TillWindowTests.<Feature>.cs` | Headless Avalonia (`Headless.Run`); `Till.SignedIn(server => …)`; `FakeStoreServer` is a partial class, one part per feature; `till.Key`, `till.Tap`, `till.WaitFor`; `Keys(till, "…")` types slowly, while `TypeAndEnter` arrives as a **scanner's burst**; `RecordingPrinter` keeps what was printed |

- **The database is shared within a test class**: a test that changes a tenant key puts it back in
  `finally`.
- **Lists that must name every new thing**, or they fail: `ContractsMirrorTheSchemaTests` (wire
  records), `ParentScopeTests` and `StoreScopingTests` (tables), `AppendOnlyTests` and
  `AppendOnlyBypassTests` (guarded tables), the trigger counts in `TriggerApplicationTests` and
  `CommissioningTests`.
- **Proving a test can fail**: change the rule, run its test, restore. Restore by copying (a fresh
  mtime): a file put back with its old date is older than the built DLL, MSBuild skips it, and the
  run tests the previous code. A mutation the compiler rejects proves nothing.
- The full suite runs in Release, one node: `dotnet test src/Waymark.sln -c Release -m:1`.

---

## 5. A schema change

1. Hakim decides it (CLAUDE.md §7). Prefer an **added nullable column with no CHECK**: a CHECK, or
   dropping a column, makes SQLite rebuild the table, which on `transactions` is an operational event.
2. The entity property; its `XConfiguration` (column name, converter, every index, CHECK and FK).
3. `dotnet ef migrations add Name --project src/Waymark.Persistence --configuration Release`, then
   **read the generated `Up()`** before anything runs.
4. A trigger goes in `triggers.sql` (append-only tables get three: no update, no delete, no replace),
   never in the model; a trigger that reads another table breaks later rebuilds of that table (F-50).
5. Regenerate the documentation: `WAYMARK_UPDATE_SCHEMA_CURRENT=1 dotnet test …Integration.Tests
   --filter SchemaCurrent`, and read the diff.
6. Update the lists of §4, the generator if its data should show the change
   (`inputs/catalogues/grocery-dz/store.json` for reason codes), and the decision's migration line.

---

## 6. How a block runs

1. **Start** from `status.md` and the block's row in `phase-1-plan.md`; read what the base documents
   (Build Plan, System_Architecture, DPIA, Operating Rules) promise for it.
2. **Ask before building** whatever is Hakim's (schema, access, the DPIA, money rules): one question
   at a time, with a recommendation.
3. **The board** comes before any screen. It is reviewed against our rules; where a rule wins, the
   decision says so ("changed from the board").
4. **Record** the decision in `decisions.md`, in full while the block is open.
5. **Build bottom up** (§2), tests at each level, every new rule broken once.
6. **Report**: what was built, what differs from the board, what Hakim must read (migrations) and
   look at (screens). Hakim reviews and commits.
7. **The block review**: the docs against the code (decisions win over older documents), then
   extreme cases (idle, panels over panels, double presses, a server restarted under the till, day
   boundaries), each kept as a test. Then the decisions move to the recap and `decisions.md` keeps
   titles; `status.md` is trimmed to what is live.

---

## 7. Traps met on the way

- **A running StoreServer locks `bin/Debug`**: the build fails with `MSB3021`, which looks like a
  code error. Build and test with `-c Release`, `dotnet ef … --configuration Release`.
- **Low memory**: `-m:1`, or MSBuild's nodes die (`MSB4166`).
- **Analyzers are errors**: `CA1859` (return the concrete type), `CA1822`, `CA1416` (StoreServer
  code called from a test needs `[SupportedOSPlatform("windows")]`), and an `if (false)` is
  unreachable code: a mutation must stay compilable.
- **Invariant globalization** in the hosts: `string.Normalize` leaves accents as they are; the
  receipt folds French by a table (`EscPosEncoder.Fold`).
- **Fast keyboard input is a scan** to `KeyboardWedgeScanner`: a test types a word slowly.
- **Shell edits**: scripted edits with C# quotes break inline heredocs; write the script to a file and
  run it.
