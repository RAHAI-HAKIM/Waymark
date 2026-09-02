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
| `src/` | The .NET solution — nine projects, three test projects |
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
  sync is structurally incapable of reaching the mapping.
- **The POS never opens the store database.** It talks HTTP to StoreServer even
  at Basic tier where both run on one machine. One code path, not two.

Both are asserted in `src/tests/Waymark.Integration.Tests/ArchitectureTests.cs`
and must *fail* when a forbidden reference is added.

---

## Getting started

```bash
dotnet build src/Waymark.sln
```

```bash
dotnet test src/Waymark.sln
```

Requires the .NET 10 SDK (current LTS). Node and the Python engine environment
arrive with Phases 1 and 2 respectively.

---

## Before writing code

Read **`CLAUDE.md`** at the repository root. It is short and it is the
authority on layering, money, identity, store scoping and the privacy
boundary. The reasoning behind each rule lives in `/docs`.

Four rules are worth repeating here, because each is silently wrong when
broken — the code compiles, the tests pass, and the damage appears months
later:

1. **Money is never a float.** Monetary columns are `TEXT` or `INTEGER`, never
   `REAL`. C# side is `decimal`, wrapped in `Money`.
2. **Every primary key is a ULID generated in application code.** No database
   autoincrement, anywhere, so offline terminals cannot collide.
3. **Mapping row first, commit, then the customer row.** Order writes so
   failure leaves garbage, not a gap.
4. **Every engine figure carries its interval, its Because block and its
   computed-at age.** A number without a range is a bug.

---

## Documents

| Document | Authority on |
| :---- | :---- |
| `docs/Waymark_Operating_Rules.md` | Commercial, legal, privacy. **Wins over everything** |
| `docs/System_Architecture.md` | Module and data design |
| `docs/Waymark_Implementation.md` | How the software is built |
| `docs/Waymark_Build_Plan.md` | Phase contents and definitions of done |
| `docs/Project_Organization.md` | The roadmap and its running log |
| `docs/decisions.md` | Every non-obvious choice, and why |
| `docs/diagrams/` | The eight architecture diagrams |

---

## Status

**Phase 0 — Foundation.** No demo. Repository, solution layout, schema,
migrations, value objects, the pseudonymisation boundary, the synthetic store
generator. Everything after this assumes it exists.

Progress and phase definitions: `docs/Waymark_Build_Plan.md`.
