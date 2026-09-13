# CLAUDE.md — Waymark

Rules for working in this repository. Read before writing code.

These are constraints, not suggestions. Most are silently wrong if broken: the code
compiles, the tests pass, and the damage appears months later.
**If a rule here blocks a task, stop and ask. Do not work around it.**

This file is the rule; the `D-nnn` beside it is the argument, in `/docs/decisions.md`.
Follow a reference only when you need the reasoning or are about to change the rule.
`/docs/README.md` is the index and says which documents live outside this repository.

---

## 1. What this is

Retail management for Algerian SMB retailers with an analytical engine (Almanac), running
on one Windows 10 till at Basic tier. The differentiator is **interpretability**: code that
cannot be explained cannot ship.

C# / .NET throughout the store — Avalonia POS, ASP.NET Core StoreServer. React + TypeScript
+ Tailwind + Vite for Admin. Python for the engine. SQLite for operational data, stats tier
1 and the POS cache; DuckDB for tier 2; Postgres and per-tenant DuckDB in the cloud.

---

## 2. Architecture rules

### 2.1 Dependencies point inward — `docs/diagrams/05-solution-dependencies.md`

```
POS · StoreServer · Admin API → Application → Domain ← Persistence · Hardware · Sync · Pseudonymisation
```

- **`Waymark.Domain` has zero dependencies** — no project, no NuGet package. Checked in the IL *and* the deps file: an unused package is dropped from the IL (D-050).
- **`Waymark.Contracts` references nothing at all**, not even Domain. It is the wire shape shared with TypeScript and Python (D-044, D-049).
- Infrastructure implements interfaces **declared in Domain**. Domain never names SQLite.
- **`Waymark.Sync` must never reference `Waymark.Pseudonymisation`, nor touch `Waymark.Domain.Privacy`.** A legal boundary, not a style preference (D-039, D-051).
- No SQL, no EF Core types, no HTTP outside Persistence, Sync and the hosts.
- Adding a project reference is an architecture change. Ask first.

### 2.2 The POS talks HTTP to StoreServer

Even at Basic tier where both run on one machine. One code path, not two. The POS never
opens the store database directly.

### 2.3 Handlers stage. The executor commits. — D-050

A handler never saves: it stages and returns, and `CommandExecutor` seals the context and
calls `IUnitOfWork.CommitAsync` **once**, so everything commits together or not at all. Ids
and log entries come from `CommandContext`, which belongs to one unit of work and throws
once that closes. Wanting to save inside a handler means it is a second command.

---

## 3. Data rules

### 3.1 Money — D-031…D-035, D-037

- Monetary columns are `INTEGER` minor units, 1/100 of a currency unit for every currency. **Never `REAL`, never `TEXT`.**
- C# side is always `Money`. Never `double` or `float` for money, quantity, or anything summed.
- **`Money` carries its currency and throws on mismatch.** No implicit conversion, ever. Conversion is a recorded event with a rate and a date, and is presentation-only.
- **No arithmetic that can round exists as an operator.** `Times`, `Percent`, `Allocate` take an explicit policy, so grepping them enumerates every site where a centime can be created. **Never add `operator *(Money, decimal)`.**
- **Splitting a known total** → `Allocate`, largest remainder; `sum(parts) == total` always. Never round parts independently.
- **Deriving a value** → `HalfEven` or `HalfUp` from `stores.rounding_policy`, stamped on `transactions.rounding_policy` so a receipt recomputes from its own row.
- **Cash tender** → to `Currency.CashRoundingStep` (500 DZD). The tender rounds, never the invoice, and only the cash portion; the difference goes to `rounding_variance`, never `cash_sessions.variance` (D-034).
- **TVA is extracted per line from the TTC price by subtraction**: `ht = round(ttc/(1+r))`, then `tva = ttc − ht`. Never round `tva` independently (D-033). Displayed prices are TTC under Law No. 04-02.
- **Division by zero throws**, always; callers expecting zero use the nullable variant. **Absence is never zero** (D-037).
- Almanac always uses `HalfEven`, stores full precision, rounds at display only.

### 3.2 Identity — D-038

- **Every primary key is a ULID generated in application code.** No autoincrement, anywhere.
- Ids come from `IIdGenerator`, a port in Domain. **Never `Ulid.NewUlid()` in an entity constructor** — it cannot be substituted, and W10's seeded generator needs it to be.
- The `Ulid` package lives in `Waymark.Application`, never Domain.

### 3.3 Store scoping is a global query filter

Every store-scoped entity carries `store_id` and is filtered by an **EF Core global query
filter**, not a `where` clause someone might forget. It **fails closed**. Cross-tenant
leakage is DPIA risk R9.

### 3.4 Files — D-013, D-042, D-043

- **Three SQLite files, never merged**: `waymark-store.db` (SQLCipher, backed up, pseudonymised data only), the POS Level-2 cache (never backed up), and the cloud.
- **Statistics tier 2 is local**, in DuckDB. The outbox carries two streams that must never re-join: an anonymous basket record with no customer column, and a customer period record at monthly grain.
- **Keys live in `%ProgramData%\Waymark\keys`, never in `data`.** The backup set is an allowlist of directories.

### 3.5 Pseudonymisation — D-039, D-042, D-045, D-051

**`HMAC-SHA256(tenant_key, "waymark:<population>:v1:" ‖ id)`**, truncated to 128 bits,
Crockford base32 — 26 characters, like a ULID.

- **Only `Waymark.Pseudonymisation` holds the key or computes a pseudonym.**
- **The key is never in a backup or a sync payload.** A test searches raw bytes, hex and base64 — and **the wrapped blob too**, or it passes while the DPAPI blob ships.
- **The scheme does not rotate**, and `TenantKeyStore` has no method that replaces a key. A replaced key gives every customer a second identity and orphans the cloud history, with no error.
- **`Pseudonym` is the only way to name a subject in `processing_log`.** Internal constructor; Domain grants `InternalsVisibleTo` to `Waymark.Pseudonymisation` alone. Application may hold one, may not make one.
- **Holding `IPseudonymiser` is a capability** — half of re-identification. Only Application and the hosts may. Inject it into as little as possible.
- **DPAPI at `LocalMachine` scope does not defend against a local user.** The keys directory's ACL does, and nothing sets it yet (O-18). Do not describe the wrapping as more than it is.
- **Erasure unlinks rather than destroys**: null the pseudonym on cloud transaction rows, record it in `erasure_ledger`. General rule: **order writes so failure leaves garbage, not a gap.**

### 3.6 Domain row and outbox row go in one transaction

Both or neither. This is the outbox pattern and the whole sync design rests on it.

### 3.7 Schema and migrations — `docs/schema-changes.md`, D-019, D-022

- **`MigrateAndApplyTriggers()` is the only supported way to bring a database up to date.** `Migrate()` alone leaves it with no append-only guards, silently.
- **Never run `dotnet ef migrations add` without reading the generated file.**
- **Every index, CHECK and foreign key must be declared in the EF model.** A table rebuild recreates the table from the model alone and drops whatever the model does not know.
- **Triggers live in `triggers.sql`, never in the EF model** — EF cannot see them and a rebuild drops them. Re-applied idempotently after every `Migrate()`.
- **A rebuild copies the whole table.** On `transactions`, `transaction_items`, `stock_movements` or `processing_log` that is an operational event, not a routine update.
- New tables must be `STRICT`. `schema_v7_1.sql` is frozen; `schema_current.sql` is regenerated and committed after every migration. `Migrate()` must not run inside a transaction.
- EF Core 10 on .NET 10 LTS. Not 11 — it is STS, and the till needs long support.

### 3.8 Quantity — D-036

- **`Quantity`** is a level or magnitude; **`QuantityDelta`** is a signed change.
- `Quantity + QuantityDelta → Quantity`; `Quantity − Quantity → QuantityDelta`. **`Quantity + Quantity` deliberately does not exist.**
- Zero is a legal `QuantityDelta` though not a legal movement; the non-zero rule belongs to the column's CHECK.
- **Both carry their unit** and mismatched units throw. A unit's `decimal_places` is enforced at construction.
- A stock level going negative is not a bug.

---

## 4. Privacy — `Waymark_DPIA_v1`, D-045

Breaking one of these is a legal problem, not a bug.

- **No direct identifier crosses to the cloud.** Pseudonymisation happens *before* the outbox; what lands there is already tier-2 shaped.
- **`processing_log` is written at every access site**, through `IProcessingLog.Record`, which takes a `ProcessingEvent` and nothing else. Writes belong in `Waymark.Application`.
- **The caller does not set `log_id`, `occurred_at` or `store_id`** — they come from `IIdGenerator`, the injected `TimeProvider` and `ICurrentStore`.
- **`objection_flag` is checked before any customer-directed output.**
- **Consent is two separate consents**, processing and marketing, each with its own timestamp, notice version, staff member and method. `consent_events` is append-only; withdrawal is a new event, never an update.
- **Sensitive categories are excluded from customer-level processing**, evaluated across *all* category links.
- **Nothing decides automatically.** Every recommendation is accepted, adjusted or dismissed by a human.

---

## 5. Engine — `System_Architecture`

**The cloud fits. The store compares. No formula is implemented twice.**

The store-side evaluator **may** read local data and cloud parameters, compare values, and
do arithmetic on a couple of quantities. It **may not** fit models, aggregate over history,
iterate, or optimise.

- The engine never participates in a live transaction. It ran last night.
- **Every figure carries its interval.** A number without a range is a bug.
- **Every recommendation carries a Because block** — at most three reasons, each a figure.
- **Every output carries its computed-at age.** Stale output is shown as stale.

---

## 6. Presentation — `Waymark_Brand_Identity`

- **Violet `#5A3AA8` is the operator** — every button, action, confirmation, active state.
- **Cyan `#0E8C86` is Almanac** — edges, fills, interval caps. **Never a button, link, text or card fill.** Cyan text is `#0A5F5B`; engine cards are white with a cyan top edge.
- **Label before colour.** A coloured edge with no label is not a valid state.
- **Two semantic colours only**, critical and warning. **There is no positive state** — a shelf that is fine gets no card.
- Archivo for text, IBM Plex Mono for labels, SKUs, quantities and figures.
- Voice: *"Suggested reorder: 240 units"*, never *"Reorder 240 units"*. Admit the range.

---

## 7. Working style

- **Tasks are small and scoped.** If one seems to need an architecture decision, stop and ask.
- **Every non-obvious choice gets an entry in `/docs/decisions.md`** — what, why, what was rejected. One paragraph.
- **Nothing merges that cannot be explained** to a sceptical judge asking why the safety-stock formula uses that z-score. "Roughly understood" fails.
- **Needs Hakim's decision, not a best guess**: schema and migrations · the tier 1→2 boundary and the pseudonym scheme · sync conflict rules · the recommendation envelope and Integration Layer contract · engine method selection, cold-start fallbacks, intervals · anything the DPIA promises. Money and stock arithmetic are settled (D-031…D-037); changing one is a new decision.

---

## 8. Testing

Tests exist where errors are **silent** — the code runs, the screen looks right, the number
is wrong. **Priority:** money arithmetic · stock movements and reconciliation · expiry and
markdown · the pseudonymisation boundary · sync idempotency and replay · consent and rights
logic. **Low priority:** UI rendering, CRUD screens, styling.

- **Prove a test can fail.** Break the code, watch the right test fail, restore. A mutation the *compiler* catches proves nothing about the test.
- Domain tests need no database. Integration tests run against a real temporary SQLite file, never in-memory.
- Architecture tests (NetArchTest) enforce §2 and must fail when a forbidden reference is added.
- The synthetic store generator is deterministic from a seed. Tests depend on that.
- Plain xUnit `Assert`, no assertion library (D-047).

---

## 9. Conventions

- Database naming `snake_case`, PascalCase in EF configuration. xUnit for tests. Current LTS .NET.
- **Central package management. Never `dotnet add package`** — edit `Directory.Packages.props` and the csproj by hand; the tool has damaged both before.
- Frontend: React + TypeScript + Tailwind + Vite, Radix, TanStack Table/Query, react-hook-form + zod, Recharts.
- **Admin is one codebase, two surfaces.** Cloud Admin hides every screen showing a customer name, phone or email.
- **RTL via CSS logical properties from the first component.** Never retrofitted.
- Monorepo: `/src`, `/waymark-admin`, `/waymark-engine`, `/docs`.

---

## 10. Reference documents

| Document | Authority on |
| :---- | :---- |
| `Waymark_Operating_Rules` | Commercial, legal, privacy. **Wins over everything** |
| `Waymark_DPIA_v1` | What is promised to the regulator |
| `System_Architecture` | Module and data design |
| `Waymark_Implementation` | How the software is built |
| `Waymark_Build_Plan` | Phase contents and definitions of done |
| `/docs/decisions.md` | Every non-obvious choice, and what was rejected |
| `/docs/phase-0-plan.md` | What is left in Phase 0, and what blocks each piece |
| `/docs/schema-changes.md` | How to change the schema without breaking it |
| `/docs/diagrams` | The eight architecture diagrams |
