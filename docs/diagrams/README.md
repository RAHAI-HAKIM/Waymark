# Waymark — architecture diagrams

Diagrams as code. Text files, versioned with the source, rendered by GitHub and most
Markdown viewers. A diagram that contradicts the code is a bug in one or the other.

Loosely follows the **C4 model**: context (1), container (2), component (5). No code-level
diagrams — generated documentation covers that.

| # | File | Answers | Audience |
| :-- | :---- | :---- | :---- |
| 1 | `01-system-context.md` | Who touches Waymark | Pitch, onboarding |
| 2 | `02-container.md` | The store/cloud split and the deployable pieces | Everyone. The main one |
| 3 | `03-deployment.md` | What physically runs where, per tier | Support, hardware policy |
| 4 | `04-data-flow.md` | Where personal data goes and where it stops | DPIA, ANPDP |
| 5 | `05-solution-dependencies.md` | The nine projects and their allowed references | Daily implementation |
| 6 | `06-sequence-sale.md` | Completing a sale, end to end | Daily implementation |
| 7 | `07-sequence-intent.md` | A Cloud Admin decision reaching the store | Sync work |
| 8 | `08-state-lifecycles.md` | Recommendation and intent state machines | Integration Layer work |

**Sources of truth.** `System_Architecture` for module design, `Waymark_Sync_Design` for
everything in 4, 7 and 8, `Waymark_Implementation_Decisions` for 2, 3 and 5.

**Convention.** Solid arrow = calls or writes. Dotted arrow = occasional or out-of-band.
Anything crossing the store/cloud line carries no direct identifier.
