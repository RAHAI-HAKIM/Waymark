# Waymark — documentation index

Which document answers which question, which wins when two disagree, and where new writing
goes.

## Authorities: these decide

| Document | Decides |
| :---- | :---- |
| `Waymark_Operating_Rules.md` | Commercial, legal, privacy. **Wins over everything** |
| `Waymark_DPIA_v1.md` | What is promised to the ANPDP. Breaking it is a legal problem, not a bug. Edited only by Hakim |
| `decisions.md` | Every non-obvious choice since 01/09. **Beats the three below on anything decided later** |
| `System_Architecture.md` | What the modules are: operations catalogue, statistics, engine departments, Integration Layer |
| `Waymark_Implementation.md` | How it is built: stack, data stores, surfaces, hardware, layout, division of work |
| `sync-design.md` | Store↔cloud and terminal↔server sync, intents, erasure, transport |
| `Waymark_Build_Plan.md` | Phase contents and definitions of done |
| `../CLAUDE.md` | The short form of every rule that reaches code. If it disagrees with a decision, the decision is right and CLAUDE.md is stale |

## Working documents: these track

| Document | Tracks |
| :---- | :---- |
| `status.md` | Where the work stands, the findings register, what is next. **Read this first each session** |
| `schema-changes.md` | How to change the schema. Read before any migration |
| `Project_Organization.md` | Hakim's stage roadmap and log |
| `diagrams/` | Eight Mermaid diagrams, indexed in `diagrams/README.md` |
| `Waymark_Brand_Identity.pptx` | Visual identity. Its locked rules are in CLAUDE.md §6 |

`DB_design_v5` (outside the repository) is the schema's ancestor, superseded by
`src/Waymark.Persistence/schema_v7_1.sql` (frozen) and `schema_current.sql` (live).

## Where new writing goes

| If it is… | It goes in |
| :---- | :---- |
| A choice with a rejected alternative | `decisions.md`, the next `D-` entry |
| A question only Hakim can answer | `decisions.md`, the next `O-` entry, saying what it blocks |
| A rule that reaches code | `CLAUDE.md`, one or two lines, pointing at its decision |
| A defect or gap found in review | `status.md` findings register |
| Progress, or what to do next | `status.md` |
| A procedure to follow later | Its own file; `schema-changes.md` is the model |
| A drawing | `diagrams/`, plus a row in its README |

## How documents change

**Edit in place to say what is true now. Git keeps the history.** Documents are not
archives: superseded text is removed, not banner-marked, and a replaced decision's entry is
rewritten to the current rule with a one-line pointer to what replaced it. The last
long-form versions are at commit `c3531bf`. Two exceptions: the DPIA and the Operating
Rules are only changed by Hakim, since they carry external commitments.

In one commit: the decision, the documents it changes, and any diagram it makes wrong. A
diagram that contradicts `decisions.md` is worse than none, because it is read as current.

## Reading order

1. `../CLAUDE.md`: the rules
2. `status.md`: where things stand
3. `diagrams/02-container.md` and `05-solution-dependencies.md`: the shape of everything
4. `decisions.md`: search it for the *why*; don't read it front to back

## Conventions

- Dates are `DD/MM/YYYY`.
- Decisions are cited by number (`D-034`, `O-18`), never by description. Numbers are never
  reused.
- Schema objects in backticks, named exactly as the schema names them:
  `stock_movements.quantity_changed`.
- Nothing is asserted about the code that a test does not enforce. Where a document claims
  a guarantee, it names the test or constraint, or says plainly that none exists yet.
