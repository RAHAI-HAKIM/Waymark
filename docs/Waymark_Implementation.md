# Waymark — Implementation

How the software is built: constraints, stack, data stores, surfaces, hardware, layout.
Owner: Hakim. Opened 01/09/2026, compacted 13/09/2026. Module design is in
`System_Architecture.md`, sync in `sync-design.md`, phases in `Waymark_Build_Plan.md`, and
every later choice in `decisions.md`.

---

## 0. Constraints

| | |
| :---- | :---- |
| Time | 3–4 h/day, about 20 h/week, less in term time |
| Pitch | February 2027: the demo is whatever phase last completed |
| Target hardware | Recent Windows 10 tills, assume 4 GB RAM; at Basic tier one machine runs POS, StoreServer and the database |
| Team | Solo, with Claude Code as implementation assistant |

Observed Algerian retailers run recent Windows 10, which removed the Windows 7 constraint
and with it the case for Electron.

## 1. Division of work

The split is by **reversibility and silence**, not difficulty.

- **Hakim** writes or specifies precisely and reviews line by line: the schema and every
  migration; the tier 1→2 boundary and pseudonym scheme; money and stock arithmetic; sync
  rules; the recommendation envelope and Integration Layer contract; engine method
  selection, cold-start fallbacks and intervals; anything the DPIA promises.
- **Claude Code** writes and Hakim reviews: UI and CRUD screens, the synthetic generator,
  test scaffolding, migration boilerplate after a schema decision, refactors, glue, build
  setup.
- **Shared**: architecture choices, library selection, debugging. Hakim decides; Claude
  argues both sides.

Tasks stay small ("implement the batch expiry query against this schema", never "build
inventory"). Nothing merges that cannot be explained to a sceptical judge.

## 2. Stack

| Surface | Choice | Why | Rejected |
| :---- | :---- | :---- | :---- |
| Engine | **Python**: statsforecast, OR-Tools CP-SAT, Polars, DuckDB, statsmodels | The Stage 1 work; no other ecosystem carries it | — |
| StoreServer and POS | **C# / .NET 10** | One decision, since both share one machine at Basic tier. First-class `System.IO.Ports`; self-contained publish (no runtime install, in-person onboarding); 40–70 MB against Electron's 150–250 MB; shared types | Python (packaging on every till, forever), Go (split toolchain on one box), Rust |
| POS UI | **Avalonia 11.3** (D-010) | Cross-platform, active, better Arabic RTL | WPF |
| Admin, local and cloud | **React + TypeScript**, Tailwind, Vite, Radix, TanStack Table/Query, react-hook-form + zod, Recharts | Claude Code writes React best; the ecosystem covers every need; headless components, because the Almanac card spec would fight a styled library | Svelte (thin table and chart ecosystem), Vue, Angular, Blazor (multi-MB runtime on phones over poor links) |
| Cloud API | Deferred to Phase 4 | — | — |

**Performance framing.** A busy supérette does 800–2 000 sales a day, about 3 a minute.
Throughput is not a constraint anywhere in the POS. The real constraints are scan-to-screen
under ~100 ms, footprint on 4 GB, cold start, and no pause while scanning. Performance is a
genuine problem only in the engine: incremental recompute, avoiding full-history rescans.

## 3. Data stores

| Layer | Store side | Cloud side |
| :---- | :---- | :---- |
| Operational + statistics tier 1 | **SQLite** `waymark-store.db`, SQLCipher-encrypted (D-015, D-042) | Postgres: tenants, subscriptions, sync state |
| Statistics tier 2 (pseudonymised, transaction grain) | **DuckDB**, local (D-043) | — |
| Statistics tier 3 | — | **DuckDB**, one file per tenant |
| POS Level-2 cache | SQLite, own file and schema, never backed up | — |
| Keys | `%ProgramData%\Waymark\keys` (D-042) | Never |

- **SQLite for operations**: an embedded file with no service, port or account, and
  nothing a shopkeeper can stop. A backup is a file copy. WAL throughput is thousands of
  writes a second against a need of ~3 a minute. **Rejected:** Postgres or SQL Server
  Express at the store (a service on a till); LiteDB (no SQL); DuckDB for operations
  (built for scans, not row updates). Weak typing is handled by STRICT tables (D-015).
- **DuckDB for statistics**: ~4–5 M line rows a year per store is small data, and
  single-writer is a non-issue at one file per tenant. **Rejected:** ClickHouse (a server,
  and its strength is cross-tenant scanning, which privacy forbids); TimescaleDB;
  Parquet + Polars alone.
- **Money** is `INTEGER` minor units mapped to `Money` (D-031). Never `REAL` or `TEXT`.

## 4. Admin: one codebase, two surfaces

Served only by StoreServer, Admin would vanish whenever a Basic-tier shop's till is off.

- **Local Admin**: the store LAN, backed by StoreServer. Full capability, including
  customer and staff identifiers, consent and data-subject requests.
- **Cloud Admin**: anywhere, a **PWA** on phones, backed by the cloud. Dashboard,
  statistics, department reports, recommendations, catalogue, suppliers, POs.
  Accept/adjust/dismiss queues an **intent** (`sync-design.md` §6).
- **Absent remotely**: any screen showing a customer name, phone or email. It is removed by
  a build-time surface flag, not hidden by a role check. It is the pseudonymisation boundary, and
  it is a pitch line: *check your shop from your phone; your customers' details never
  leave it.*

Cloud Admin cannot work offline. That is acceptable, because the POS is what must never
stop.

## 5. POS hardware reality

| Device | How it actually works |
| :---- | :---- |
| Receipt printer | A USB or serial endpoint speaking **ESC/POS**. A byte stream, no driver, no dialog |
| Cash drawer | Wired to the *printer* (RJ11), opened by a pulse command (`ESC p`). Drawer control is printer control |
| Barcode scanner | HID keyboard wedge: types the digits, then Enter. That is why the POS is keyboard-first |
| Scale | Manufacturer-specific serial protocols (Toledo, CAS, Dibal). One integration at a time, after the cohort hardware survey |

Development runs against the fake printer, which writes the real byte stream to a file
(D-052).

## 6. Sequencing principles

- **Each phase ends demo-able; nothing is shown mid-phase.** Nothing designed is deleted:
  the schema and contracts ship whole, even for dark features.
- **The POS is a floor; the engine is a dial.** A shop cannot run without refunds, voids
  or end-of-day reconciliation, and a founding customer keeping a notebook beside the till
  ruins the data window. POS categories A, B, D, F and G are incompressible. Engine
  departments each degrade to a heuristic independently, and that is where flexibility
  lives.
- **Statistics is the hidden cost:** each statistic is compute + cache + incremental update
  + chart + placement. Deferred departments don't need theirs.
- **Built early regardless** (retrofitting is the expensive kind of work): the full schema;
  the envelope with `binary` and `menu`; both Integration Layer halves with role gating
  and the pending queue; `processing_log` and `consent_events` writes at every access
  site; the tier 1→2 boundary; store scoping; decimal discipline; migrations; the
  synthetic generator.
- **Founding customers** become possible after Phase 3 and comfortable after Phase 4.

## 7. Solution layout

The reference graph is compiler-enforced architecture. It is drawn in
`diagrams/05-solution-dependencies.md` and enforced by `ArchitectureTests`
(CLAUDE.md §2.1).

| Project | Holds |
| :---- | :---- |
| `Waymark.Domain` | Entities, value objects, ports. **Zero dependencies** |
| `Waymark.Application` | Commands and handlers, `processing_log` writes, id generation |
| `Waymark.Contracts` | Wire shapes shared with TypeScript and Python. References nothing |
| `Waymark.Persistence` | EF Core, SQLite, migrations, unit of work |
| `Waymark.Hardware` | ESC/POS, drawer, scanner, later scales |
| `Waymark.Pseudonymisation` | The tenant key and the pseudonym scheme |
| `Waymark.Sync` | Outbox drain, inbox application, preconditions |
| `Waymark.StoreServer` | ASP.NET Core host: owns the DB, serves Local Admin and the POS API, runs sync and jobs |
| `Waymark.Pos` | Avalonia host: UI plus hardware, talks HTTP to StoreServer **even at Basic tier** (one code path; localhost costs under 1 ms) |
| Tests | `Domain.Tests`, `Application.Tests`, `Hardware.Tests`, `Integration.Tests` (includes the architecture rules, D-005) |

Outside the solution: `waymark-admin`, `waymark-engine`, and later `waymark-cloud`.

**Recorded risk.** Nine projects is a lot for one person. The three that clearly earn
their separation are Domain (testability), Pseudonymisation (legal defensibility) and
Hardware (develop with nothing plugged in). Others may merge if time pressure bites.

## 8. Skills gaps and resources

The gaps: application architecture; production database work; frontend (the largest time
sink); offline sync; auth and PIN gating; packaging with a restore a non-technical user can
run; testing silent errors; ops hygiene; security implementation. Learn on contact.
References: *Architecture Patterns with Python* ch. 1–7 (cosmicpython.com); *DDIA* ch. 5
and 7; Milan Jovanović (EF Core, minimal APIs, auth) and Nick Chapsas (C#, testing) by
topic, never front to back.
