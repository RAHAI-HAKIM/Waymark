# Phase recaps

One file per finished phase: what was built, what was decided, what was found, and what it
left for later. A recap is written when a phase closes, so a later phase can recover the
context without replaying the sessions.

**Not part of the start-of-session reading** (CLAUDE.md). The files are long, and the live
state is in `docs/status.md`, the rules in `CLAUDE.md` and the reasoning in
`docs/decisions.md`. Open a recap only when a question reaches back into a finished phase:
why something was built this way, what was tried, or what a phase's numbers were.

A recap is a snapshot and is not kept current. When it disagrees with `decisions.md` or
`status.md`, those are right.

| Phase | Recap | Closed |
| :---- | :---- | :---- |
| 0, Foundation | [phase-0.md](phase-0.md) | 17/09/2026 (build 15/09; §8 is the revision, the final test and the close) |
| 0.5, The walking skeleton | [phase-0.5.md](phase-0.5.md) | 21/09/2026 (built 18–20/09; §5 is what the thin cut found) |
| 1, The till runs a shop — **till Block D** | [phase-1.md](phase-1.md) | Not closed. Blocks A to D reviewed (B 03/10, C 12/10, D 16/10/2026); §5, §8 and §11 are what the reviews found |

**Not a phase:** [implementation-workflow.md](implementation-workflow.md), how the code is laid out and how a feature crosses the projects, written at the end of Block D for a developer joining. It is read, unlike the recaps, when learning the codebase.
