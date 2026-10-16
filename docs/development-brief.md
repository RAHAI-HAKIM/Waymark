# Development brief

The implementation side of Waymark, for someone meeting the project for the first time.
Written 12/10/2026 for the first incubator session. This page is a guide: it says what
exists, how it is built, and **which few documents to open and how much of each to read**.
The figures here are a snapshot; `status.md` is the live one.

---

## 1. What is being built

Retail management for Algerian small retailers, with an analytical engine (Almanac) that
suggests what to reorder, mark down or watch, and always says why. It runs on one Windows
till in the shop; the cloud receives only pseudonymised data.

| Piece | What it is | Built with |
| :---- | :---- | :---- |
| The till (POS) | What the cashier uses | C# / .NET 10, Avalonia |
| StoreServer | The shop's own server, on the same machine at the first tier | ASP.NET Core, SQLite encrypted with SQLCipher |
| Admin | The owner's back office, local and cloud | React, TypeScript |
| Almanac | The engine: the cloud fits models, the shop only compares | Python; Postgres and DuckDB in the cloud |

## 2. Where it stands

| | |
| :---- | :---- |
| Started | 01/09/2026, after 8 weeks of learning and 3 of design |
| Done | **Phase 0** (foundation, 17/09), **Phase 0.5** (one thin path from a scanned barcode to a decided recommendation, 21/09) |
| Under way | **Phase 1, "the till runs a shop"**: blocks A to D are done and reviewed (the till sells, closes its shift and prints) |
| The till today | Sells by scan, name or weight; discounts and price overrides behind a manager's PIN; split payment; customers and a credit book (le carnet); cancels and refunds; opens and closes its drawer on a count; reads its X and Z reports |
| Still to come in Phase 1 | The catalogue and Admin, stock, customer rights tooling, staff, offline and backup; the real printer and scale when the hardware is there (block K) |
| Not built yet | The cloud (a stub), the engine beyond one expiry rule, most of Admin |
| Size | 63 tables, 19 migrations, **2 764 automated tests**, all passing |
| Next milestone | The February demo is Phase 2: the engine speaks |

## 3. How it is built

One developer (Hakim) working with an AI assistant (Claude), under written rules:

- **Hakim writes the code that decides**: money and stock arithmetic, consent and rights,
  permissions. The assistant writes its tests first, then the code around it.
- **Every non-obvious choice is a numbered decision** with what was rejected (118 so far).
- **Every rule is tested, and every test is proven able to fail** by breaking the code once.
- **Each block ends with a review** against a running system, and its defects go in a public
  register rather than being fixed quietly.
- **Nothing ships that cannot be explained**: the product's promise is interpretability, and
  the same rule is applied to the code.

## 4. What to read

In this order. About an hour in all; the first three are enough for a first meeting.

| # | Document | Read | Why |
| :-- | :---- | :---- | :---- |
| 1 | `diagrams/02-container.md` | All (one drawing) | The shop/cloud split and the pieces |
| 2 | `status.md` | The opening paragraph, §1 and §5 | Where the work stands and what is next |
| 3 | `Waymark_Build_Plan.md` | The head, then each phase's first lines | What each phase delivers and when it counts as done |
| 4 | `../CLAUDE.md` | §2 to §4 | The rules the code obeys: architecture, money, privacy |
| 5 | `Waymark_Implementation.md` | §2, §3 and §7 | The stack, the data stores, the solution's layout |
| 6 | `recaps/phase-1.md` | §1, then two or three decisions of §3 or §6 | What a block produces, and how a decision is argued |
| 7 | `decisions.md` | Skim the titles; never front to back | The index of every choice. Search it for a *why* |
| 8 | `diagrams/04-data-flow.md` and `06-sequence-sale.md` | Both | Where personal data stops; what one sale does |
| 9 | `recaps/implementation-workflow.md` | §1 to §3 | How the code is laid out, and how one feature crosses the projects |

Not needed for a first reading: `phase-1-plan.md` (how the sessions are ordered),
`schema-changes.md` (a procedure), `sync-design.md` (Phase 4's design), the Phase 0 recaps.

## 5. Relations with the other sides of the project

| Side | Its document | What it gives development | What development gives back |
| :---- | :---- | :---- | :---- |
| **Legal and data protection** | `Waymark_DPIA_v1.md`, `Waymark_Operating_Rules.md` | Promises that become code: pseudonymise before anything leaves the shop, two separate consents, a logbook of every access, erasure, an age check before a customer is created | A test for each promise, named, so the DPIA can cite it. Where no test exists yet, the register says so (today: two controls) |
| **Design and brand** | `assets/waymark-design-system.html`, `Waymark_Brand_Identity.pptx` | A board for each screen, **before** the screen is built (a gate at blocks B, E and G) | A review of the board against the rules, then the screen. Where the two disagree, a decision records which won |
| **The engine (Almanac)** | `System_Architecture.md` §3 to §5 | The methods, their intervals and the "because" each recommendation carries | Clean, tested data: the sales, stock and cash ledgers the models will read, and a synthetic shop to try them on before a real one exists |
| **Business and market** | `Waymark_Operating_Rules.md`, `Project_Organization.md` | Tiers, hardware limits, what a shop needs on day one (the carnet, cash rounded to 5 DA, French and Arabic) | A demo at the end of every phase, and honest estimates: the plan's ranges are wide on purpose |

Three rules cross every boundary. The Operating Rules win over everything. The DPIA is
edited by Hakim alone, and breaking it is a legal problem, not a bug. A decision taken later
beats an older design document, and is written down the same day.

## 6. What is uncertain

- **Pace.** Phase 1 is the largest phase: about 21 of 48 sessions are done. The blocks on
  customers and compliance are the ones most likely to grow.
- **Hardware.** Receipt printers, drawers and scales are simulated so far: the till prints to a
  file in the exact bytes a printer takes. The real devices are a block of their own (K), taken
  when they are on the desk.
- **Open questions** are listed, not hidden: nine wait for a decision (`decisions.md`, last
  section), among them how much of supplier management belongs in Phase 1.
