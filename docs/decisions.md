# Decision log

Every non-obvious choice: **what**, **why**, **what was rejected**. Code cites these
numbers, so they are never renumbered or reused.

**Maintenance.** An entry states what is true *now*. When a decision is replaced, rewrite
the entry to the current rule and name what replaced it, in one line. Do not keep the old
prose, because git keeps it. The long-form log, with every superseded argument, is at
commit `c3531bf` (compacted 13/09/2026).

Open questions (`O-nn`) are at the end.

| Topic | Entries |
| :---- | :---- |
| Repository, build, CI | D-001–D-012, D-014, D-017 |
| Storage and schema mechanics | D-013, D-015, D-016, D-019–D-029 |
| Store scoping | D-030 |
| Money, quantity, identity | D-031–D-038, D-041, D-053 |
| Privacy, keys, pseudonymisation | D-039, D-040, D-042, D-043, D-045, D-051 |
| Contracts and application | D-044, D-048, D-049, D-050 |
| Synthetic generator | D-046 |
| Hardware | D-052 |
| Testing | D-018, D-047 |

---

## Repository, build, CI

### D-001 — Monorepo
One repository: `/src`, `/waymark-admin`, `/waymark-engine`, `/docs`, `/learning`, `/tools`.
**Why.** The recommendation envelope is defined once and consumed by C#, TypeScript and
Python, so one commit can change it and all three consumers. **Rejected.** A repo per
surface. Reconsider if the cloud side gets its own deploy cadence.

### D-002 — `learning/` stays in the repository
Renamed from `Learning stage/`, because spaces break shells and CI globs. It is kept
because its forecasting, statistics and OR modules are the reference implementations the
engine is checked against.

### D-003 — Central package management
`src/Directory.Packages.props` holds every version and projects name packages without one.
A version bump is one reviewed line. Never `dotnet add package`: it has damaged both files
before (CLAUDE.md §9).

### D-004 — `net10.0` LTS, set once in `Directory.Build.props`
Set alongside `Nullable`, `ImplicitUsings` and `TreatWarningsAsErrors`. **Why.** The till
runs unattended for years, and a warning left to accumulate is exactly the silent signal
this codebase must not discard. **Rejected.** STS releases, per-project frameworks.

### D-005 — Architecture tests live in `Waymark.Integration.Tests`
They must reference every assembly, and giving `Domain.Tests` a reference to Persistence
would create the coupling they forbid. **Rejected.** A separate project; revisit if the
rules outgrow one file.

### D-006 — `AssemblyMarker` in every library
NetArchTest reports success over an empty type set, so a rule over an assembly with no
named type passes while inspecting nothing. **Rejected.** `Assembly.Load` by name.

### D-007 — The POS shell is code-only, no XAML
A placeholder with no screens. Phase 1 introduces `.axaml`.

### D-008 — `*.db` is ignored globally
A blanket pattern cannot be got round by naming a file differently, and every database is
generated. (Written for `waymark-identity.db`, which D-039 removed; the rule stands.)

### D-009 — NuGet audit findings are build errors
Fix a vulnerable transitive package with a pin in `Directory.Packages.props`, never with a
suppression. **Rejected.** `NuGetAudit=false`, `NuGetAuditLevel=critical`. Pins are only
trustworthy after a restore has run.

### D-010 — Avalonia stays on 11.3
12.x is weeks old and `Avalonia.Diagnostics` has no 12.x release. A UI regression on an
unattended till costs a customer. Revisit when 12.x has a track record.

### D-011 — CA1707 is off in `src/tests` only
Test names are sentences, so a failure reads as the rule that broke.

### D-012 — A rule is verified by breaking it
Break the code, watch the right test fail, restore. A green test proves nothing on its own:
NetArchTest cannot tell an untested rule from one that cannot fire. Generalised to every
test in CLAUDE.md §8.

### D-014 — `Microsoft.EntityFrameworkCore.Design` is referenced twice
Referenced by Persistence and StoreServer, both `PrivateAssets="all"`. The EF tools
resolve against the startup project. **Rejected.** Dropping `PrivateAssets` in
Persistence, which would leak a design-time dependency into every consumer.

### D-017 — CI runs on `windows-latest`
The till is Windows, the POS is a `WinExe`, and data lives under `%ProgramData%`. CI should
fail the way a store fails. **Rejected.** Ubuntu, and a matrix of both.

---

## Storage and schema mechanics

### D-013 — Store data lives under `C:\ProgramData\Waymark`
```
data\       waymark-store.db (+ -wal, -shm)   backed up
keys\       tenant key, database key          own ACL; local backup only (D-042)
pos-cache\  POS Level-2 cache                 never backed up
backups\  logs\  config\
```
The root comes from `CommonApplicationData`. `Waymark:Storage:DataDirectory` overrides it
for development and tests. Off Windows, where the default maps to root-owned `/usr/share`,
use the override: no second default path is baked in.

**Why.** The data has to:
- survive an update, since a self-contained publish folder is replaced wholesale;
- live somewhere writable, which `Program Files` is not;
- be machine-scoped, because a service under LocalSystem cannot see `%LOCALAPPDATA%`;
- stay out of OneDrive, which corrupts an open WAL database;
- sit in a writable *local directory*, because WAL creates sibling files and does not work
  over SMB or a NAS.

**Rejected.** Beside the executable, `%LOCALAPPDATA%`, `Documents`, a network share.

**Installer consequence.** `C:\ProgramData` gives `BUILTIN\Users` write access but only
read access to files another account created. So the installer must grant Modify on
`data\`, and restrict `keys\` (O-18).

### D-015 — One native SQLite provider for the process: SQLCipher
Persistence uses `Microsoft.EntityFrameworkCore.Sqlite.Core` plus
`SQLitePCLRaw.bundle_e_sqlcipher`. The plain `…Sqlite` package drags in `bundle_e_sqlite3`,
and two bundles in one process link, build and start, then fail at file-open depending on
init order.

**Cost.** SQLCipher 4.5.2 community is **SQLite 3.39.2**, so write DDL against that
floor. STRICT, `RETURNING`, generated columns, `DROP COLUMN`, `->`/`->>`, UPSERT, window
functions, partial indexes and FTS5 are all present. JSONB (3.45) is missing; text JSON is
enough. A developer's `sqlite3` CLI is newer, so `dotnet ef` is the authority.

**Rejected.** `bundle_zetetic` (commercial; a package swap if the version lag ever bites);
one process per database.

`waymark-store.db` is the encrypted file (D-042). An unencrypted database still opens
under the same bundle, which the POS cache needs (D-040).

### D-016 — The schema is evolved by EF migrations from a reviewed v1
Three options were weighed. A: the `.sql` stays canonical, meaning hand-written SQLite
12-step rebuilds for years. B: EF is canonical, meaning 11 reviewed triggers and 139
indexes re-expressed. C: a reviewed v1 plus EF automation after it. **C was chosen.**
D-019 replaced the bootstrap mechanism; these costs still apply and code cites them:

- **Cost 3.** Tables created by later migrations would not be STRICT. Paid by
  `StrictSqliteMigrationsSqlGenerator`, which covers plain creation and the rebuild path
  and is registered only through `UseWaymarkSqlite`.
- **Cost 4.** `__EFMigrationsHistory` and `__EFMigrationsLock` belong to EF and are
  excluded from STRICT and invariant checks.
- **Cost 5.** An EF table rebuild silently drops every index and trigger the model does
  not know about. This was reproduced: EF printed `Done.` with an append-only trigger gone.
  Paid by three things: every index is declared in the model; triggers live in
  `triggers.sql` and are re-applied after every migration (D-025); and the fidelity tests
  (D-029). D-022 extends this to CHECK constraints and foreign keys.

### D-019 — `MigrateAndApplyTriggers()` is the only creation path
`InitialSchema` creates all 58 v1 tables from the model and reproduces
`schema_v7_1.sql` exactly. That was checked mechanically across columns, column order,
types, nullability, keys, 137 FKs, unique groups, index filters, 167 CHECKs and 102
defaults (O-9). The first form, schema SQL pasted into `Up()`, was dropped once that was
proven. **Why.** Creation and evolution become one call, with no baseline row to stamp by
hand. `schema_v7_1.sql` is frozen history. `schema_current.sql` is the readable current
schema, generated by a test (D-026) and never executed. `Migrate()` must not run inside a
transaction: EF Core 9+ opens its own. **Rejected.** Reading the `.sql` from an embedded
resource inside `Up()`, which would let a historical migration change meaning when a file
changes.

### D-020 — Golden file and invariant tests answer different questions
`schema_current.sql` detects *change*: an unintended schema diff fails the build.
Invariant tests check *rules*, and they must execute against a real migrated database,
because a declaration is not behaviour. A trigger with the right name and text can carry a
`WHEN` clause that narrows it to nothing, and only executing the `DELETE` finds that. A
generated file can also be stale, or bake in an absence, such as a database built without
triggers. Both kinds of test are kept.

### D-021 — One set of entities: Domain owns them, Persistence maps them
Entities are plain classes in `Waymark.Domain`, with one `IEntityTypeConfiguration<T>` per
entity in Persistence. Domain's zero-dependency rule stops an entity acquiring EF
attributes. The context is `WaymarkDbContext`, with options through the constructor so
tests can point it anywhere. `WaymarkDbContextDesignTimeFactory` targets a scratch path
under `%TEMP%` (never a store) and registers the STRICT generator. **Rejected.** Separate
domain and persistence models (116 classes plus mapping); EF attributes on entities.

### D-022 — A table rebuild also drops CHECK constraints and foreign keys
This was reproduced: after a rebuild, `CHECK (amount > 0)` was gone and `-5` was accepted.
A CHECK cannot be re-applied the way a trigger can. So **every index, CHECK and FK is
declared in the EF model** (CLAUDE.md §3.7). Changing an existing CHECK means a rebuild, so
later decisions deliberately use `ADD COLUMN` or code-side validation instead (D-044,
D-045, D-048). D-053 took a rebuild knowingly, while no store holds data.

### D-023 — The 58 entities were generated, not typed
`tools/generate-model` read the database built from `schema_v7_1.sql` and wrote the
entities, 70 enums, configurations and the `DbContext`. It was a one-shot: **never re-run
it.**

Rules it applied:
- Every `INTEGER` becomes `long`, with no exceptions. An `int` money column overflows at
  21 474 836,47 DZD.
- Enums get explicit converters, never `HasConversion<string>()`, which would store
  `"Standard"` where the CHECK allows only `"standard"`.
- Every CHECK, index and FK is declared.
- `required` marks only what a caller must supply.

EF's `ForeignKeyIndexConvention` is removed, so every index is one somebody chose (68 named
indexes plus 14 UNIQUE constraints, not 153). An inline UNIQUE surfaces as
`sqlite_autoindex_*` versus EF's `IX_*`: a harmless, permanent naming difference.

Naming: `returns` → `SalesReturn` (CA1716), `category_attributes` →
`CategoryAttributeLink` (CA1711), `consent_events.action` → `ConsentEventAction`, and
`value_type` → `Promotion*ValueType`. CA1720 is off for `Domain/Enums`, because enum
members mirror schema values.

### D-024 — A comparison is worth only what it compares
The generator flattened three composite UNIQUE constraints into single-column ones, and
dropped the `WHERE` on all 12 partial indexes. Three of those indexes *are* constraints
(`ux_parameter_current`, `ux_product_category_primary`, `ux_transactions_invoice`). Both
defects passed, because the comparison matched indexes by name and columns only. Hakim
found them. The comparison now checks uniqueness, filters and unique *groups*, and was
verified by reintroducing both defects. Fixed in the generator, not by hand-editing the
migration.

### D-025 — `triggers.sql` and `MigrateAndApplyTriggers()`
The append-only triggers live in `src/Waymark.Persistence/triggers.sql`, an embedded
resource. Every statement is `DROP TRIGGER IF EXISTS` then `CREATE`, so the file is
idempotent. `MigrateAndApplyTriggers()` migrates, applies the triggers and **throws if any
trigger is missing** or names a table that does not exist. It also refuses an ambient
transaction with a message that points at the cause. The name is blunt on purpose:
`Migrate()` alone leaves audit tables unprotected, and nothing reports it.

Test fixtures: `ReviewedSchemaFixture` builds from the frozen `.sql`.
`MigratedDatabaseFixture` builds the way a store does, and every rule suite uses it.
Currently 14 triggers. A trigger added later on a table the baseline already has is listed, with its reason, in `ModelMatchesSchemaTests.TriggersAddedAfterTheReviewedSchema` (procedure: `schema-changes.md` §3.6).

### D-026 — `schema_current.sql` is produced by a test, not a script
`SchemaCurrentTests` regenerates it when `WAYMARK_UPDATE_SCHEMA_CURRENT=1`, and otherwise
fails on drift. Output is sorted, so the diff reads as a summary of the migration. A test
owns `MigratedDatabaseFixture` and cannot build the database the wrong way. A script run
against a `dotnet ef database update` database would record zero triggers as correct.
**Rejected.** Generating it from `schema_v7_1.sql`; dumping `.schema` from the CLI.

### D-027 — Every `HasDefaultValue(x)` is paired with `HasSentinel(x)`
EF omits a property equal to its sentinel (the CLR default) and lets the database default
apply. So `Customer.LegalBasis = Consent` stored `'contract'`, `Priority = 0` stored `100`,
and `IsActive = false` stored `1`. Fifteen properties were exposed. With the sentinel set
to the database default, omission is harmless everywhere. `DefaultValueSentinelTests`
reads the raw column, because a round trip through EF hides the swap. `Money` needs no
explicit sentinel (D-041). **Rejected.** Dropping `HasDefaultValue`, which loses the
DEFAULT on the next rebuild.

### D-028 — StoreServer initialises the database at startup, or does not start
StoreServer resolves the data directory (D-013), registers the context through
`UseWaymarkSqlite`, and calls `MigrateAndApplyTriggers()` before serving. It also refuses
to start if `Waymark:Store:Currency` disagrees with the `stores` row (D-041). A till that
won't start is a phone call; a till that silently stopped enforcing consent history is a
finding. `WaymarkStoragePaths` composes paths and nothing else.

### D-029 — The fidelity comparison guards the baseline, not head
Comparing the frozen `.sql` against head broke on the first legitimate migration. Three
tests now split the job:

| Test | Guards |
| :---- | :---- |
| `ModelMatchesSchemaTests.The_baseline_migration_reproduces_the_reviewed_schema` | `InitialSchema` alone versus `schema_v7_1.sql`. Indexes matched by shape, not name |
| `ModelMatchesSchemaTests.The_model_has_no_changes_waiting_for_a_migration` | A configuration changed with no migration |
| `SchemaCurrentTests` | The shipped schema drifting from its golden file, names included |

---

## Store scoping

### D-030 — A marker interface, a reflected filter, fail closed
Entities with `store_id` implement `IStoreScoped` (17 tables). `WaymarkDbContext` attaches
a global query filter to each by reflection: one rule, never a line per configuration. The
nullable tables `promotions` and `processing_log` read `store_id IS NULL OR = @p`, where
null means "every store". The rest use plain equality. **Unset means nothing**: a null
`ICurrentStore.StoreId` matches only store-less rows. `stores` is filtered too.
`IgnoreQueryFilters()` is for test seeding; any production use needs a decision entry.
Verified: removing the marker from `Terminal` returned store B's terminal to store A,
which is DPIA R9 reproduced. **Rejected.** A convention on the column name; a `where` at
each call site.

---

## Money, quantity, identity

### D-031 — `Money`: a fixed-scale integer, a currency, no accidental rounding
`readonly struct Money(long MinorUnits, Currency Currency)`. **Storage is always 1/100 of a
currency unit**, a Waymark convention rather than the currency's exponent, so an EF
converter stays a pure function. `+`, `-`, comparison and `× int` are exact operators.
Anything that can round is a method taking a policy: `Times(num, den, Rounding)`,
`Percent(BasisPoints, Rounding)`, `DivideBy`, and `Allocate` (exact, no policy). **There is
no `operator *(Money, decimal)`**, and `MoneyApiShapeTests` fails if one appears. Grepping
those methods enumerates every site where a centime can appear. **Rejected.** `decimal`
everywhere with rounding at persistence, which makes the rounding sites unknowable.

### D-032 — Rounding is three problems, not one setting
| Problem | Mechanism | Variance? |
| :---- | :---- | :---- |
| Splitting a known total | `Allocate`: floor, then largest remainder, ties by position | None, by construction |
| Deriving a value | `HalfEven` or `HalfUp`, from `stores.rounding_policy` | Yes, the retailer's policy |
| Cash tender | Currency cash step (D-034) | Yes, recorded |

The policy is stamped on `transactions.rounding_policy`, so a receipt recomputes from its
own row after a policy change (columns and default: D-053). `Truncate` is not a store
policy: it drifts one way and
never cancels. Banker's rounding reduces drift but does not remove it, because prices
cluster on `.99`, so the ledger exists under either policy. **Almanac always uses
`HalfEven`**, stores full precision and rounds at display only.

### D-033 — TVA is extracted per line from TTC, by subtraction
Displayed retail prices are TTC (Law 04-02), TVA is per line, and the discount is applied
first.
```
line_ttc = round(sell_price × qty) − discount
ht       = round(line_ttc × 10000 / (10000 + rate_bp))
tva      = line_ttc − ht          ← never rounded on its own
```
`ht + tva == line_ttc` holds on every line, so `subtotal + tax_total == total_amount`
exactly, and there is no invoice-level tax variance (no `tax_reconciliation` source).
`prices.is_tax_inclusive` stays for B2B and HT supplier prices. Resolves O-11.

### D-034 — Cash tender rounds to the currency step; the difference is recorded
**The tender rounds, never the invoice**, and only the cash portion; card pays exact. The
rule is nearest, ties away from zero, with `Currency.CashRoundingStep` = 500 for DZD and 1
elsewhere. The number 500 never appears in a handler. The difference goes to
`rounding_variance` (source `cash_tender` or `currency_conversion`, append-only), **never
`cash_sessions.variance`**, which exists to catch theft and would learn to be ignored.
Allocation is never a source: a row with it would mean the allocator is broken. Invariant:
`expected_cash = float + Σcash + Σpaid_in − Σpaid_out − Σdrops + Σtender_variance`.
**Rejected.** Always toward the store, which takes up to 4,99 DZD from every cash
customer. Resolves O-10.

### D-035 — Currency is carried by the value
Ledger currency (immutable, DZD), document currency (per document) and presentation
currency (never stored) are kept apart. Arithmetic across currencies **throws**, and there
is no implicit conversion. `Currency` is a code registry (DZD; EUR and USD defined), not a
table. There is no CHECK on the 8 `currency` columns, since adding one would rebuild
`transactions`. **Known Stage 2 limit:** the ambient converter builds `Money` in the ledger
currency, so a EUR supplier document does not round-trip. D-041 puts an alarm on it.
Conversion happens once, into `batches.unit_cost` with rate and date, as a landed-cost
decision. **Not built:** rate tables, a conversion engine, multi-currency reports.

### D-036 — `Quantity` (a level) and `QuantityDelta` (a change)
Stored in thousandths. The schema has signed levels (`inventories.quantity` may go
negative), signed changes and positive-only magnitudes, so the split is level versus
change, not sign.
```
Quantity + QuantityDelta → Quantity      Quantity − Quantity → QuantityDelta
QuantityDelta + QuantityDelta → QuantityDelta      Quantity + Quantity  ✗ (named Sum only)
```
`closing = opening + Σdeltas` becomes type-checked, catching the sign error no test would.
A zero delta is legal as a value; the column's `CHECK (<> 0)` forbids it as a row. Both
types carry `unit_code`, mismatches throw, and `UnitPrecision.For(code, decimal_places)`
enforces precision (a piece rejects 1,5). **Unit conversion is not built**: when it lands
it shares Money's round-once rational primitive. Money gets no level/change split, since
the credit ledger is append-only (O-14).

### D-037 — Division by zero throws; absence is never zero
`DivideBy(0)` always throws. A caller that legitimately expects zero calls `TryDivideBy`,
gets `null` and must handle it, so the choice is visible in which method was called. A 0%
margin shown because revenue was zero is the canonical silent error; the engine and the UI
have an explicit "insufficient data" state instead.

### D-038 — ULIDs come from a port
`IIdGenerator` is declared in Domain. `UlidGenerator` (in Application, where the `Ulid`
package lives) is a singleton in StoreServer. `SeededIdGenerator(seed, clockStart)` is for
tests and the generator. **Never `Ulid.NewUlid()` in an entity**, because a static call
cannot be seeded. The Domain package allowlist exists and is **empty**. Resolves O-1.
`SeededIdGenerator` must follow W10's simulated clock before the generator uses it (D-046).

### D-041 — `Money` is wired into the model; `Quantity` cannot be
**32 money columns are `Money`**, converted centrally by CLR type in `OnModelCreating`.
That produced no migration: a converted type is invisible to the model differ. The list is
hand-verified in `MoneyMappingTests.MoneyColumns` and checked both ways, because 12 money
columns carry no `-- centimes` comment.

**Stay `long`:** columns whose unit depends on a sibling value
(`promotion_*.promotion_value`, `parameter_registry.value_number` and intervals,
`recommendations.interval_*`, `recommendation_options.projected_value`,
`*_attribute_values.value_number`), and **all quantity columns**. A converter sees one
property, and 5 of the 11 quantity columns have no unit on their row, so `Quantity` is
built at the point of use.

`default(Money)` has no currency, so EF uses it as the sentinel.
`WaymarkModelCacheKeyFactory` keys the model by ledger currency.
`MoneyMappingTests.Every_currency_column_holds_the_ledger_currency` is the Stage 2 alarm.

### D-053 — `stores.rounding_policy` and `transactions.rounding_policy`
Migration `AddRoundingPolicies` (Hakim, 13/09/2026). Both columns are `TEXT NOT NULL
DEFAULT 'half_up'` with `CHECK (rounding_policy IN ('half_even','half_up'))`, mapped to the
same `Rounding` value object and converter that `rounding_variance.policy` uses, so one
vocabulary means one thing.

- **`HalfUp` is the store default**: it is what retailers and most fiscal software expect,
  and the retailer confirms or changes it at onboarding.
- **`Transaction.RoundingPolicy` is `required`, with no C# default.** A handler must copy
  the store's policy explicitly. A silent default would stamp `half_up` on a sale in a
  `half_even` store, which is the unrecomputable receipt D-032 exists to prevent. The
  database default only serves the `ADD COLUMN`.
- `HasSentinel(HalfUp)` pairs the default (D-027). `HalfEven` is the CLR default of
  `Rounding`, so without the sentinel it would be dropped from the INSERT;
  `DefaultValueSentinelTests` reads both raw columns.
- **Cost, taken knowingly:** the CHECKs make this a rebuild of `stores` and `transactions`
  (D-022). Every index, FK and CHECK was recreated from the model, and neither table has a
  trigger. Rebuilt columns come back in alphabetical order, a cosmetic change visible in
  `schema_current.sql`. Acceptable while no store holds data; after the first
  installation, a CHECK on `transactions` is an operational event (CLAUDE.md §3.7).
- **Rejected:** a separate `RoundingPolicies` enum duplicating `Rounding`; a converter-only
  column without a CHECK (the D-048 route), because the CHECK is cheap before go-live.

---

## Privacy, keys, pseudonymisation

### D-039 — Pseudonymisation by keyed hash, with no mapping table
`pseudonym = base32(HMAC-SHA256(tenant_key, "waymark:<population>:v1:" ‖ id)[0..16])`: 26
Crockford characters, like a ULID. `waymark-identity.db` is removed.

**Why.** The identity file never held PII (names are in `customers`). Its only job was
per-customer severance by deleting a row. Reverse lookup needs no table: a store has a few
thousand customers, so compute each pseudonym and match. The separation that matters is
that **Waymark never holds the key alongside a backup**.

**Accepted costs:**
1. Exclusion is a rule, not a file boundary; D-042 restores most of it.
2. Erasure depends on the cloud nulling the pseudonym, with no local unilateral cut.
3. A key compromise re-identifies the tenant's history permanently, and the scheme does
   not rotate.

**Binding conditions:**
1. The key is never in a backup or sync payload, proved by a test.
2. One key per tenant, not per store (O-12). It is generated client-side and never issued
   by the cloud.
3. Domain-separation prefix per population.
4. `Waymark.Sync` never references `Waymark.Pseudonymisation`.

**Erasure** unlinks rather than destroys: null the pseudonym on cloud transaction rows and
record it in `erasure_ledger`, which is re-applied on restore. **Rejected.** A random
pseudonym plus a mapping file: a stronger local cut, but a second key, write ordering and
two-phase bugs on one till.

### D-040 — SQLCipher was verified end to end
A throwaway probe on the real stack found SQLite 3.39.2 and SQLCipher 4.5.2. It confirmed:
WAL works; STRICT works; the wrong key or no key is rejected; no plaintext leaks, schema
text included; `PRAGMA rekey` works; an unencrypted DB opens in the same process; and
`MigrateAndApplyTriggers()` succeeds on an encrypted file. **Nothing in production code
supplies a key yet** (O-20).

### D-042 — Key custody is split by what each key protects
| | Database key | Tenant key |
| :---- | :---- | :---- |
| Protects | Availability: loss destroys history | Confidentiality: compromise is retroactive |
| Rotates | Yes, via `PRAGMA rekey` from an admin command | Never |
| Supplied as | Raw key `PRAGMA key = "x'…'"`, skipping the KDF | HMAC key in `Waymark.Pseudonymisation` |

Both are 32 bytes from `RandomNumberGenerator`, wrapped with DPAPI `LocalMachine` scope,
with entropy bound to the install id and **domain-separated per key purpose** (D-051).
Each is its own file in `%ProgramData%\Waymark\keys\`. The **cloud backup set is an
allowlist of directories** and never includes `keys\`; the local nightly backup does. The
exclusion test must search for the **wrapped blob** as well as the raw key. **Neither key
is escrowed with Waymark.** Recovery is a printed code per key, held by the retailer and
produced by an in-app screen, built in Phase 1 so the Phase 2 backup key needs no site
visit. **Key check:** a truncated `HMAC(tenant_key, "waymark:keycheck:v1")` is stored in
the cloud tenant record. After a restore StoreServer recomputes it and refuses to sync on
a mismatch until a new epoch is acknowledged.

**Not defended:** a stolen, powered-on till. LocalMachine DPAPI is recoverable from a disk
image, full-disk encryption is unavailable on Windows Home, and the DPIA records this as
accepted risk R5. **Defended:** copies taken during repair, USB grabs of `ProgramData`,
backup staging.

**Rejected.** Escrow, which would give Waymark both halves; a passphrase-derived tenant key,
which Waymark could brute-force from pairs it holds. Resolves O-2.

### D-043 — Tier 2 is full-fidelity and local; the outbox shapes what leaves
The tier 1→2 transform runs in the store and writes local **DuckDB** at transaction grain
with the pseudonym. Only the outbox crosses, as **two streams that must never re-join**:

| Stream | Grain | Pseudonym |
| :---- | :---- | :---- |
| Anonymous basket | Per basket: date, hour bucket, weekday, lines (product, qty, value), payment class, discount flag | None; no customer column at all |
| Customer period | Per pseudonym per month: visits, banded spend, distinct categories, recency, first-seen period, objection flag at emit | Yes |

The mismatched grains are the control. Coarsening (hour, spend band) happens **at emit**.
The test for a field is **erasure-survivability**: does it single anyone out once the
pseudonym is nulled? The customer stream has two gates, the licence and the objection
flag; an objection downgrades to the basket stream rather than dropping the data.

**Standing rules:**
- No field enters the outbox without a named consumer.
- No free text: ids only, resolved cloud-side.
- One versioned record shape per stream.

The field lists are illustrative and grow with the statistics. Resolves O-3.

### D-045 — `processing_log` records every named operation, never a direct identifier
Required by Loi 25-11 (articles 41 bis 2 and 3): a register of activities plus an
automated logbook of collection, modification, **consultation**, transmission and
deletion, with reasons, times and actors, produced to the ANPDP on request. Consultation
is logged per event.

- `subject_id` is **always a pseudonym**, staff included under their own prefix. Nothing
  is purged at erasure, and destroying the key unlinks the whole log.
- `IProcessingLog.Record` takes a `Pseudonym`, which has no string overload and no
  implicit conversion, and a test enforces that.
- `purpose` is a closed enum validated in code (no CHECK, so no rebuild): `pos_sale`,
  `loyalty_lookup`, `credit_management`, `customer_service`, `analytics_pseudonymised`,
  `legal_obligation`, `data_subject_request`, `retention_expiry`.
- `legal_basis` is added.
- **Append-only against edits**: `trg_processing_log_no_update` refuses every UPDATE (the
  DPIA §5.5 promise; resolves O-21). DELETE is deliberately left open for the retention
  roll-up below, and erasure never needs to touch the log.
- Growth is bounded at retention: detail is kept for the statutory period, then rolled
  into `processing_counters` per `(store, day, operation, purpose)`.
- **No engine or reporting path reads the log**; a test is owed when those paths exist.

Outside the code: designate a DPO, declare to the ANPDP before operating, price both into
the customer module. Resolves O-16.

### D-051 — The pseudonymisation boundary, as built
- `PseudonymScheme` is the one expression. Its test recomputes from primitives rather than
  comparing a stored constant.
- There are three populations (`Customer`, `Staff`, `Supplier`), and an unknown one
  throws.
- The first character is `0–7`, since 130 bits of room hold 128.
- **`IPseudonymiser` is a capability.** Sync may not touch `Waymark.Domain.Privacy`, and
  Contracts, Persistence and Hardware may not name `IPseudonymiser` or `ITenantKeyCheck`.
  Application and the hosts may.
- `ITenantKeyCheck` is a separate port, on least privilege.
- Entropy for the tenant key is `waymark:tenant-key:v1:‖install_id`; the database key
  needs its own prefix, or the two blobs become swappable.
- **No method replaces the key**: creation uses `FileMode.CreateNew`, and the unwrap error
  says not to delete the file.
- `TenantKeyNeverLeavesTests` searches `waymark-store.db`, its `-wal` file and outbox
  payloads for the key and the wrapped blob, as raw bytes, hex in both cases and base64.

**Finding:** LocalMachine DPAPI is unwrappable by *any* local process, so the ACL on
`keys\` is the real control against a second local account, and nothing sets it (O-18).

---

## Contracts and application

### D-044 — The recommendation envelope mirrors the recommendation tables
`Waymark.Contracts` mirrors `recommendations`, `recommendation_options` and
`recommendation_decisions` rather than inventing a shape. Additions:
- `recommendation_type` column. The dedupe key is derived as `(store_id, type,
  subject_type, subject_id)`, and re-emission means an update plus a superseded row.
- `because_json` = `{key, params, factors[]}`, each factor
  `(label_key, value, unit, direction)`. The headline is rendered in the store's language;
  the factors are the auditable reasoning.
- Option `payload_json` is an **executable intent** mapped to an Application command.

**Rejected:** new action types (a CHECK change means a rebuild; `binary` and `menu` plus
`adjust` cover them); a stored priority score (derivable); a `requires_reidentification`
flag (implied by the subject type and role). Resolves O-15.

### D-048 — The schema cost of D-042 to D-047
One migration, `ProcessingRegisterAndRecommendationType`, with no rebuild: two
`ADD COLUMN` (`recommendations.recommendation_type`, `processing_log.legal_basis`, both
`DEFAULT ''` as SQLite requires), `processing_counters`, and `ix_recs_dedupe`.

Three collisions were resolved:
1. **`Pseudonym` lives in Domain** with an `internal` constructor, and
   `InternalsVisibleTo` goes to `Waymark.Pseudonymisation` only. Application can hold one
   and cannot make one.
2. **`ProcessingLegalBasis`** is its own five-member enum, because `customers.legal_basis`
   has a four-value CHECK.
3. **`processing_log.subject_id` stays nullable.** A system operation has no subject, and
   the type enforces "never an identifier".

`purpose` became the `ProcessingPurpose` enum, which throws both ways on an unknown value.

### D-049 — Contracts are checked against the database
Contracts references nothing, so its enums are copies. `ContractsMatchSchemaTests`
compares them with the **CHECK constraints of the live migrated DB** (12 vocabularies;
`FactorDirection` and `Comparison` are contract-only). `ContractsMirrorTheSchemaTests`
compares fields with columns both ways, with listed, reasoned exceptions.

Wire rules:
- **Figures cross as strings**, never JSON numbers (a double in TS and Python).
- Every property has `[JsonPropertyName]` and enums have `[JsonStringEnumMemberName]`, so
  naming never depends on the consumer's serializer policy.
- `OutboundEnvelope` and `InboundEnvelope` are separate types with separate channel enums.
- Payloads are unparsed `JsonElement`.
- `entity_type` stays an open string.
- `Precondition` is a single comparison, so the store evaluator stays a comparator
  (CLAUDE.md §5).

**Open:** mirror `payment_class` onto the payment-method CHECK; `BecauseFactor.unit` mixes
unit codes with `percent` and `days`; there is no spend-banding scheme yet (a DPIA §2.6
commitment).

### D-050 — Handlers stage, the executor commits
`ICommandHandler` stages and returns. `CommandExecutor` seals the `CommandContext`, then
calls `IUnitOfWork.CommitAsync` once, so domain rows, outbox and log commit together.
`CommandContext.NewId()` and `.Record()` throw after sealing. `IProcessingLog.Record`
stages into the same unit of work. The caller does not set `log_id` (`IIdGenerator`),
`occurred_at` (injected `TimeProvider`) or `store_id` (`ICurrentStore`): a row filed under
another store is invisible behind the fail-closed filter. `ProcessingEvent` has no
`StoreId`, and a test enforces that. `terminal_id` stays caller-supplied. `NoResult` is the
void result. **Rejected.** A logging decorator on every command, which would guess
purpose, basis and subject. Found on the way: Domain had shipped
`Microsoft.EntityFrameworkCore.SqlServer` unused. It was removed, and
`Domain_ships_no_package_outside_the_allowlist` now reads the deps file as well as the IL.
**On failure the executor calls `IUnitOfWork.Discard()`** (it clears EF's change tracker).
Withholding the commit is not enough: staged entities stay tracked, and the next commit on
the same context would write them, a failed operation's log row included.
`CommandExecutorPersistenceTests` proves it on a real database.

---

## Synthetic generator

### D-046 — The synthetic store generator (W10)
The fake épicerie: about 400 variants, 8 categories, 5 suppliers, 3 staff, and a year of
trading with weekly and seasonal shape, Ramadan and payday effects, expiry, spoilage,
stockouts and returns. It is configurable by store type, history length and connectivity,
and deterministic from a seed. Anything aimed at *evaluating* the engine belongs to a
later phase. Prerequisites: W1–W3.

**Configuration.** One config file holds store type, months, catalogue name, connectivity
and master seed. Every behavioural number lives in it, each with a
`source: guess | literature | interview`.

**Catalogue (pluggable).**
- `ICatalogueSource` plus `FileCatalogueSource` supply categories, variants, units, VAT
  class, suppliers, staff, terminals, opening hours and price history. The generator
  hardcodes none of it.
- Ships with a `grocery-dz` catalogue.
- Variants carry base rate, shelf life, seasonality profile and substitutability tier;
  profiles are defined in config.
- Validation on load fails loudly: every reference resolves, every supplier has a delivery
  day, ids are unique.

**Randomness.** No shared sequential RNG. Draws come from `Draw(stream, coords…)`, a hash
of the master seed and the coordinates. Fixed streams: `demand(variant, day)`,
`arrivals(day, slot)`, `lead(supplier, order)`, `spoil(batch)`, `error(staff, day, i)`. All
ids come from `SeededIdGenerator`.
**Before building W10:** today `SeededIdGenerator` advances 1 ms per id from `clockStart`
and ignores the simulated clock, so a generated year would get ULID timestamps packed into
its first minutes. Rework it to read the generator's `TimeProvider`, staying monotonic
within a tick, so ids carry the simulated time.

**Demand.** A base rate multiplied by weekday, month, Ramadan, payday and promotion
effects.
- Ramadan follows real calendar dates, in three phases: a stock-up week; an inverted daily
  rhythm with a pre-iftar peak and a late second wave; then an Aïd spike and a quiet week.
  The category mix shifts per profile.
- Payday: a spike over roughly the last 2 and first 5 days of the month, and a mid-month
  trough. `on_account` payments rise in the week before payday.

**Inventory.**
- FIFO batches, with `batch_id` on transaction lines.
- Expiry by shelf life, with write-offs recorded as spoilage.
- Purchase orders are received after a drawn lead time and fill rate.
- Stockouts happen naturally: sales stop until the next delivery.

**Ordering rule.** `NaiveShopkeeperPolicy` is its own class, called by the day loop.
- Deliberately mediocre: reorder below an eyeball threshold, round quantities up, order
  only on delivery days, over-order before Ramadan, ignore slow movers until they hit
  zero.
- Every future improvement claim is measured against it.

**Transactions.**
- Footfall comes from day intensity; basket sizes have a mode of 1–3 and a long tail.
- Payment mix: cash, card, wallet, store credit, on account.
- Refunds link through `original_transaction_id`.
- Voids carry reason codes, and every discount carries `discount_reason_code`.
- Mis-scans happen.
- Daily cash sessions, with occasional discrepancies.
- Tender rounding follows D-034.
- Periodic stock counts, with discrepancies.

**Connectivity.** `always-on`, `flaky` or `offline-stretch` affects only
`outbox`, `inbox` and `sync_state`, never sales.

**Outputs.**
- The database.
- A **latent-demand CSV** per variant-day, written beside the database and never loaded
  into it.
- A run manifest: seed, config and counts.

**Review.** Output distributions, not code, plus these pass/fail invariants:
- no variant sells more than it received;
- `closing == opening + Σdeltas` per variant per day;
- `ht + tva == line_ttc` under both policies;
- every completed transaction has an `invoice_number`;
- every discount has a reason;
- `expiration_date >= received_date`;
- two runs with the same seed are identical, **compared as a canonical dump** (every table's
  rows, sorted by primary key) rather than as file bytes. Page layout and SQLCipher's
  random salts differ between byte-identical logical contents, so a byte comparison would
  break the day the database key lands (O-20);
- a second catalogue generates a valid year with no code change.

**Outside the code.** Two afternoons with épiciers turn a dozen parameters from `guess`
into `interview`. Resolves O-17.

---

## Hardware

### D-052 — The fake hardware is the real driver with a different sink (W11)
- `IReceiptPrinter.PrintAsync(ReceiptDocument)` describes the paper, with amounts as
  `Money`. One `EscPosEncoder` turns it into bytes. `FileEscPosSink` appends the real byte
  stream to `.escpos` and a readable rendering to `.txt`. A serial sink changes nothing
  above it.
- `EscPosPrinter` implements `IReceiptPrinter` and `ICashDrawer`, because the drawer is a
  solenoid on the printer.
- `OpenAsync(reason)` has no parameterless overload, and an undefined reason is refused:
  no-sale opens are the shrinkage audit point.
- `KeyboardWedgeScanner` separates a scan from typing on a 50 ms inter-character window,
  with an injected clock.
- On amount lines the label is truncated, never the figure. A currency with an exponent
  other than 2 refuses to print.
- Every `EscPos` constant carries its Epson command name, for Hakim's verification.

**Receipts print French in ASCII.** The encoder is ASCII-only, so accents and Arabic are
out. Arabic needs a code page per printer, pre-shaping and bidi ordering, none of it
verifiable without the cohort's printers. Options are considered in Phase 1.

---

## Testing

### D-018 — Hand-written assertion messages
Every assertion message states the rule broken and why it matters, so a 2 a.m. failure
explains itself. Made permanent by D-047.

### D-047 — Plain xUnit `Assert`, no assertion library
FluentAssertions 8.x is commercially licensed, and plain `Assert` has served every suite
so far. Resolves O-5.

---

## Open — waiting on Hakim

| # | Question | Why it can't be defaulted | Blocks |
| :---- | :---- | :---- | :---- |
| O-18 | **Who sets the ACL on `%ProgramData%\Waymark\keys`, and where does the install id live?** | LocalMachine DPAPI is unwrappable by any local process; the ACL is the real control, and `ProgramData` grants Users write by inheritance. Setting it needs Windows ACL APIs in the key's project (an architecture change), and it collides with the installer's Modify grant on `data\` (D-013). An install id in a world-readable `config\` adds nothing (D-051) | **Deferred to the post-Phase 0 revision.** Nothing in Phase 0 code. The DPIA describes the control as in place |
| O-20 | **When does the database key land, and how does W10 stay deterministic on an encrypted file?** | D-042's database key is unimplemented, so StoreServer creates `waymark-store.db` **in plaintext**. That misses the Phase 0 done-criterion "encrypted", and DPIA §5.4 says the store DB is encrypted at rest. SQLCipher uses random per-page salt and IV, so D-046's "byte-identical runs" cannot hold on an encrypted file. Options: implement the key now and define generator determinism as a logical dump; or defer the key to Phase 0.5/1 and record the gap | **Deferred to the post-Phase 0 revision.** Not W10, provided its determinism test compares a canonical dump rather than file bytes (see D-046) |

### Resolved
| Open | Resolved by |
| :--- | :--- |
| O-1 | D-038 |
| O-2 | D-015, D-042 |
| O-3 | D-039, D-043 |
| O-4 | D-031…D-037 |
| O-5 | D-047 |
| O-6 | D-021 |
| O-7 | closed by D-029 |
| O-8 | D-023 (FK indexes suppressed) |
| O-9 | D-019 |
| O-10 | D-034 |
| O-11 | D-033 |
| O-12 | D-039 (per tenant) |
| O-13 | D-039 (128 bits, base32) |
| O-14 | D-036 (no Money split) |
| O-15 | D-044 |
| O-16 | D-045 |
| O-17 | D-046 |
| O-19 | Dropped: receipts are French in ASCII; alternatives in Phase 1 (D-052) |
| O-21 | D-045 (`trg_processing_log_no_update`) |
| O-22 | D-053 |
