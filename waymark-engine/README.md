# waymark-engine

Almanac. Python, runs nightly in the Algerian cloud against the per-tenant
DuckDB file. It never participates in a live transaction.

## The rule that shapes everything here

**The cloud fits. The store compares. No formula is implemented twice.**

This project fits. The store-side evaluator may read local data and
cloud-supplied parameters, compare values, do arithmetic on a couple of
quantities and do date arithmetic. It may not fit models, aggregate over
history, iterate, or optimise. If a formula would otherwise be written twice,
it belongs here and the store reads the parameter.

## What every output must carry

- **Its interval.** A number without a range is a bug.
- **A Because block** — at most three reasons, each with a figure.
- **Its computed-at age.** Stale output is shown as stale, never hidden.
- **A parameter registry version.** Parameters come from the registry with a
  `version` and a `computed_at`.

And nothing decides automatically. Every recommendation is accepted, adjusted
or dismissed by a human.

## Status

Phase 0: directory reserved. The engine speaks in Phase 2. The methods
themselves were worked through in `/learning` during the learning stage.
