# Phase 0 — the remaining work

Working document. Rewritten 11/09/2026, after D-031…D-040 closed the money, quantity,
identity and pseudonymisation decisions.

The Build Plan says *what* Phase 0 contains and *when* it is done. This says what is left,
in the order it can actually be built, with what blocks each piece.

---

## Where it stands

**Done and verified.**

| | Evidence |
| :---- | :---- |
| Repo, solution, 9 projects, 3 test projects, CI | Builds clean, 0 warnings |
| `CLAUDE.md`, `decisions.md` (D-001…D-040), 8 diagrams | — |
| Schema: 58 tables, all STRICT, frozen as `schema_v7_1.sql` | — |
| Baseline migration reproduces it exactly | 625 columns, column order, 137 FKs, unique groups, index filters, 167 CHECKs, 102 defaults (O-9) |
| `StrictSqliteMigrationsSqlGenerator` | Plain creation and the rebuild path |
| `triggers.sql`, `MigrateAndApplyTriggers()` | 11 triggers, refuses to return if one is missing |
| `schema_current.sql`, regenerated and committed | Never executed |
| 58 entities + configurations, generated | `ModelMatchesSchemaTests` |
| Store scoping, 17 entities, fail-closed | `StoreScopingTests`, mutation-tested (D-030) |
| StoreServer starts, migrates, serves `/health` | — |
| Encryption path | SQLCipher verified end to end (D-040) |

**64 tests passing.** Uncommitted: the store-scoping work, awaiting review.

---

## The order of work

Five items are unblocked and can start today. Four are blocked on decisions that are
yours, and they are blocked on *different* decisions, so none of them blocks the others.

```
  W1 Currency + Money ─┬─ W4 the migration ─┐
  W2 Quantity ─────────┤                    ├─ W5 wire into configurations
  W3 IIdGenerator ─────┘                    │
  W6 Domain allowlist test ─────────────────┘

  W7 Pseudonymisation      ← O-2  (key custody)
  W8 Contracts             ← O-15 (recommendation envelope)
  W9 Application scaffold  ← O-16 (processing_log columns)
  W10 Synthetic generator  ← O-17 (generator spec)
  W11 Fake hardware        ← nothing; parked by choice
```

---

### W1 — `Currency` and `Money`

*Unblocked. The largest single item, and the one everything else waits on.*

**Produces.** `Currency` — `(Code, MinorUnitExponent, CashRoundingStep)` with a static
registry holding DZD, and EUR/USD defined but unused. `Money` — `(long MinorUnits,
Currency Currency)`, exact operators, `Times` / `Percent` / `Allocate` taking an explicit
policy, `Rounding.HalfEven` / `HalfUp`, the cash-step function, the TVA extraction helper.

**Tests that have to fail before they pass:**

- `Allocate` sums to the total exactly — for 3 lines of 1/3, for 7 lines of a prime, for
  negative totals, for a single line, for weights summing to zero
- the largest-remainder distribution is deterministic — same input, same order, every time
- `ht + tva == line_ttc` on every line, under both policies, over a generated sweep of
  prices and the 19% / 9% rates
- `HalfEven` and `HalfUp` differ exactly where they should, and nowhere else
- adding DZD to EUR throws; comparing them throws
- cash rounding: 1247 → 1245, 1248 → 1250, 1245 → 1245, ties away from zero, and a
  currency with step 1 rounds nothing
- division by zero throws; the nullable variant returns null and the compiler forces it
- **no `operator *(Money, decimal)` exists** — a compile-time assertion, so nobody adds one
  back by accident

**Estimate.** 8–12 h including the tests, which are most of it.

---

### W2 — `Quantity` and `QuantityDelta`

*Unblocked. Independent of W1.*

**Produces.** Two readonly structs, both carrying `UnitCode`. The operator set from D-036,
with `Quantity + Quantity` deliberately absent. Unit mismatch throws; conversion is
explicit through `factor_to_base`. A unit's `decimal_places` is enforced at construction.

**Tests.** `closing == opening + Σ(deltas)` over a generated movement sequence; 1 kg + 500 g
throws unless converted; a `piece` unit rejects 1.5; a delta of zero throws; `Magnitude`
discards sign and `Decrease` applies it.

**Estimate.** 4–6 h.

---

### W3 — `IIdGenerator`

*Unblocked. Small, and W10 cannot start without it.*

**Produces.** The port in Domain. `UlidGenerator` in Application. `SeededIdGenerator(seed,
clockStart)`. The `Ulid` package moves to Application. DI registration in StoreServer.

**Tests.** Same seed, same sequence of ids, across two runs and two processes. Ids sort in
creation order. A generated 2024 store has 2024 timestamps embedded. Different seeds do not
collide over 100k ids.

**Estimate.** 2–3 h.

---

### W4 — The first real migration

*Unblocked, but do it after W1 so the column types follow the value objects.*

**Produces.** `rounding_variance` (D-034), `stores.rounding_policy`,
`transactions.rounding_policy`, and two append-only triggers on the new ledger.

**The care this needs.** `transactions` is one of the three tables CLAUDE.md §3.7 names as
an operational event. Adding a column with a default is an `ALTER TABLE ADD COLUMN` in
SQLite, not a rebuild — but that has to be *checked in the generated migration*, not
assumed, because EF will rebuild if it decides it needs to, and a rebuild drops triggers,
indexes and CHECKs silently (D-022). Read the generated `Up()` before running it.

**Tests.** `ModelMatchesSchemaTests` still green. All 13 triggers present after
`MigrateAndApplyTriggers()`. The ledger rejects UPDATE and DELETE. `schema_current.sql`
regenerated and matching.

**Estimate.** 3–4 h, most of it reading the generated migration.

---

### W5 — Wire the value objects into the generated configurations

*Blocked on W1, W2, W4.*

**Produces.** Value converters for `Money`, `Quantity` and `QuantityDelta`, applied by a
rule in `tools/generate-model` keyed off the column comments the schema already carries
(`-- centimes` → Money, `-- thousandths, signed` → delta, `-- thousandths` → quantity).
Regenerate all 58 configurations. The ambient ledger currency comes from
`Waymark:Store:Currency`, with a startup check that it equals `stores.currency`.

**The trap.** Every column comment must map to exactly one rule, and a column that matches
none must **fail the generator** rather than fall through to `long`. A silent fall-through
is how a money column ends up unwrapped and nobody notices.

**Estimate.** 4–6 h.

---

### W6 — The Domain package allowlist test

*Unblocked. Half an hour.*

Reads `typeof(AssemblyMarker).Assembly.GetReferencedAssemblies()` and fails on anything
outside `System.*` / `netstandard` / `mscorlib` plus a named allowlist. **The allowlist
starts empty** (D-038). Must fail when a package is added to Domain — mutation-test it.

---

### W7 — `Waymark.Pseudonymisation` · blocked on **O-2**

HMAC-SHA256, the domain-separation prefix, truncation to 128 bits, Crockford base32, the
tier 1→2 transform interface. Key custody is the blocker: where the bytes live and whether
they are recoverable.

**The test that matters most is not the hash.** It is the one proving the tenant key cannot
appear in a backup payload or the outbox — D-039 accepts losing file-level exclusion, and
this test is what replaces it. It has to fail when the exclusion is removed.

---

### W8 — `Waymark.Contracts` · blocked on **O-15**

Recommendation envelope, intent shape, sync message shapes. Cheap once the shape is fixed;
expensive to change afterwards, because Almanac emits it too.

---

### W9 — Application scaffolding · blocked on **O-16**

Command/handler skeleton, and the `processing_log` helper. §4 says the write is structural
rather than remembered, which means the helper's signature *is* the privacy promise —
which is why guessing the columns is not an option.

---

### W10 — Synthetic store generator · blocked on **O-17**, needs W1–W3

The Build Plan calls this the highest-leverage build in the phase. ~400 variants, 8
categories, 5 suppliers, 3 staff, one year of transactions with weekly and seasonal shape,
Ramadan, payday spikes, realistic expiry, spoilage, stockouts, returns. Deterministic from
a seed.

Review its **output distributions**, not its code. A generator built to a guessed spec
produces plausible-looking data that teaches the engine the wrong seasonality, and nothing
catches that later.

---

### W11 — Fake hardware · parked

ESC/POS writer to a file, drawer-kick logger, scanner as keyboard input. Nothing blocks it;
it is simply worth less than everything above until there is a POS to attach it to.

---

## Division of labour

The Build Plan says Hakim writes the value objects. That was written before the decisions
existed; now that D-031…D-037 specify the arithmetic completely, the proposal is:

| | Hakim | Claude |
| :---- | :---- | :---- |
| W1, W2 | Reviews line by line. The arithmetic is what gets defended on stage | Writes, tests first, proves each test fails |
| W3, W5, W6 | Reviews the result | Writes |
| W4 | **Reads the generated migration before it runs** | Generates, verifies, regenerates `schema_current.sql` |
| W7 | Decides O-2, then reviews | Writes after the decision |
| W10 | Writes the spec, reviews output distributions | Writes the generator |

If you would rather write `Money` yourself, say so — it is ~150 lines of arithmetic and the
tests are the expensive half, so a split where you write the type and I write the suite
also works.

---

## How Phase 0 ends

From the Build Plan, as amended:

- Solution builds; architecture tests pass **and fail** when a forbidden reference is added
- Migration creates the store database from empty, encrypted, with every trigger in place
- The generator produces a year of data that loads and looks plausible under inspection
- Value-object tests cover both rounding policies, allocation summing exactly, TVA
  extraction from TTC, currency mismatch throwing, the cash step, and the level/change algebra
- The tenant key cannot appear in a backup payload or the outbox, proved by a test that
  fails when the exclusion is removed

**One thing outside the code.** `Waymark_DPIA_v1` §5.2 describes a separately-stored
mapping table. D-039 removed it. That document must be amended before it is filed, and
that is not a task any test will remind you about.
