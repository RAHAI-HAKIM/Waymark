# Waymark — documentation index

Which document answers which question, which one wins when two disagree, and where a new
piece of writing belongs. Start here rather than guessing.

---

## The documents

### Authorities — these decide things

| Document | Decides | Owner |
| :---- | :---- | :---- |
| `Waymark_Operating_Rules.md` | Commercial, legal and privacy. **Wins over everything** | Hakim |
| `System_Architecture.md` | Module and data design — what the parts are and what they hold | Hakim |
| `Waymark_Implementation.md` | How the software is built — stack, layout, sequencing | Hakim |
| `Waymark_Build_Plan.md` | Phase contents and definitions of done | Hakim |
| `decisions.md` | Every non-obvious choice, why, and what was rejected | Both |
| `../CLAUDE.md` | The short form of every rule that reaches code | Hakim |

**Precedence.** `Waymark_Operating_Rules` beats everything. `System_Architecture` beats
`Waymark_Implementation` on what the modules are; `Waymark_Implementation` beats it on how
they are built. `decisions.md` beats all of them on anything decided *after* they were
written — which by now is most of the data layer.

`CLAUDE.md` is not a fifth authority. It is the short form, and every rule in it points at
the decision that produced it. If the two disagree, the decision is right and `CLAUDE.md`
is stale.

### Working documents — these track things

| Document | Tracks |
| :---- | :---- |
| `phase-0-plan.md` | What is left in Phase 0, in build order, with what blocks each piece |
| `schema-changes.md` | How to change the schema without breaking it. Read before any migration |
| `Project_Organization.md` | The roadmap and Hakim's running log |
| `diagrams/` | Eight architecture diagrams, as Mermaid. `diagrams/README.md` indexes them |

### Held outside the repository

Referenced by name in the documents above, but not files here:

| Document | Why it matters |
| :---- | :---- |
| `Waymark_DPIA_v1` | What is promised to the ANPDP. Breaking one of its commitments is a legal problem, not a bug |
| `Waymark_Sync_Design` | The authority behind diagrams 4, 7 and 8, and every sync window |
| `Waymark_Brand_Identity` | Present as a `.pptx`. The seven locked lines are summarised in `CLAUDE.md` §6 |
| `DB_design_v5` | The schema's ancestor. Superseded in the repository by `src/Waymark.Persistence/schema_v7_1.sql`, which is frozen |

**`Waymark_DPIA_v1` currently needs amending.** Its §5.2 describes a separately-stored
mapping table; decision D-039 removed it. Nothing in the repository will remind anyone
about this, which is why it is written down here.

---

## Where a new piece of writing goes

| If it is… | It goes in |
| :---- | :---- |
| A choice with a rejected alternative | `decisions.md`, as the next `D-` entry |
| A question only Hakim can answer | `decisions.md`, as the next `O-` entry, saying what it blocks |
| A rule that reaches code | `CLAUDE.md`, one or two lines, pointing at its decision |
| A procedure somebody will follow later | Its own file — `schema-changes.md` is the model |
| Progress, or what to do next | `phase-0-plan.md` |
| A drawing | `diagrams/`, plus a row in `diagrams/README.md` |

If it does not fit any row, it is probably a decision that has not been written down yet.

---

## How a document changes

**Superseded in place, dated, with the old text kept and marked.** Never silently
rewritten. The reasoning behind a rejected decision is the most useful thing in these
files — it is what stops the same idea being re-proposed in six months — so it stays, with
a banner saying what replaced it and when.

The pattern, as used through `Waymark_Implementation` §9.7 and §9.9:

```markdown
> **SUPERSEDED 11/09/2026 by decisions.md D-039.** What is true now, in two or three
> sentences, and which parts of the text below still stand.
```

Three things go together in one commit: the decision entry, the documents it changes, and
any diagram it makes wrong. A diagram that contradicts `decisions.md` is worse than no
diagram, because it is read as current.

---

## Reading order, if you are new to this

1. `../README.md` — what the repository is and how to build it
2. `../CLAUDE.md` — the rules, in about ten minutes
3. `diagrams/02-container.md` — the store/cloud split, which is the shape of everything
4. `diagrams/05-solution-dependencies.md` — the nine projects and what may reference what
5. `phase-0-plan.md` — where the work actually stands
6. `decisions.md` — not front to back. Search it when you want to know *why*

---

## Conventions

- **Dates are `DD/MM/YYYY`**, and every superseding note carries one.
- **Decisions are referenced by number** — `D-034`, `O-2` — never by description.
- **Schema objects are in backticks** and named exactly as the schema names them:
  `stock_movements.quantity_changed`, not "the stock movement quantity".
- **Nothing is asserted about the code that a test does not enforce.** Where a document
  claims a guarantee, it names the test or the constraint that produces it.
