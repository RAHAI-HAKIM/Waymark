# Waymark

A retail management system for Algerian SMB retailers, with an analytical
engine called **Almanac**. Store-side software runs on a single Windows 10 till
at Basic tier.

The differentiator is **interpretability**. Every recommendation must be
explainable to the shopkeeper — which figure, over which window, with which
range. Code that cannot be explained does not ship.

> Formerly *QRetail*. The rename came with the brand identity work in
> August 2026; the learning-stage history under `/learning` predates it.

---

## Repository layout

| Path | Contents |
| :---- | :---- |
| `src/` | The .NET solution — nine projects that ship, the synthetic store generator, five test projects |
| `waymark-admin/` | React + TypeScript Admin. One codebase, two surfaces (local and cloud) |
| `waymark-engine/` | Almanac. Python, runs nightly in the cloud |
| `docs/` | Architecture, build plan, operating rules, diagrams, decision log |
| `learning/` | Stage 1 learning work — forecasting, statistical learning, OR modules |
| `tools/` | Build and development scripts |

### The solution

```
                POS · StoreServer · Admin API      hosts
                              ↓
                        Application                use cases
                              ↓
                          Domain                   rules, entities
                              ↑
     Persistence · Hardware · Sync · Pseudonymisation      infrastructure
```

Dependencies point inward. `Waymark.Domain` has zero dependencies.
Infrastructure implements interfaces *declared in* Domain, which is why the
arrow from it points up.

The reference graph is the architecture, and it is compiler-enforced. Adding a
project reference is an architecture change — ask first, then record it in
`docs/decisions.md`.

Two absences are load-bearing:

- **`Waymark.Sync` never references `Waymark.Pseudonymisation`.** That missing
  edge is a legal boundary. What reaches the outbox is already pseudonymised;
  sync is structurally incapable of reaching the tenant key. Asserted in
  `src/tests/Waymark.Integration.Tests/ArchitectureTests.cs`, which must *fail*
  when a forbidden reference is added.
- **The POS never opens the store database.** It talks HTTP to StoreServer even
  at Basic tier where both run on one machine. One code path, not two. Asserted by
  `ArchitectureTests.Pos_cannot_reach_the_store_database`, which walks the
  project graph.

---

## Getting started

```bash
dotnet build src/Waymark.sln
```

```bash
dotnet test src/Waymark.sln
```

Requires the .NET 10 SDK (current LTS). Verified against 10.0.400: fifteen
projects, zero warnings, all tests green. Node and the Python engine environment
arrive with Phases 1 and 2 respectively.

---

## Before writing code

Read **`CLAUDE.md`** at the repository root. It is short and it is the
authority on layering, money, identity, store scoping and the privacy
boundary. The reasoning behind each rule lives in `/docs`.

Five rules are worth repeating here, because each is silently wrong when
broken — the code compiles, the tests pass, and the damage appears months
later:

1. **Money is never a float.** Monetary columns are `INTEGER`, in minor units,
   never `REAL` and never `TEXT`. C# side is `Money`, which carries its
   currency and refuses to round without being told how.
2. **A stock level and a stock change are different types.** `Quantity` and
   `QuantityDelta`. `Quantity + Quantity` does not compile, on purpose.
3. **Every primary key is a ULID generated in application code**, through the
   `IIdGenerator` port. No database autoincrement, anywhere, so offline
   terminals cannot collide — and the synthetic generator stays deterministic.
4. **The tenant key never leaves the premises.** It is what makes a pseudonym,
   and it is in no backup and no sync payload.
5. **Every engine figure carries its interval, its Because block and its
   computed-at age.** A number without a range is a bug.

---

## Documents

| Document | Authority on |
| :---- | :---- |
| `docs/status.md` | Where the work stands, known flaws, what is next |
| `docs/Waymark_Operating_Rules.md` | Commercial, legal, privacy. **Wins over everything** |
| `docs/decisions.md` | Every non-obvious choice, and why |
| `docs/System_Architecture.md` | Module design |
| `docs/Waymark_Implementation.md` | How the software is built |
| `docs/sync-design.md` | Sync, intents, erasure |
| `docs/Waymark_Build_Plan.md` | Phase contents and definitions of done |
| `docs/schema-changes.md` | How to change the schema without breaking it |
| `docs/diagrams/` | The eight architecture diagrams |

Start at **`docs/README.md`** — it says which document answers which question,
which wins when two disagree, and where a new piece of writing belongs.

---

## Status

**Phase 0 — Foundation: build complete** (15/09/2026). No demo. What remains before
Phase 0.5 is a final test and the resolution of the open findings in `docs/status.md`.
The phase recap is `docs/recaps/phase-0.md`; phase definitions are in
`docs/Waymark_Build_Plan.md`.

The synthetic store generator makes a deterministic year of a fake épicerie for
development, testing and the engine; see `src/Waymark.Generator/README.md`.
