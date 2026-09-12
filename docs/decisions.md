# Decision log

Every non-obvious choice, one paragraph: **what**, **why**, **what was rejected**
(CLAUDE.md §7.3). Newest last. A decision that later turns out wrong is
superseded by a new entry, never edited away — the reasoning that failed is the
part worth keeping.

Entries are numbered `D-NNN`. Phase in brackets.

---

## D-001 — Monorepo, four top-level surfaces [Phase 0]

**What.** One repository: `/src` (the .NET solution), `/waymark-admin`
(TypeScript), `/waymark-engine` (Python), `/docs`, plus `/learning` and
`/tools`.

**Why.** The recommendation envelope is defined once in `Waymark.Contracts` and
consumed by C#, TypeScript and Python. Split repositories would make that
contract a versioned dependency between three release cycles, for a project
with one developer. A single commit that changes the envelope and all three
consumers together is worth more than independent versioning nobody needs yet.

**Rejected.** Separate repos per surface — real isolation, but the coordination
cost lands entirely on one person. Reconsider only if the cloud side grows a
separate deployment cadence.

---

## D-002 — `Learning stage/` renamed to `learning/` [Phase 0]

**What.** The learning-stage work (Polars, ISL, FPP3, OR) moved from
`Learning stage/` to `learning/`, keeping git history through the rename.

**Why.** A space in a path breaks unquoted shell commands, CI globs and
Windows/POSIX tool interop in ways that cost minutes repeatedly and teach
nothing. The directory stays in the repository because the forecasting, stat
learning and inventory modules there are the reference implementations the
engine will be checked against.

**Rejected.** Deleting it (the modules are load-bearing reference), and moving
it to its own repository (it would stop being consulted).

---

## D-003 — Central package management [Phase 0]

**What.** `src/Directory.Packages.props` holds every NuGet version;
project files name packages without versions.

**Why.** Twelve projects that each pin their own version is twelve chances for
`Waymark.Persistence` and `Waymark.Integration.Tests` to disagree about EF Core
and produce a binding failure that only shows up at runtime. Central management
makes a version bump one reviewed line.

**Rejected.** Per-project versions — the .NET default, and fine for three
projects. Not for twelve.

---

## D-004 — Target framework pinned once, in `Directory.Build.props` [Phase 0]

**What.** `net10.0` — the current LTS — set once for all twelve projects, with
`Nullable`, `ImplicitUsings` and `TreatWarningsAsErrors` alongside it.

**Why.** The store-side software runs unattended on a shop's Windows 10 till
for years between visits; LTS is the only defensible choice. Warnings as errors
is deliberate: this codebase's characteristic failure is *silent* — the code
runs, the screen looks right, the number is wrong — so a warning that is
allowed to accumulate is exactly the signal being discarded.

**Rejected.** Per-project frameworks, and STS releases.

---

## D-005 — Architecture tests live in `Waymark.Integration.Tests` [Phase 0]

**What.** The NetArchTest rules enforcing CLAUDE.md §2.1 sit in the existing
integration test project rather than a fourth `Waymark.Architecture.Tests`.

**Why.** Architecture tests must reference every assembly to inspect it. Putting
them in `Domain.Tests` would give the domain test project a reference to
Persistence and Sync, which is the exact coupling the tests exist to forbid.
Integration tests already reference everything legitimately, so the rules cost
no new edges in the reference graph.

**Rejected.** A fourth test project — cleaner conceptually, but the build plan
names three and nine projects for one person is already a recorded risk.
**Open:** worth revisiting if the architecture rules grow past one file.

---

## D-006 — `AssemblyMarker` in every library [Phase 0]

**What.** Each of the seven library projects contains a single empty
`public sealed class AssemblyMarker`.

**Why.** The architecture tests load assemblies by naming a type inside them. An
assembly with no types cannot be named, and NetArchTest reports *success* over
an empty type set — so without a marker, every architecture rule passes
vacuously on a project that has not been written yet. That is the one failure
mode an architecture test must not have: green because it inspected nothing.

**Rejected.** `Assembly.Load` by string name (fragile, and silently returns
nothing when the project is not copied to the test output directory).

---

## D-007 — The POS shell is built in code, not XAML [Phase 0]

**What.** `Waymark.Pos` has a code-only `App` class and no `.axaml` files.

**Why.** There is no till UI to design yet, and the placeholder's only job is to
make the solution build and give Phase 0.5 somewhere to land. Code-only removes
the XAML compilation pipeline from a shell that will be replaced wholesale.
Phase 1 introduces `.axaml` when there are actual screens.

**Rejected.** The standard Avalonia template — it brings markup, a
`ViewLocator` and a `ViewModelBase` for a window containing one sentence.

---

## D-008 — `*.db` is ignored globally, with no exception [Phase 0]

**What.** `.gitignore` excludes `*.db`, `*.db-wal`, `*.db-shm`, `*.sqlite`.

**Why.** `waymark-identity.db` holds the `customer_id ↔ pseudonym_key` mapping
and must never leave the machine that created it (CLAUDE.md §3.4). A per-file
ignore would work until someone named a file slightly differently; a
blanket pattern cannot be got round by accident. Every database in this project
is generated — from migrations or from the synthetic store generator — so
nothing is lost.

**Rejected.** Ignoring the identity file by exact name. The blast radius of
being wrong is a regulator's question, not a merge conflict.

---

## D-009 — NuGet audit findings are build errors [Phase 0]

**What.** `TreatWarningsAsErrors` promotes NU1903 (known vulnerability in a
package) to an error, and the first restore failed on five of them:
`SQLitePCLRaw.lib.e_sqlite3` 2.1.11, `System.Security.Cryptography.Xml` 9.0.0
and `Tmds.DBus.Protocol` 0.20.0, all transitive. Every pin was moved to the
current release, which cleared all five.

**Why.** Keeping it. This software holds money and personal data on a machine
in a shop that nobody visits for months. A vulnerable transitive dependency is
precisely the failure this project is most exposed to and least likely to
notice — the code runs, the screen looks right. When a finding appears, the fix
is a version pin in `Directory.Packages.props`, never a suppression.

**Rejected.** `<NuGetAudit>false</NuGetAudit>`, and setting
`NuGetAuditLevel` to `critical`. Both make the first inconvenient finding
disappear along with every later one.

**Note.** The initial pins were written without a network check and were all
stale by several months. Package versions in this file are only trustworthy
after a restore has run.

---

## D-010 — Avalonia stays on 11.3, not 12.x [Phase 0]

**What.** Avalonia pinned at 11.3.20 though 12.1.1 is released.

**Why.** The till runs unattended on a shop's Windows 10 machine for long
stretches, and onboarding happens in person, alone, on unfamiliar hardware — a
UI framework regression costs a customer and a day of travel. 12.x is weeks
old. `Avalonia.Diagnostics` having no 12.x release at all says the ecosystem
agrees it is early.

**Rejected.** 12.1.1 — reconsider once it has a track record and the
diagnostics package ships.

---

## D-011 — CA1707 is off inside `src/tests` [Phase 0]

**What.** Analyzer CA1707 (no underscores in member names) is disabled for test
projects only.

**Why.** Test names here are sentences —
`Sync_must_not_reference_Pseudonymisation`. When a test fails, the runner
output should read like the rule that was broken, without anyone opening the
file. CA1707 is correct everywhere else and stays on.

**Rejected.** Renaming the tests to PascalCase, and disabling CA1707 solution-wide.

---

## D-012 — The architecture tests were verified by breaking them [Phase 0]

**What.** A `ProjectReference` from `Waymark.Sync` to
`Waymark.Pseudonymisation` was added deliberately, along with a type that used
it, and the suite was run. Both boundary tests failed with their intended
messages. The reference was then reverted.

**Why.** The build plan's definition of done says the architecture tests must
*fail* when a forbidden reference is added. A green architecture test proves
nothing on its own — NetArchTest reports success over a type set it could not
find, so an untested rule and a rule that cannot fire look identical. This is
the same reasoning as D-006, applied to the suite rather than the assemblies.

**Consequence.** Repeat this whenever a rule is added. A rule that has never
been seen to fail is not yet a rule.

---

## D-013 — The databases live in `C:\ProgramData\Waymark`, never beside the executable [Phase 0]

**What.** Proposed layout on a store machine, pending Hakim's confirmation of
the `identity\` half:

```
C:\ProgramData\Waymark\
├─ data\        waymark-store.db (+ -wal, -shm)
├─ identity\    waymark-identity.db — own ACL, never leaves the machine
├─ pos-cache\   POS Level-2 cache
├─ backups\     nightly local backup staging
├─ logs\
└─ config\
```

Resolved from `Environment.SpecialFolder.CommonApplicationData` and overridable
through `Waymark:Storage:DataDirectory`, so development does not write to
`ProgramData`. The identity directory is **not** in that shared options class —
only `Waymark.Pseudonymisation` binds it (CLAUDE.md §3.4).

**Why.** Five reasons, each a way a till loses a shop's history:

1. *It survives an update.* A self-contained publish is a folder replaced
   wholesale during onboarding or an upgrade. Data inside that folder dies with
   it, and the deployment model here — in person, alone, on unfamiliar
   hardware — is exactly when nobody notices until it is too late.
2. *`Program Files` is read-only to non-admin accounts*, so "beside the exe"
   does not work there even before the update problem.
3. *It is machine-scoped, not user-scoped.* StoreServer runs as a service;
   under LocalSystem `%LOCALAPPDATA%` resolves to
   `C:\Windows\System32\config\systemprofile\AppData\Local`, which is writable
   but invisible to a shopkeeper doing a restore. Multiple cashier logins would
   each get a separate database.
4. *It is never OneDrive-redirected.* `Documents` and `Desktop` frequently are
   on retail Windows machines, and OneDrive syncing an open SQLite file is one
   of the most reliable ways to corrupt a WAL database.
5. *WAL needs a writable directory, not just a writable file.* SQLite creates
   and deletes `-wal` and `-shm` beside the database, so the directory ACL is
   what matters. For the same reason the database must never sit on a mapped
   drive or SMB share — WAL does not work over network filesystems, which rules
   out writing directly to the backup NAS.

`identity\` is a separate directory rather than a sibling file so the cloud
backup job can exclude a *directory*. Same reasoning as D-008: a rule that
cannot be got round by accident beats one that depends on matching a filename.

**Installer consequence, verified on this machine.** `C:\ProgramData` grants
`BUILTIN\Users` only `Write` with `ContainerInherit` — an account can create
files there but gets `ReadAndExecute` on files created by another account. The
installer must explicitly grant Modify on `C:\ProgramData\Waymark\data`, or the
second Windows account to open the till gets a read-only database and an
unhelpful error.

**Rejected.** Beside the executable (dies on update); `%LOCALAPPDATA%`
(user-scoped, invisible under a service account); `Documents` (OneDrive); a
network share (WAL does not work there).

**Portability note.** `CommonApplicationData` maps to `/usr/share` on Linux,
which is root-owned. Development on a non-Windows machine needs the override,
not a fallback path baked into the code.

---

## D-014 — `Microsoft.EntityFrameworkCore.Design` is referenced twice [Phase 0]

**What.** The package is referenced by `Waymark.Persistence` *and* by
`Waymark.StoreServer`, both with `PrivateAssets="all"`.

**Why.** The EF tools resolve against the **startup** project, not the project
holding the `DbContext`. Persistence marks the package `PrivateAssets="all"`,
which is correct — it must not flow to anything that references Persistence —
but that is exactly why StoreServer could not see it and `dotnet ef` refused to
run. Both references are design-time only and neither reaches a published build.

**Rejected.** Dropping `PrivateAssets` in Persistence, which would leak a
design-time dependency into every consumer including the test projects.

---

## D-015 — One SQLite provider for the process, and it is SQLCipher [Phase 0]

**Resolves O-2.**

**What.** `Waymark.Persistence` now references
`Microsoft.EntityFrameworkCore.Sqlite.Core` plus
`SQLitePCLRaw.bundle_e_sqlcipher`, rather than
`Microsoft.EntityFrameworkCore.Sqlite`. The plain package hard-depends on
`bundle_e_sqlite3`, and only one bundle may supply the native library in a
process. Verified after the change: `Waymark.StoreServer` output carries
`provider.e_sqlcipher.dll` and `e_sqlcipher.dll` alone.

`waymark-store.db` is opened **without** a key and is therefore an ordinary
unencrypted SQLite file — confirmed by reading its first sixteen bytes, which
are the literal string `SQLite format 3`. It stays readable by the stock CLI
and any SQLite tool. Only `Waymark.Pseudonymisation` supplies a key, and only
for `waymark-identity.db`, whose header is encrypted noise and which refuses to
open without the key.

**Why.** Two SQLite bundles in one process is not a configuration that fails
loudly. It links, it builds, it starts; whichever `Batteries_V2.Init()` runs
first wins, and the failure surfaces when a file is opened against the wrong
provider. Making the provider singular and explicit removes the ambiguity
rather than relying on load order.

**The cost, measured rather than assumed.** SQLCipher 4.5.2 community is built
on **SQLite 3.39.2**, against 3.53.3 for the stock bundle — about fourteen
releases behind. Probed directly:

| Available at 3.39.2 | Not available |
| :---- | :---- |
| STRICT tables, and they do reject a REAL into an INTEGER column | JSONB (3.45) — the binary JSON representation |
| `RETURNING`, generated columns, `ALTER TABLE DROP COLUMN` | |
| `->` and `->>` JSON operators, `json_extract`, `json_group_array` | |
| UPSERT, window functions, partial indexes, deferred foreign keys | |
| `unixepoch()`, `ceil`/`floor` and the rest of the math functions | |
| FTS3/4/5, RTREE, `ENABLE_COLUMN_METADATA`, `THREADSAFE=1` | |

So the schema loses exactly one thing worth naming: JSONB. Text `json` plus the
`->>` operator covers every use this project has, and the parameter registry and
recommendation envelope are small documents where the binary form would save
nothing measurable.

**Rejected.** `bundle_zetetic` (the commercially licensed SQLCipher build —
revisit if the community build's version lag becomes a problem, since it is a
package swap and not a schema change). Two processes, one per database — real
isolation, absurd for a single till. Keeping both bundles and hoping the right
`Init()` wins.

**Consequence for the schema.** Write against a **3.39.2** floor. Declare money
and stock tables `STRICT` so `INTEGER`/`TEXT` is enforced by the engine rather
than by review — this is CLAUDE.md §3.1 made mechanical, and it is the single
most valuable thing this version still gives us.

---

## D-016 — `schema_v7_1.sql` creates v1; EF migrations evolve it from a baseline [Phase 0]

**Superseded in part by D-019 (08/09/2026).** The cost analysis below stands and
was reproduced; only the *mechanism* was wrong. `--ignore-changes` does not
exist in EF Core — it is an EF6 parameter. Costs 3, 4 and 5 and the
schema-fidelity test carry forward unchanged into D-019.

**Why not the other two.** Option A — the `.sql` stays canonical forever and
every upgrade is hand-written — means writing SQLite's twelve-step table
rebuild by hand for most column changes, repeatedly, alone, for years. Option
B — EF becomes canonical and the `.sql` becomes history — means re-expressing
11 triggers and 139 indexes that are already written, reviewed and validated on
3.39.2, and replacing one readable artifact with 58 configuration classes that
nobody can read as a whole. C keeps the reviewed v1 and buys automation for
everything after it.

### The costs, and what pays for each

Every one of these was reproduced rather than predicted.

**Cost 1 — the two-step bootstrap is fragile.** A new install must run the
`.sql` *and* stamp the baseline row. Get the migration id wrong by one
character and EF tries to create tables that already exist; the install fails.
This happened on the first attempt at reproducing it.

*Paid by:* one code path, not a documented procedure. A bootstrapper in
`Waymark.Persistence` that, on a missing database file, executes the schema
from an **embedded resource**, writes the baseline row, then calls `Migrate()`;
on an existing file, calls `Migrate()` alone. The `.sql` ships inside the
assembly so it cannot be missing or stale. Phase 0's definition of done already
requires a test that both databases are created from empty — that test is this
mitigation.

**Cost 2 — `--ignore-changes` asserts that the model matches the database; it
does not check.** If an entity configuration differs from the `.sql` by one
column, nothing complains until a query fails in a shop.

*Paid by:* the schema-fidelity test below.

**Cost 3 — tables created by later migrations would not be STRICT**, leaving a
database where the original 58 tables enforce types and newer ones silently do
not.

*Paid by:* `StrictSqliteMigrationsSqlGenerator`, a ~30-line override of
`SqliteMigrationsSqlGenerator.Generate(CreateTableOperation, …)`. Verified to
emit `STRICT` on plain creation **and** on the table-rebuild path EF uses for
`ALTER`. Registered with `ReplaceService<IMigrationsSqlGenerator, …>`.

**Cost 4 — the override also stamps EF's own `__EFMigrationsHistory`.**
Harmless today, since both its columns are `TEXT`, but it is EF's table and its
shape is not ours to change.

*Paid by:* excluding that table by name in the override, and exempting it from
the all-tables-STRICT assertion.

**Cost 5 — the dangerous one. An EF table rebuild silently drops every index
and trigger EF does not know about.** SQLite's `DROP TABLE` takes the table's
triggers with it, and EF's rebuild recreates only what is in its model.
Reproduced directly: a table created from `.sql` with `ix_cash_store` and an
append-only `trg_cash_no_delete` went through one rebuild migration and came
out with both **gone**. EF printed `Done.` and reported no warning. An
append-only guard on cash movements disappearing without a message is precisely
the silent-failure class this project is built to avoid.

*Paid by three things together:*

1. **Every index declared in the EF model**, so EF recreates them itself. This
   is mechanical and the scaffolder supplies them.
2. **Triggers extracted into their own `triggers.sql`**, re-applied
   idempotently (`DROP TRIGGER IF EXISTS` then `CREATE TRIGGER`) after every
   `Migrate()`. Triggers are pure DDL, so re-application is safe and cheap, and
   it makes trigger loss structurally impossible rather than remembered.
3. **The schema-fidelity test**, below, which fails if the inventory changes.

### The schema-fidelity test — the load-bearing mitigation

One test pays for costs 2 and 5 and catches the case nobody thought of. It
builds two databases in a temporary directory: one created by
`schema_v7_1.sql`, one created by running every migration forward from empty.
It then compares them and fails on any difference in:

- table names, and the `strict` flag on each
- column names, declared types, nullability, default values
- index names and their columns
- trigger names
- foreign key definitions

Green means the two paths agree. Red means the model and the schema have
drifted, or a rebuild dropped something — reported at build time rather than
discovered in a shop.

**Recorded risk.** C is two mechanisms where B is one, and two mechanisms is
how a tired solo developer gets confused. The mitigation for that is not
technical: it is that the bootstrapper is the only supported way to create a
database, and the fidelity test is the only thing that decides whether the two
mechanisms still agree. If either is bypassed, this decision stops being safe
and should be revisited toward B.

---

## D-017 — CI runs on `windows-latest` [Phase 0]

**What.** The GitHub Actions workflow builds and tests on Windows, not Ubuntu.

**Why.** The till is a Windows 10 machine, `Waymark.Pos` is a `WinExe` with an
application manifest, and the databases live under `%ProgramData%`. CI should
fail the way a store fails, not the way a Linux container does. Ubuntu would be
faster and cheaper per minute and would catch cross-platform mistakes, but
those are not mistakes this product can make.

**Rejected.** `ubuntu-latest`, and a matrix of both — the second runner would
double the minutes to test a platform nothing ships to.

**Note.** `dotnet test` over the solution prints "No test is available" for
`Waymark.Domain.Tests` and `Waymark.Application.Tests`, which are still empty.
Verified that the run still exits 0, so CI is green. When those projects gain
their first tests the message goes away on its own.

---

## D-018 — Tests use plain xUnit `Assert` until O-5 is settled [Phase 0]

**What.** No assertion library. Failure messages are written by hand, as the
second argument to `Assert.True`.

**Why.** O-5 is open: FluentAssertions 8.x is commercially licensed and Waymark
is a commercial product, and the alternatives have not been picked. Writing 40
tests against a library that then gets swapped is work done twice. Plain
`Assert` commits to nothing.

**Consequence, and it turned out to be a feature.** Hand-written messages
forced each assertion to say what rule was broken and why it matters — "STRICT
is what makes the decimal rule mechanical", "an EF table rebuild drops triggers
it does not know about". A fluent assertion would have produced a diff of two
lists. When one of these fails at 2 a.m. months from now, the message is the
explanation.

---

## D-019 — The initial migration is the schema; `Migrate()` is the only creation path [Phase 0]

Supersedes the mechanism of D-016. Dated 08/09/2026.

**What.** schema_v7_1.sql is pasted inline into the Up() of the initial migration as migrationBuilder.Sql(...). Migrate() therefore creates a complete v1 database from empty. There is no separate bootstrap step and no baseline row to stamp by hand.

Triggers are not in that SQL. They live in triggers.sql, applied idempotently after every Migrate() — see cost 5 of D-016, which is unchanged.

**Why** the mechanism changed. D-016 assumed dotnet ef migrations add --ignore-changes produced an empty migration with a populated model snapshot. That parameter is EF6 only; EF Core has never had it. The real procedure is to generate the migration and hand-empty its Up(), and once the file has to be hand-edited anyway, pasting the schema into it costs nothing extra and removes an entire mechanism.

**What it buys,** against D-016's own recorded costs.

Cost 1 — the two-step bootstrap is fragile — disappears. There is no second step. No baseline id to get wrong, no branch on whether the file exists. D-016 recorded that this failed on the first attempt at reproducing it; the failure mode no longer exists.

The recorded risk of D-016 — "C is two mechanisms where B is one, and two mechanisms is how a tired solo developer gets confused" — is retired. Creation and evolution are now the same call.

**What it costs.**

The schema exists in two artifacts: schema_v7_1.sql and the pasted copy inside the migration. They are identical at v1 and must never both be edited. schema_v7_1.sql is frozen at the moment the baseline is generated and becomes historical. Every change after that is a migration.

The initial migration file is roughly 1,200 lines of SQL held in a string. Ugly, and honest about what it does.

Rejected. Reading schema_v7_1.sql from an embedded resource inside Up() rather than pasting it. A historical migration whose meaning changes when a file changes is the classic migration trap: replaying migration 1 two years from now would produce a different database than it did on the day it ran.

Two runtime details that are not optional.

`Migrate()` must not run inside an ambient transaction. From EF Core 9, Migrate starts its own transaction and uses an ExecutionStrategy; an external transaction raises MigrationsUserTransactionWarning and throws.
dotnet ef database update is still not a creation path for developers who have edited a migration by hand. Under C′ it happens to work, because Up() is populated — but the supported path remains the application's own startup call, so that ApplyTriggers() runs with it. A database created by the CLI alone has no triggers.

Readable current state. Because schema_v7_1.sql freezes, a schema_current.sql is regenerated from a migrated database after every migration and committed. It is never executed — it exists so the whole schema stays readable in one file, which was the point of rejecting option B in D-016.

The fidelity test changes shape. It no longer compares two creation paths, since there is only one. It now builds a database via Migrate() + ApplyTriggers() and asserts:

every table is STRICT, except __EFMigrationsHistory
the trigger inventory matches the names parsed from triggers.sql
sqlite_master, sorted and normalised, matches the committed schema_current.sql
context.Database.HasPendingModelChanges() is false

The third is the load-bearing one: it is a golden-file test, so an unintended change fails the build and an intended change appears as a diff to approve.

One-time step, and it is worth doing carefully. When the baseline migration is first generated, EF emits CreateTable operations for all 58 tables before the body is replaced. Diff that generated code against schema_v7_1.sql before deleting it. It is a free, complete, one-shot comparison of the 58 entity configurations against the reviewed schema, at the only moment it comes for nothing.

---

## D-020 — `schema_current.sql` and the invariant tests answer different questions [Phase 0]

**The question.** D-019 regenerates and commits `schema_current.sql` after every
migration. If that file always describes the current database, can the schema
tests read it instead of building a database with `Migrate()`?

**Answer: no, and both are kept.** They are not two ways of doing one job.

**`schema_current.sql` is a change detector.** It answers *"did the schema
change without anyone noticing?"* Compared as a golden file, an unintended
change fails the build and an intended one appears as a diff to approve. That
is genuinely valuable and it is why D-019 introduced it.

**The invariant tests answer a different question** — *"is this rule still
true?"* — and there are three things the file cannot tell them.

**1. Declaration is not behaviour.** Demonstrated: a trigger named
`trg_consent_events_no_delete`, on the right table, with `RAISE(ABORT)` and the
right message, but carrying a `WHEN OLD.action = 'granted'` clause picked up in
a refactor. Any check that the text contains that trigger passes. A withdrawn
consent row then deletes silently, with no error, and the append-only promise
in the DPIA is broken. Only executing the `DELETE` finds it.

The same applies to a trigger attached to the wrong table by copy-paste, or one
whose body drifts. The name and the text look right in every case.

**2. A generated file can be stale; a live database cannot.** Regenerating
`schema_current.sql` is a manual step after every migration. Skip it once and
the file describes the previous version while the tests stay green — the museum
piece problem, moved one step along rather than solved.

**3. It can bake in an absence.** D-019 records that `dotnet ef database update`
alone produces a database with no triggers, because `ApplyTriggers()` did not
run. Regenerate the file from *that* database and the committed documentation
records zero triggers as correct. Every later text check then agrees, forever.

**Decision.** The invariant and append-only suites build a real database with
`Migrate()` + `ApplyTriggers()` and assert against it. The golden-file
comparison against `schema_current.sql` stays as D-019 specified. Keeping both
costs one `Migrate()` per test class.

**Consequence.** The 29 tests written under D-016 currently load
`schema_v7_1.sql`. Once that file freezes they are testing a historical
artifact, and they will keep passing while doing it. Repointing them at
`Migrate()` is not optional tidying — it is what stops them becoming decorative.

---

## D-021 — One set of entity classes: Domain owns them, Persistence maps them [Phase 0]

**Resolves O-6.** Decided 08/09/2026.

**What.** Entities live in `Waymark.Domain`, as plain classes with no
attributes, no EF Core reference and no knowledge of how they are stored.
`Waymark.Persistence` holds one `IEntityTypeConfiguration<T>` per entity,
discovered by `ApplyConfigurationsFromAssembly`. There is no second set of
persistence models.

**Why.** Two sets would be 116 classes and a mapping layer between them, for
one developer, against a nine-project structure already recorded as a risk in
diagram 05. The property the separation is supposed to buy — Domain not knowing
about the database — is bought here by the configuration living in Persistence
instead. The reference graph enforces it: `Waymark.Domain` has zero
dependencies, so an entity *cannot* acquire a `[Table]` attribute without the
architecture tests failing.

**Rejected.** Separate domain and persistence models, and entities in
Persistence with EF attributes (which would cross §2.1 outright).

**Also settled.** The context is `WaymarkDbContext` in namespace
`Waymark.Persistence` — not `AppDbContext` in `Persistence`. Options arrive
through the constructor rather than `OnConfiguring`, because the database path
is configuration (D-013) and a context that builds its own connection string
cannot be pointed at a test directory.

`WaymarkDbContextDesignTimeFactory` exists because the EF tools construct a
context from the startup project's DI container, and `Waymark.StoreServer`
registers nothing yet. Without it every `dotnet ef` command fails. It points at
a scratch path, never the real one, so a mistyped command cannot reach a
store's database — and it registers the STRICT generator, which is what puts
`STRICT` into the migrations the tools generate.

---

## D-022 — An EF table rebuild also drops CHECK constraints [Phase 0]

**Extends D-016 cost 5, which named only triggers and indexes.**

**What was found.** A table created with `CHECK (amount > 0)` was put through
one EF rebuild migration. Afterwards the constraint was gone from the DDL, and
`INSERT ... amount = -5` succeeded. EF reported success and warned about
nothing.

The mechanism is the one already recorded for indexes and triggers: EF rebuilds
the table from its model, and whatever the model does not declare does not come
back. Triggers can be re-applied afterwards from `triggers.sql`. A CHECK cannot
— altering one in SQLite means rebuilding the table again — so **CHECK
constraints must be declared in the entity configurations**, not only in the
schema.

This is not a small surface. The schema uses CHECK constraints for every enum,
every boolean, and money columns that must stay positive. Losing them silently
would leave a database that accepts a negative `paid_in` and an unrecognised
`movement_type`.

**Foreign keys are in the same position** and must be declared for the same
reason. At v1 they arrive with the pasted SQL, so nothing is broken today, but
every relationship has to be configured as its entities land or the first
rebuild of a table quietly loses its references.

**Suggested addition to CLAUDE.md §3.7**, for Hakim to make or reject:

> Every index, CHECK constraint and foreign key must be declared in the EF
> model. A table rebuild recreates the table from the model alone, and anything
> not declared is dropped without a warning. Triggers are the exception: they
> live in `triggers.sql` and are re-applied after `Migrate()`.

**One more thing the worked example surfaced.** EF creates an index for every
foreign key of its own accord. `units_of_measure.base_unit_code` gained
`IX_units_of_measure_base_unit_code`, which the hand-written schema does not
have. Across 58 tables that will be dozens of indexes the schema never asked
for. They are usually harmless and often useful, but they are a real difference
that will show up in the D-019 one-time diff, and the choice — accept them or
suppress them — should be made once and deliberately rather than 40 times.

---

## D-023 — The 58 entities were generated, not typed [Phase 0]

**What.** `tools/generate-model` read the database built from
`schema_v7_1.sql` and wrote 58 entities, 70 enums, 58 configurations and the
`DbContext`. It is a one-shot: the schema is evolved by migrations from here
(D-019), so the generator is kept for provenance and never re-run.

**Why generate rather than write.** 625 columns, 137 foreign keys, 68 indexes
and about 150 CHECK constraints. Typing that is not craft, it is a transcription
exercise with roughly six hundred chances to make an error that compiles. The
generator applies one rule per decision and applies it everywhere; the diff
below then proves the result matches the schema. Neither half is available
to hand-written files.

**The rules, each of which is a decision applied 58 times.**

*Every `INTEGER` column becomes `long`, with no exceptions* — including counts
and display orders where `int` would be perfectly safe. Uniformity is the point:
"is this column scaled or summed?" is a judgment call, and a judgment call made
625 times will be wrong somewhere. The scaffolder's `int` default overflows a
money column at 21,474,836.47 DZD, which is what the rule exists to prevent.

*Enums get explicit converters, never `HasConversion<string>()`.* The shorthand
stores the C# member name — `"Standard"` into a column whose CHECK allows only
`"standard"` — and fails on the first insert. The sync channels settle the
question anyway: their values are `A_statistics` and `D_intents`, which no
mechanical rule produces.

*Every CHECK, index and foreign key is declared in the configuration*, per
D-022.

*`required` marks only what a caller must supply* — not nullable, and no
database default.

**The result, measured.** A database built from the model's own create script
was diffed structurally against one built from the schema: 58 tables, 625
columns, types, nullability, primary keys, 137 foreign keys and the STRICT flag
on all 58. **No differences.** That comparison now runs permanently as
`ModelMatchesSchemaTests` — verified to fail by mistyping one column name.

**Naming decisions worth knowing about.**

| | |
| :---- | :---- |
| `returns` → `SalesReturn` | CA1716: `Return` is a reserved word in VB |
| `category_attributes` → `CategoryAttributeLink` | CA1711 forbids a type name ending in `Attribute` |
| `consent_events.action` → `ConsentEventAction` | `Action` collides with `System.Action` |
| `promotion_*.value_type` → `Promotion*ValueType` | collides with `System.ValueType` |
| `cash_movements.movement_type` → `CashMovementType` | not `CashMovementMovementType`; the stutter is stripped |

Enum members mirror the database vocabulary, so a data type really is called
`Integer`. CA1720 is disabled for `Domain/Enums` rather than renaming them,
because the enum's only job is to agree with the schema.

**EF added 85 indexes the schema does not have, and they were suppressed.** It
indexes every foreign key of its own accord, turning 68 declared indexes into
153. Removing `ForeignKeyIndexConvention` in `ConfigureConventions` brings that
back to 82: the schema's 68 named indexes plus the 14 UNIQUE constraints, with
nothing appearing by itself. Every remaining difference between the model and
the schema is accounted for.

The rule is now uniform, which is the part that matters more than the index
count: every index in this database is declared somewhere a person chose to
declare it. A foreign key that turns out to need one gets `HasIndex` like
anything else.

**One difference that cannot be removed.** A UNIQUE constraint written inline in
the schema produces an implicit `sqlite_autoindex_*`, while EF emits a named
`IX_table_column`. Same constraint, same enforcement, different name. EF has no
way to write an inline UNIQUE, so this is a permanent and harmless cosmetic
difference — worth knowing before it appears in the D-019 diff and looks like a
problem.

---

## D-024 — Two generator defects, and the verification that let them through [Phase 0]

**Found by Hakim while running the baseline migration**, not by any test here,
which is the part worth recording.

**Defect 1 — composite UNIQUE constraints were flattened.** The parser collected
unique columns into a flat list, so `UNIQUE(count_id, variant_id, batch_id)`
became three separate single-column constraints. That is far stricter than the
schema: it would have forbidden a variant appearing in two different stock
counts. Three constraints were affected — `stock_count_items`,
`recommendation_options` and `transaction_payments`.

**Defect 2 — partial indexes lost their WHERE clause.** All 12 of them. The
filter is not decoration; on the three that are also UNIQUE it *is* the
constraint:

| Index | Unfiltered, it would |
| :---- | :---- |
| `ux_parameter_current` | forbid the registry holding two versions of a parameter — the only thing the registry is for |
| `ux_product_category_primary` | forbid a product belonging to more than one category |
| `ux_transactions_invoice` | over-constrain invoice numbers |

Each would have rejected legitimate data at the first insert that mattered.

**Why the verification missed both, which is the real lesson.** `compare.py`
and `ModelMatchesSchemaTests` compared indexes by *name and columns*. Neither
looked at uniqueness or at the filter. So a flattened composite and twelve
dropped WHERE clauses both compared equal, and D-023 could report "no
structural differences" in good faith while two real defects sat in the model.

A comparison is only worth what it compares. Both now check index columns,
uniqueness, partial-index filters, and unique constraints as *groups* — and the
test was verified by reintroducing both defects, which it named precisely.

**Fixed at the source, not in the migration.** Hakim's corrections were made by
hand in the generated migration file, which leaves the configurations still
wrong — the drift O-7 describes. Correcting the generator and regenerating
produced a migration that reproduces every one of those corrections, plus three
things the hand edit had lost: `ix_count_items_count`,
`ix_payments_transaction` and `ix_rec_options_rec`, all real indexes in the
schema. The hand edit had also reversed the column order of the
`stock_count_items` unique constraint to `(batch_id, variant_id, count_id)`;
the schema's order is `(count_id, variant_id, batch_id)`, and order decides
which queries an index can serve.

**Also fixed:** the design-time factory did not create its own directory, so
every `dotnet ef database update` failed with "unable to open database file",
which does not say so.

---

## D-025 — `triggers.sql`, `ApplyTriggers()`, and one entry point that cannot forget [Phase 0]

**Closes the last piece of D-016 cost 5.**

**What.** The eleven append-only triggers were extracted verbatim from section 13
of `schema_v7_1.sql` into `src/Waymark.Persistence/triggers.sql`, embedded in
the assembly beside the schema. Every statement is `DROP TRIGGER IF EXISTS`
then `CREATE TRIGGER`, so the file is idempotent: applying it to a database
that has the triggers is a no-op, and applying it to one that has just lost
them restores them.

`WaymarkDbContext.MigrateAndApplyTriggers()` is the only supported way to bring
a database up to date. It migrates, applies the triggers, and then **refuses to
return** if any are still missing.

**Why the method is named that way.** `Migrate()` alone leaves a database with
no audit guards, and that failure is silent — the till works, the reports look
right, and `consent_events` quietly accepts an UPDATE that the DPIA says is
impossible. A method called `MigrateAndApplyTriggers` makes anyone reaching for
`Database.Migrate()` stop and wonder why there are two. That is the entire
design: the same reasoning as `UseWaymarkSqlite`, where two ways to configure a
context and one of them silently wrong was the problem worth removing.

Throwing on a missing trigger is deliberate. A database that will not open is a
visible failure someone fixes in minutes. A database that accepts writes the
regulator was told are impossible is a failure discovered by the regulator.

**Ambient transactions are refused with a message that points here.** EF Core 9
and later start their own transaction for migrations and use an execution
strategy; an ambient one throws from deep inside `Migrate` about retrying
execution strategies, which says nothing about the actual mistake.

**The test fixtures were split, which was D-020's stated consequence.**
`ReviewedSchemaFixture` builds from the frozen `schema_v7_1.sql` — the artifact
a human approved. `MigratedDatabaseFixture` builds the way a store gets it,
`Migrate()` then `ApplyTriggers()`. Every suite that asserts a *rule* now uses
the second, so the rules are checked against what actually reaches a till
rather than against a file that is never executed again.

`ModelMatchesSchemaTests` compares the two, and therefore now also compares
triggers — which makes it the thing that proves `ApplyTriggers()` ran at all.

**Verified by removing a trigger from the script.** Seven tests failed from four
independent directions: the fidelity comparison reported it missing after
`Migrate() + ApplyTriggers()`, the named-trigger check reported it absent, and
three count assertions disagreed. Restored, 52 tests green.

**Consequence for the invariant suites.** They now run against a migrated
database, which contains EF's own `__EFMigrationsHistory` and
`__EFMigrationsLock`. Those are excluded: their shape is EF's business, and the
lock table has exactly the rowid-alias primary key these rules forbid
everywhere else.

---

## D-026 — `schema_current.sql` is produced by a test, not a script [Phase 0]

**What.** `src/Waymark.Persistence/schema_current.sql` is a canonical rendering
of the live schema — 58 tables, 81 indexes, 11 triggers, each sorted by name.
Committed, read, never executed, and deliberately not an embedded resource.
`SchemaCurrentTests` regenerates it when `WAYMARK_UPDATE_SCHEMA_CURRENT=1` and
otherwise fails if it has drifted.

**Why a test rather than a `tools/` script.** D-019 named the hazard precisely:
regenerate this file from a database created by `dotnet ef database update`
alone and it records **zero triggers as correct**, permanently, and every later
comparison agrees with it. A script would have to remember to build the
database the right way. A test that already owns `MigratedDatabaseFixture`
cannot build it any other way — the fixture calls
`MigrateAndApplyTriggers()` and nothing else is on offer.

The failure message carries the regeneration command, so the person who hits it
does not have to find this entry to know what to do.

**Why sorted.** A diff of this file after a migration is the human-readable
summary of what the migration did, and it is worth reading before committing.
Unsorted output would show things moving as well as changing, which is how a
useful diff becomes one nobody reads.

**Rejected.** Generating it from `schema_v7_1.sql` (frozen at v1, so it would
never change), and dumping `.schema` from the CLI (which would capture whatever
database happened to be lying around, including one built without triggers).

---

## D-027 — Every `HasDefaultValue` is paired with `HasSentinel` [Phase 0]

**Found by reading the StoreServer startup log**, which is the only reason it
was found at all: EF logs it once, as a model-validation warning, and nothing
else was looking.

**What was wrong.** EF omits a property from an INSERT when its value equals the
*sentinel* — the value EF reads as "not set" — and lets the database default
apply instead. The sentinel defaults to the CLR default of the type. Wherever
the database default is something else, an explicitly set value is silently
replaced:

| Property | Set to | Would have stored |
| :---- | :---- | :---- |
| `Customer.LegalBasis` | `Consent` | `'contract'` |
| `PromotionProduct.Priority` | `0` | `100` |
| `ReasonCode.IsActive` and eight other flags | `false` | `1` |

Fifteen properties were exposed. **`Customer.LegalBasis` is the serious one**:
it is the lawful basis recorded against a customer, `Consent` is the CLR default
only because it happens to be declared first, and the DPIA is built on that
column meaning what it says.

**The fix, and why it is uniform.** Every `HasDefaultValue(x)` now carries
`HasSentinel(x)`. Setting the sentinel *to the database default* makes omission
harmless in every case at once: if the caller's value equals the default it is
omitted and the database writes that same default; anything else is sent. No
per-property judgement, and no way to get one wrong.

**Why not rely on EF inferring it.** EF does infer a sentinel from a property
initializer in some cases — which is worse than never doing it, because it made
the bool properties behave correctly while the enum and integer ones did not.
Verified: with the sentinels removed, `IsActive = false` still round-tripped
while `LegalBasis = Consent` came back as `contract` and `Priority = 0` came
back as `100`. A rule that holds sometimes is one nobody can reason about.

**Rejected.** Dropping `HasDefaultValue` from the model so EF always sends the
value — that loses the DEFAULT clause on the next table rebuild (D-022), which
trades a wrong value for a missing constraint.

`DefaultValueSentinelTests` covers all three shapes and reads the raw column
rather than trusting the round trip: EF reads back whatever it wrote, so a
swallowed value looks perfectly correct through the model.

---

## D-028 — StoreServer initialises the database at startup, or does not start [Phase 0]

**What.** `Waymark.StoreServer` resolves its data directory from
`Waymark:Storage:DataDirectory`, falling back to `%ProgramData%\Waymark\data`
(D-013), registers `WaymarkDbContext` through `UseWaymarkSqlite`, and calls
`MigrateAndApplyTriggers()` in a scope before serving anything.

**Why at startup and why fatal.** The alternative is a till that starts, works,
looks right, and has no append-only guards. A store that will not start is a
phone call and twenty minutes; a store that quietly stopped enforcing consent
history is a finding, discovered by whoever asks for the audit trail. Failing
loudly is the cheaper of the two by a wide margin.

**Path resolution belongs to the host.** `WaymarkStoragePaths` composes paths
and nothing else — no configuration, no dependency injection — so
`Waymark.Persistence` keeps no opinion about how a host is wired. The identity
database is deliberately absent from that class: only
`Waymark.Pseudonymisation` may name that path (CLAUDE.md §3.4), and a
convenience helper offering it would be the first step to it appearing
somewhere else.

**Verified by running it.** Against a scratch directory the server created a
database with 58 tables, 81 indexes, all 11 triggers, WAL mode and the migration
recorded in `__EFMigrationsHistory`.

---

## D-029 — The fidelity comparison guards the baseline, not head [Phase 0]

**Found by Hakim's first real schema change**, which is the only way it could
have been found: the test passed on every run until a migration existed.

**What was wrong.** `ModelMatchesSchemaTests` compared `schema_v7_1.sql` against
a database migrated to **head**. Those are identical at v1 and diverge the
moment a legitimate migration lands, because `schema_v7_1.sql` is frozen and can
never gain a column. Adding `variants.reorder_point` — the worked example from
`docs/schema-changes.md`, done exactly as written — failed the suite with
`variants.reorder_point: mapped, not in the reviewed schema`. The migration was
correct; the test was wrong, and would have been wrong for every migration
after it.

This is the museum-piece failure D-020 describes, in the test written to avoid
it. Worth stating plainly: writing the warning down did not stop it happening.

**The fix.** The comparison now builds the database by applying **`InitialSchema`
alone** and nothing after it. The property it guards is narrower and permanent:
*the baseline migration reproduces the schema a human reviewed*. That stays true
however many migrations follow, and it is what O-7 actually cared about.

**What now guards what.** The reframing splits one overloaded test into three
honest ones.

| Check | Guards |
| :---- | :---- |
| `ModelMatchesSchemaTests.The_baseline_migration_reproduces_the_reviewed_schema` | the baseline, or `schema_v7_1.sql`, being edited |
| `ModelMatchesSchemaTests.The_model_has_no_changes_waiting_for_a_migration` | a configuration changed with no migration to carry it |
| `SchemaCurrentTests` | the shipped database drifting from its committed documentation |

The second is new, and closes the gap the reframing opened: with the comparison
pinned to the baseline, nothing else would notice an entity edited without a
migration. `HasPendingModelChanges()` compares the model to the last migration's
snapshot, which is exactly that.

**A division of labour worth knowing.** The baseline comparison matches indexes
by *shape* — table, columns, uniqueness, filter — never by name, because EF
cannot write an inline UNIQUE and names its equivalent differently. So an index
*rename* passes it. Verified: renaming `ix_variants_status` in the baseline
migration left the comparison green and failed `SchemaCurrentTests`, which
reported the exact line. Shape is the semantic guard; the golden file is the
name-level one, and neither is redundant.

---

## D-030 — Store scoping: a marker interface, a reflected filter, fail closed [Phase 0]

Implements CLAUDE.md §3.3 and DPIA risk R9.

**What.** Seventeen of the 58 tables carry `store_id`. Their entities implement
`IStoreScoped`, and `WaymarkDbContext.OnModelCreating` attaches a global query
filter to each. No schema change: query filters are model metadata, and
`has-pending-model-changes` stays clean.

**A marker interface rather than a convention on the column name.** Naming the
column `store_id` should not silently opt a table into a security filter, and
forgetting the marker should not be mistakable for a decision. The filtered set
is something a person wrote down, and a test checks that what they wrote down
covers every table with the column.

**Applied by reflection rather than a line in each of the seventeen
configurations.** It is one rule seventeen times, and a rule repeated by hand is
a rule that will be missed once — which is the whole reason §3.3 asks for a
filter instead of a `where` clause.

**Two filter shapes, because two columns are nullable.** `promotions` and
`processing_log` allow a null store, and those rows mean *every* store — a
promotion that is not store-specific, or processing that happened outside one.
They read `store_id IS NULL OR store_id = @p`. The other fifteen are NOT NULL
and read plain equality: the nullable form would add a branch that can never be
true and cost an index seek on tables like `transactions`.

**Unset means nothing, not everything.** `ICurrentStore.StoreId` returning null
does not disable the filter; it matches only rows belonging to no store. A
tenancy filter that opens up when unconfigured fails silently and leaks
everything; one that returns nothing fails in the first minute. `StoreServer`
reads `Waymark:Store:StoreId`, which is unset until a store row exists.

**`stores` is filtered too**, so a till sees its own row and no other. Correct at
Basic tier, and worth revisiting if a deployment ever holds more than one store
in one database.

**Verified by removing the marker from `Terminal`.** Five tests failed, and the
useful one was not the structural check but
`The_other_stores_rows_are_not_merely_reordered_but_absent`: store A's context
returned store B's terminal. That is R9 happening, reproduced.

**Escape hatch, and it is deliberate.** `IgnoreQueryFilters()` bypasses this, and
the tests use it to seed. Anything outside a test that calls it is doing
cross-store work and needs to say why in a decision entry.

---

---

## D-031 — `Money`: a fixed-scale integer, a currency, and no accidental rounding [Phase 0]

Closes half of O-4.

**What.** `Money` is a readonly struct of `(long MinorUnits, Currency Currency)`. It
maps to the schema's `INTEGER` money columns with no change to any column:
`schema_v7_1.sql` already fixes the storage as *"INTEGER, in centimes (scale
100). Never REAL, never TEXT."*

**The storage scale is a Waymark convention, not the currency's minor unit.**
One Waymark minor unit is 1/100 of a currency unit, for every currency. This
matters because an EF value converter sees one property in isolation — it cannot
read the row's `currency` column to learn an exponent. Fixing the storage scale
at 100 keeps the converter a pure function. The currency's *own* exponent lives
on the `Currency` type and governs display and cash rounding, not storage.

**No arithmetic that can round exists as an operator.** `+`, `-`, unary `-`,
comparison and multiplication by an `int` are exact and are operators.
Multiplication by a rate or a quantity, and division, exist **only** as methods
that take an explicit rounding policy:

```
money.Times(Quantity q, Rounding r)
money.Percent(BasisPoints bp, Rounding r)
money.Allocate(weights)        // exact; takes no policy
```

There is deliberately no `operator *(Money, decimal)`.

**Why the ceremony.** Grepping for `Times(`, `Percent(` and `Allocate(` returns
every site in the codebase where a centime can be created or destroyed. That
list is what CLAUDE.md §7.4 asks for — a founder who can point at every place
money changes shape. An implicit `*` operator would scatter those sites into
ordinary-looking arithmetic and there would be no way to enumerate them again.

**Rejected: `decimal` everywhere with rounding at the persistence boundary.**
It is what most .NET codebases do. It moves the rounding to a place nobody
reads, applies one policy to every case regardless of meaning, and makes the
count of rounding sites unknowable.

---

## D-032 — Rounding is three problems, not one setting [Phase 0]

**What.** Three mechanisms, deliberately not unified:

| Problem | Mechanism | Can it create variance? |
| :---- | :---- | :---- |
| Splitting a known total across parts | `Allocate` — largest remainder | **No.** Exact by construction |
| Deriving a value with no predetermined total | `Rounding.HalfEven` / `HalfUp`, retailer's choice | Yes, and it is the retailer's policy |
| Cash tender | The currency's cash step | Yes, and it is recorded (D-034) |

**Allocation, not repeated rounding.** A basket discount split across four lines,
or a bundle price split into components, gives each part its floor and then
distributes the leftover minor units one at a time, largest remainder first,
ties broken by line sequence. `sum(parts) == total` holds by construction,
always, deterministically. Rounding each part independently guarantees it will
not.

**Two policies, both the retailer's:** `HalfEven` (banker's) and `HalfUp` (away
from zero). Stored as `stores.rounding_policy`, **and stamped on
`transactions.rounding_policy`**. The second is the one that matters: a retailer
who switches policy in March must not make February's receipts
unreproducible. A receipt has to be recomputable from its own row.

**`Truncate` is not offered as a store policy.** It is biased downward on every
single line without exception, so the drift accumulates in one direction and
never cancels, and it is the hardest of the three to defend to an inspector. It
remains available as an internal operation where it is semantically correct
("how many whole packs fit"), never as a money presentation policy.

**Banker's does not make the variance zero.** Its zero-expectation property
assumes the discarded fractions are uniformly distributed. Retail prices cluster
on `.00`, `.50`, `.90`, `.99`, and 19% TVA on a price ending in `.99` produces a
strongly biased set of half-centimes. Banker's *reduces* drift versus half-up;
it does not remove it. The variance ledger is needed under either policy, which
is why it is not conditional on the choice.

**The engine's rounding is not selectable.** Almanac always uses `HalfEven`, and
mostly should not round at all — §5 says every figure carries its interval, and
a figure with an interval does not need rounding to the centime. Engine output
is stored at full precision and rounded only at display. This is what keeps the
retailer's presentation choice out of the statistics: a store that picked
`HalfUp` would otherwise bias every number Almanac fits on.

---

## D-033 — TVA is extracted per line from a TTC price, and derived by subtraction [Phase 0]

Resolves O-11. Under Law No. 04-02 on commercial practices, displayed retail
prices in Algeria must be **TTC**, and research confirms TVA is computed per
line item, with the discount applied before extraction.

**The arithmetic of a line, in order:**

```
line_ttc = round(sell_price × quantity, policy)   ← rounding site 1
line_ttc = line_ttc − discount_amount             ← exact
ht       = round(line_ttc / (1 + rate), policy)   ← rounding site 2
tva      = line_ttc − ht                          ← exact, by subtraction
```

**TVA is derived by subtraction, never rounded independently.** That is the
whole trick: it makes `ht + tva == line_ttc` true by construction on every line,
under any policy, with no residual. Summing then gives `subtotal + tax_total ==
total_amount` exactly, which is a testable invariant rather than a hope.

**Consequence: invoice-level tax variance cannot occur**, so
`tax_reconciliation` was dropped from the variance ledger's sources before it
was ever built. Had TVA been computed on the invoice total, or rounded
independently per line, that source would have been necessary.

**`prices.is_tax_inclusive` stays, and stays defaulted to 1.** Law 04-02 governs
*displayed retail* prices. B2B and wholesale quoting in HT is a different case,
and `supplier_variant.purchase_price` is HT by nature.

---

## D-034 — Cash tender rounds to the currency's step, and the difference is recorded [Phase 0]

Resolves O-10 and the R5 question of when. The smallest coin in practical
circulation in Algeria is 5 DZD.

**Rounding applies to the tender, never to the invoice.** The invoice total stays
exact: it is a fiscal document, it must be printable before the customer chooses
how to pay, and it must not change because they reached for cash instead of a
card.

```
Total TTC      1 247,00
Espèces        1 245,00
Arrondi          −2,00   →  rounding_variance, source 'cash_tender'
```

Only the **cash portion** of a tender is rounded. Card takes the exact amount.

**Nearest, ties away from zero.** Rejected: always-toward-the-store, which is
common practice and was the first suggestion here. It takes up to 4,99 DZD from
every cash customer on every sale, systematically and without exception — an
argument nobody wants to have under the same law that mandated TTC display.
Nearest is also the only one of the three that does not accumulate, and it is
what a shopkeeper does by hand anyway, which makes it explainable.

**The step is a property of the currency, not a constant in the POS.**
`Currency.CashRoundingStep` is 500 minor units for DZD and 1 for EUR (no
rounding). The number 500 never appears in a handler.

**Why a ledger and not a memo.** `cash_sessions` already has `counted_cash`,
`expected_cash` and `variance`, and that variance column exists to detect theft
and miscounting. If tender rounding leaks into it, every session shows a few
dinars off every day, the shopkeeper learns to ignore the number, and the
control is dead. Recording tender rounding separately is what keeps
`cash_sessions.variance` meaning what it says.

**New table, in the first real migration:**

```
rounding_variance
  variance_id  store_id  occurred_at
  source        'cash_tender' | 'currency_conversion'
  reference_type  reference_id
  amount        INTEGER signed minor units
  policy        TEXT
  created_at
```

Append-only, guarded by triggers like the other ledgers. Note what `source`
excludes: allocation. Allocation is exact (D-032), so a row with that source
would mean the allocator is broken — the absence is itself an assertion.

**The invariant this buys, and it is testable:**

```
expected_cash = opening_float
              + Σ cash payments + Σ paid_in − Σ paid_out − Σ drops
              + Σ tender rounding variance
```

---

## D-035 — Currency is carried by the value, and costs one field [Phase 0]

Closes the currency half of O-4.

**Three things the word "currency" usually conflates, kept apart:**

| | What it is | Today |
| :---- | :---- | :---- |
| Ledger currency | What the store's books are in. Set at commissioning, immutable | DZD |
| Document currency | What one document is denominated in | DZD — except supplier documents, which may already differ |
| Presentation currency | What a report is *displayed* in. Never stored | DZD |

**One rule.** Within a document all money is in that document's currency. Across
documents, arithmetic requires equal currencies or an explicit recorded
conversion. `+`, `-` and comparison **throw** on mismatch. There is no implicit
conversion anywhere, ever.

**Almost nothing changes.** v7 already put `currency TEXT NOT NULL DEFAULT 'DZD'`
on all eight money-bearing tables — `stores`, `prices`, `transactions`,
`transaction_payments`, `purchase_orders`, `suppliers`, `supplier_variant`,
`batch_items`. The work is in the type, not the schema.

**What is built now:** a `Currency` readonly struct of `(Code,
MinorUnitExponent, CashRoundingStep)` and a static registry of supported
currencies. **No `currencies` table** — these are ISO facts and local practice
facts, identical for every store, so they are code, not data. A table would cost
a migration, foreign keys from eight tables, and seed data, to hold values that
never vary per store.

**What is not built now:** rate tables, a rate feed, a conversion engine,
multi-currency reporting, revaluation. There is nothing to convert.

**No CHECK constraint on the `currency` columns.** Adding one to eight existing
tables means eight SQLite table rebuilds, including `transactions` and
`transaction_payments` — the exact operation CLAUDE.md §3.7 says to plan as an
operational event. For one supported currency the protection is not worth the
rebuild. Revisit if a second currency ever ships.

**Why the exponent is worth having today.** The schema's "scale 100" is a **DZD
fact**, not a universal one. If a currency with a different minor unit ever
appears, every INTEGER money column changes meaning at once, and no migration
can tell you which rows were which. With the exponent on the type from the
start, that day costs one converter.

**Stage 2 works with no code at all.** A French supplier quoting EUR sets
`supplier_variant.currency = 'EUR'` and `purchase_orders.currency = 'EUR'`; the
entire purchase-order arithmetic happens in EUR, so no conversion occurs and no
rate is needed. **Known limitation:** foreign-currency documents do not
round-trip through the ambient converter, which builds `Money` with the
deployment's ledger currency from `Waymark:Store:Currency`. That is the Stage 2
boundary and it is named here so it is not discovered later.

**Stage 3 — where EUR meets DZD** — happens at exactly one place: valuing
received stock into DZD inventory. The converted cost is written **once** into
`batches.unit_cost`, stamped with the rate and its date, as a landed-cost
decision rather than a live lookup. The same batch then always values the same
way and last year's margins do not move when the dinar does. Any residual is the
`currency_conversion` source of D-034's ledger.

---

## D-036 — `Quantity` and `QuantityDelta`: a level and a change are different types [Phase 0]

Closes the quantity half of O-4. Resolves R4.

**The distinction is level versus change, not positive versus signed.** The first
instinct was to force quantity positive, but the schema says otherwise in four
places — `inventories.quantity` is annotated *"thousandths, may be negative"*,
`stock_movements.quantity_changed` is *"thousandths, signed"*,
`transaction_items.quantity` is `CHECK (<> 0)` for refund lines, and
`transaction_payments.amount` is *"negative on refund"* — while
`quantity_ordered`, `quantity_received`, `quantity_returned` and
`stock_count_items.quantity` are all `> 0`. A stock level going negative is not a
bug; it is a sale that outran its receipt, which happens constantly.

**Two types, and the operators are the specification:**

```
Quantity      + QuantityDelta  →  Quantity        level + change = level
Quantity      − Quantity       →  QuantityDelta   level − level  = change
QuantityDelta + QuantityDelta  →  QuantityDelta   changes sum
Quantity      + Quantity                          deliberately does not exist
```

What falls out is the reconciliation algebra CLAUDE.md §8 puts in its top three,
now type-checked rather than conventional:

```
closing_level = opening_level + Σ(deltas)
```

**Why a type and not a test.** The archetypal stock bug is a sign error — a delta
passed where a magnitude was expected. Both values are plausible integers, and
no test catches it because whoever writes the test makes the same mistake. This
is exactly the silent error §8 says tests exist for, and here the type system
does the job better than a test can.

**Zero is a legal value for a delta, though not a legal movement.** Written up
first as "signed, never zero", which building it proved wrong.
`CHECK (quantity_changed <> 0)` stops a pointless row being written; it cannot
stop a receipt and a write-off cancelling, and it cannot stop two equal levels
differing by nothing. A type that refused zero could express neither, so
`Quantity − Quantity` would not be total and `Σ(deltas)` would have no identity.
The non-zero rule belongs to the column, exactly like the non-negative rules on
`quantity_ordered`.

**Totalling magnitudes goes through a named `Sum`, not `operator +`.** Adding the
units across the lines of an order is legitimate; adding two *levels* is not, and
one type serves both roles. Naming the operation is the safeguard — it reads as a
deliberate act at the call site, which is exactly where somebody should ask which
of the two they are doing.

**Both types carry their unit.** Every quantity column in the schema sits beside
a `unit_code`, and `units_of_measure` carries `factor_to_base` and
`decimal_places`. Adding 1 kg to 500 g must not be a silent integer addition:
mismatched units throw, and conversion is explicit through `factor_to_base`.
`decimal_places BETWEEN 0 AND 3` is per unit, so a variant sold by `piece`
(0 places) rejects 1.5 pieces at construction rather than at the CHECK
constraint.

**The cost, and why it is small here.** Two EF value converters instead of one
would normally mean editing 60-odd configurations by hand. Because path C′
generates the configurations, it is one rule in `tools/generate-model`, keyed off
the column comments the schema already carries (`-- thousandths, signed` → delta,
`-- thousandths` → quantity). The remaining cost is 8–15 explicit conversions at
boundaries — `QuantityDelta.Decrease(sold)`, `delta.Magnitude` — and each one is
a place somebody had to say out loud which direction they meant.

**`decimal_places` is enforced by `UnitPrecision`, a third type.** The validation
needs a number that lives on `units_of_measure`, and `Quantity` holds only the
`unit_code` — because that is all a row carries, and an EF converter cannot reach
across to another table. So the metadata is its own small value type, built at the
boundary from the entity: `UnitPrecision.For(unit.UnitCode, (int)unit.DecimalPlaces)`.
It is named for its job rather than called `UnitOfMeasure`, which is already the
entity in `Waymark.Domain.Reference`.

**Unit conversion is deliberately not built yet.** Nothing in Phase 0 converts
between units — `supplier_variant.units_per_purchase_unit` is a separate
mechanism — and conversion is the only quantity operation that would round. It
therefore needs the same "multiply by a rational, round once" primitive `Money`
already has, and the two should be one implementation rather than two. Building
it now would mean either duplicating that logic or refactoring `Money` mid-review.
When it lands, it lands with the extraction.

**`Money` is deliberately not split the same way.** It has the same shape
(`customers.credit` versus `credit_movements.amount`), but there the ledger is
append-only and the balance is derived rather than mutated, so the confusion has
far less room to occur. Revisit only if it bites.

---

## D-037 — Division by zero throws; absence is never zero [Phase 0]

**What.** `Money` division by zero throws, always, with no contextual exception.
A caller who legitimately expects zero to be possible calls a different method
that returns null, and the compiler then forces them to handle it. The engine
has an explicit "insufficient data" state and the UI renders that state.

**Why it is not contextual.** A margin of 0% shown because revenue was zero is
precisely the failure CLAUDE.md §8 exists for — the code runs, the screen looks
right, the number is wrong, and nobody notices for a quarter. §5 says stale
output is shown as stale and never hidden; a zero standing in for "could not
compute" hides it.

**The property that makes this cheap to review.** The choice is visible in *which
method was called*, so nobody reasons about context at a call site and a
reviewer sees it in the diff.

---

## D-038 — ULIDs come from a port, so the generator stays deterministic [Phase 0]

Resolves O-1.

**What.** `Waymark.Domain` declares `IIdGenerator { string NewId(); }` and
nothing else. `UlidGenerator` wraps `Ulid.NewUlid()` and lives in
`Waymark.Application`. `SeededIdGenerator(seed, clockStart)` is deterministic and
is what tests and the synthetic store generator inject. Command handlers mint
every id for the whole unit of work — transaction, items, movements, outbox row
— before anything is written.

**What forced it.** CLAUDE.md §8 says the synthetic store generator is
deterministic from a seed and tests depend on that. An entity that calls
`Ulid.NewUlid()` in its constructor makes that unachievable: the same seed
produces different ids every run, so there is no golden-file test over generated
data, no diff between two generated stores, and no reproducing a reported bug
from its seed. A static call cannot be substituted, and no amount of test
scaffolding recovers it afterwards.

A ULID is 48 bits of timestamp plus 80 bits of randomness, so fixing the clock
and seeding the randomness preserves every property that matters — uniqueness
within a run and correct sort order. It also gives a bonus that turns out to
matter: a store generated for 2024 gets ids whose embedded timestamps are in
2024, so ULID sort order matches business chronology in the fake data too.

**Consequence for the allowlist.** Hakim approved a named-allowlist rule for
Domain packages in place of loosening "zero dependencies" to "no project
references". That rule stands and is the right rule — but with the port,
**`Ulid` lives in Application and the allowlist starts empty.** Domain keeps a
true zero. `Ulid` 1.4.1 was verified MIT with an empty dependency group on
net6.0/net7.0/net8.0, so the allowlist would have been safe; it simply is not
needed. It earns an entry only if Domain later wants a typed `UlidId` that
validates format or reads the timestamp back out.

---

## D-039 — Pseudonymisation by keyed hash; `waymark-identity.db` is removed [Phase 0]

Supersedes the identity-file half of CLAUDE.md §3.4 and all of §3.5. **This
changes commitments in `Waymark_DPIA_v1` and that document must be amended
before it is filed.**

**What.** `pseudonym_key = HMAC-SHA256(tenant_key, "waymark:customer:v1:" ‖
customer_id)`, truncated to 128 bits and Crockford base32 encoded, so a
pseudonym is 26 characters like a ULID and the cloud's columns stay uniform.
There is no mapping table and no second SQLite file. The four-file split in §3.4
becomes three.

**The reframe that decided it.** `waymark-identity.db` never held the PII —
names and phone numbers are in `customers` in the operational database, with
`ix_customers_phone` on them. The identity file's job was exactly one
capability: **severing the link, per customer, by deleting a row.** That is a
much narrower purpose than the four-file table suggests, and the question
reduces to whether per-customer severance is worth a file, a second key and
§3.5's write ordering.

**Reverse lookup does not need a table.** HMAC is one-way, but a store has a few
thousand customers: enumerate them, compute each HMAC, match. Milliseconds. The
mapping table is genuinely unnecessary.

**The backup is what the separation has to survive.** `waymark-store.db` is
backed up to Waymark's cloud. If the mapping simply lived in a column on
`customers`, it would be uploaded in every backup, Waymark would hold both sides,
and the tier separation would be decorative. Two things survive that: a separate
file that is never backed up, or **a key that is never in a backup or sync
payload.** They are equally protective against the threat that matters —
Waymark-the-company being able to connect `customer_id`s in a backup to
pseudonyms in the cloud.

**Erasure is almost unchanged, and the first draft of this entry got that
wrong.** `Waymark_Implementation` §9.9 (decision 018) already specifies erasure
as **null the pseudonym on the cloud's transaction rows**, purge identity columns
in `processing_log`, and record it in `erasure_ledger` — *because* merely
destroying the mapping leaves the history linked to itself, so a year of
timestamped baskets still singles the person out. All of that works identically
under HMAC. What disappears is the second, store-side, unilateral cut. Since the
cloud-side null was always the load-bearing action and the mapping deletion was
defence-in-depth against the cloud failing to comply, **the cost is the loss of
that defence in depth, not the loss of anonymisation.** The aggregate statistics
survive either way.

**Rejected: a random pseudonym in a mapping table.** It is the stronger scheme on
one axis — severance can be performed locally, unilaterally, without the cloud's
cooperation, and without a network. Against that, removing the file deletes a
second key, §3.5's write ordering and an entire class of two-phase-commit bugs
from a system running on one Windows till.

**Accepted costs, stated plainly:**

1. **Backup exclusion stops being file-level.** This is the real one.
   `Waymark_Implementation` §9.7 reason 3 is that excluding a whole *file* from
   the cloud backup is configuration, not a rule anyone has to remember at a call
   site. Excluding a 32-byte secret from a payload is a rule someone can forget.
   This is precisely why condition 1 below is a test rather than a sentence.
2. **Erasure loses its defence in depth** — see above. Severance now depends on
   the cloud honouring the request; there is no local unilateral cut.
3. **Key compromise is total, permanent and unfixable.** One key, one function,
   every customer, forever, retroactively. The scheme is non-rotating by
   decision, so a leaked key re-identifies all historical cloud data for that
   tenant and nothing undoes it.
4. **No relief on D-015.** `waymark-store.db` stays SQLCipher-encrypted, so the
   bundle and SQLite 3.39.2 stay.

**Two costs that turn out not to be costs.** *Key loss* looked like a new risk
but is not: §9.7.1 already accepts that if the hardware is lost and only the
cloud backup survives, the mapping is gone permanently and the history becomes
unlinkable. The same sentence holds with "key" in place of "mapping". And §9.7
reason 2 — that two keys mean stolen hardware yields the operational database
but not the link — was always weaker than it reads, because the PII itself lives
in `customers` in the operational database. A thief with the till gets the names
and phone numbers directly; the mapping was never what stood in the way.

**Four conditions, all binding:**

1. **The key is never in a backup or sync payload** — enforced by a test over
   the backup payload and the outbox, not by a policy statement.
2. **The key is per tenant, not per store** (O-12), so one customer has one
   pseudonym across a chain. This costs nothing at Basic tier, where a tenant is
   one store. At multi-store it costs an out-of-band provisioning step, and the
   thing it must never cost is Waymark's cloud seeing the key: the key is
   generated client-side and carried operator-to-operator, never issued by the
   cloud.
3. **Domain separation in the input** — the `"waymark:customer:v1:"` prefix.
   Costs nothing now, impossible to add later, and it is what allows staff or
   suppliers to be pseudonymised under the same key without collision, and the
   scheme to be versioned if it must be.
4. **`Waymark.Sync` must still never reference `Waymark.Pseudonymisation`**
   (§2.1). That boundary is now more important, not less, because
   `Waymark.Pseudonymisation` holds the key.

**Not settled here:** key custody and recovery (O-2), and what a tier-2 record
contains beyond the pseudonym (O-3).

---

## D-040 — SQLCipher verified end to end before anything was built on it [Phase 0]

Hakim asked for confirmation before writing. A throwaway probe was run against
the real stack and deleted afterwards.

```
sqlite_version 3.39.2          cipher_version 4.5.2 community
cipher_provider libtomcrypt    kdf_iter 256000 (PBKDF2-HMAC-SHA512)
journal_mode=wal  -> wal       STRICT tables -> OK
right key -> opens    wrong key -> rejected    no key -> rejected
canary string in plaintext -> False
PRAGMA rekey -> OK, rows intact
unencrypted database in the same process -> OK

MigrateAndApplyTriggers() on an encrypted file -> OK
  60 tables, 11 triggers, 81 indexes, 58 STRICT
  'CREATE TABLE "transactions"' present in plaintext -> False
```

**What this settles.** The whole migration path — the STRICT generator, all 11
append-only triggers, all 81 indexes — runs unchanged against an encrypted
database. WAL works. Nothing leaks to a hex editor, including the schema text
itself. An unencrypted database opens in the same process under the same
bundle, which is what the POS Level-2 cache needs.

**What it also settles, usefully:** `PRAGMA rekey` works, so key rotation is
mechanically possible. D-039 chose a non-rotating pseudonymisation key, but the
*database* key is a separate secret and can be rotated. That matters to O-2.

**What it does not settle.** Where the key bytes live and who can recover them.
That is O-2 and it is unchanged by any of this.

---

## D-041 — `Money` is wired into the model; `Quantity` cannot be [Phase 0]

W5 as planned was "a rule in `tools/generate-model` keyed off the column
comments — `-- centimes` → Money, `-- thousandths` → Quantity". Building it
found three reasons that rule does not work. Two of them are fatal to half the
task.

**Finding 1: the comments are not a complete rule.** Nineteen columns carry
`-- centimes`. Twelve more are money and carry no comment at all, because the
schema comments the first line of a group and not the rest:
`transactions.discount_total`, `tax_total` and `total_amount` are unmarked,
as are all four money columns on `transaction_items` after the first.
`purchase_order_items.discount` is money and reads like a rate. A
comment-driven rule would have left those twelve as bare `long` and reported
success. The list is therefore **hand-verified against `schema_v7_1.sql` and
written down in `MoneyMappingTests.MoneyColumns`**, where a test compares it to
the model in both directions — a money column missing from the model fails, and
so does a non-money column that crept in.

**Finding 2: some columns cannot be typed at all.** Their unit depends on a
sibling column in the same row:

| Column | Depends on |
| :---- | :---- |
| `promotion_variant.promotion_value`, `promotion_product.promotion_value` | `value_type` — centimes when `amount`, basis points when `percent` |
| `parameter_registry.value_number`, `interval_low`, `interval_high` | the parameter |
| `recommendations.interval_low`, `interval_high`, `recommendation_options.projected_value` | what the recommendation is about |
| `product_attribute_values.value_number`, `variant_attribute_values.value_number` | *"scaled per the attribute's unit"* |

These stay `long`. A value object cannot honestly wrap a number whose meaning is
decided elsewhere, and wrapping it in the wrong one would be worse than leaving
it plain.

**Finding 3 — the fatal one: `Quantity` has no unit to be built from.** A value
converter sees one property in isolation. `Money` survives that because the
ledger currency is a deployment fact that can come from outside the row
(D-035). **A unit is not**: it varies per row, and five of the eleven quantity
columns have no unit column on their row at all —
`promotion_variant.min_quantity`, `product_bundle_items.quantity`,
`inventories.quantity`, `returns.quantity_returned`,
`stock_count_items.quantity`. Their unit is the variant's `selling_unit_code`,
on another table.

An EF complex type mapping `Quantity` to `(quantity, unit_code)` would work for
the six that do carry a unit and cannot work for the five that do not, so the
model would be half one shape and half another. **Quantity columns stay `long`,
and `Quantity` is constructed at the point of use** — in a handler, from the row
plus the unit it belongs to, which is exactly where `UnitPrecision` was designed
to be built anyway (D-036). Nothing is lost from the arithmetic: the level/change
algebra still type-checks wherever quantities are actually computed on.

**What was built.** Thirty-two money columns are now `Money` in the entity. The
converter is applied **centrally by CLR type** in
`WaymarkDbContext.OnModelCreating`, not named in each configuration — the same
argument as the store filter: one rule thirty-two times is a rule that will be
missed once, and the one it is missed on is a money column stored unwrapped with
nothing to notice.

**Nothing migrated, and that is worth knowing.** Changing a property from `long`
to `Money` with a converter to `long` is invisible to EF's model differ:
`has-pending-model-changes` stays clean and the column stays `INTEGER`. Verified
before the other thirty-one columns were touched.

**The sentinel improved by accident.** D-027 paired `HasDefaultValue(0L)` with
`HasSentinel(0L)` because `0L` means both "no value" and "zero", and without the
pairing an explicit zero was silently replaced by the column default.
`default(Money)` is the only `Money` with no currency, so it cannot collide with
a real amount — EF uses it as the struct's sentinel without being told, and the
explicit `HasSentinel` is gone from the money properties. `HasDefaultValue` keeps
a value only so migrations still emit `DEFAULT 0`; its currency never surfaces,
which `WaymarkConverters.ZeroMoney` says out loud.

**`WaymarkModelCacheKeyFactory`.** EF caches one model per context type. The
money converters are built from the ledger currency, so a model built for a DZD
store could be handed to a EUR one and read every money column back with the
right number and the wrong label. One store per process at Basic tier means this
cannot bite today; it is here because the failure leaves no trace at all.

**The Stage 2 limitation now has an alarm.**
`MoneyMappingTests.Every_currency_column_holds_the_ledger_currency` scans all
eight currency columns and fails if a row is in anything but the ledger
currency, and `StoreServer` refuses to start if `Waymark:Store:Currency`
disagrees with the `stores` row. D-035 named this limitation and accepted it;
what was missing was anything that would say when it had been reached. The day
that test fails is the day the EUR supplier work starts, not a bug to patch.

**Verified by breaking it.** Not applying the converter, dropping the nullable
branch, and reverting a money column to `long` each fail the suite; reverting a
*non-nullable* one does not even compile.

## Open — decisions waiting on Hakim

These are in CLAUDE.md §7.2 territory and were deliberately **not** guessed at
during scaffolding.

### Still open

| # | Question | Why it cannot be defaulted | Blocks |
| :---- | :---- | :---- | :---- |
| O-2 | **Key custody and recovery.** Where the SQLCipher key and the HMAC tenant key live on a Windows till — DPAPI machine scope, DPAPI user scope, a key file, an operator passphrase — and whether either is recoverable if the machine dies, by whom. | DPIA §5.4. Two secrets now, with different properties: the database key **can** be rotated (`PRAGMA rekey`, verified in D-040); the tenant key **cannot** (D-039), so its loss permanently unlinks all cloud history and its compromise is permanent and retroactive. Escrow solves loss but reintroduces the linkage risk unless the escrow is operator-held. | `Waymark.Pseudonymisation`, and encrypting the store database at all |
| O-3 | **What a tier-2 record contains**, beyond the pseudonym. | The mapping *mechanism* is settled by D-039; the payload is not. It is the thing the DPIA promises about, and it decides what the outbox can carry. | The tier 1→2 transform, the outbox shape |
| O-5 | Assertion library for the test projects. | FluentAssertions 8.x requires a paid commercial licence from Xceed and Waymark is a commercial product, so the pin was removed rather than shipping a licensing liability into Phase 1. Candidates: `AwesomeAssertions` (MIT fork of FluentAssertions 7), `Shouldly`, or plain xUnit `Assert`. All 64 tests currently use plain `Assert` and read fine. | Nothing. Deferrable indefinitely, but cheaper to decide before the value-object suites are written |
| O-15 | **The recommendation envelope's exact fields and their types.** `Waymark_Build_Plan` gives `{department, explanation, urgency, action_type: binary\|menu, options[], source}` — is that final? | CLAUDE.md §7.2 names the Integration Layer contract explicitly. It is also the shape Almanac has to emit, so changing it later costs on both sides. | `Waymark.Contracts` |
| O-16 | **What `processing_log` records at each access site** — the exact column set per purpose (collection, consultation, disclosure, transmission, erasure). | DPIA commitment. §4 says the write is structural, not remembered, which means the helper's signature *is* the promise. | The `processing_log` helper, and therefore any handler touching a customer |
| O-17 | **The synthetic store generator's spec** — category mix, price distributions, Ramadan and payday shape, spoilage and stockout rates, connectivity quality. | The Build Plan calls this the highest-leverage build in the phase and says to review its *output distributions*, not its code. A generator built to a guessed spec produces plausible-looking data that teaches the engine the wrong seasonality. | The generator, and every test that depends on a year of data |

### Resolved

| # | Outcome |
| :---- | :---- |
| O-1 | ~~Does `Waymark.Domain` take the `Ulid` package?~~ — **resolved by D-038: it does not.** The port `IIdGenerator` lives in Domain, `Ulid` lives in Application. The named-allowlist rule Hakim approved stands and starts empty, so Domain keeps a true zero dependency count. |
| O-2 *(first half)* | ~~Two SQLite providers in one process~~ — **resolved by D-015**, one SQLCipher bundle for the whole process, and **verified end to end by D-040**: migration, 11 triggers, 81 indexes, 58 STRICT tables, WAL, wrong-key rejection, rekey, and an unencrypted database in the same process. Custody remains open above. |
| O-3 *(first half)* | ~~The tier 1 → tier 2 mapping mechanism~~ — **resolved by D-039.** HMAC-SHA256 with a per-tenant key, truncated to 128 bits, no mapping table, no identity file. |
| O-4 | ~~`Money` and `Quantity`~~ — **resolved by D-031, D-032, D-033, D-034, D-035, D-036 and D-037.** Fixed-scale integers, currency on the value, three rounding mechanisms, TVA by subtraction, cash tender recorded, two quantity types, division by zero throws. |
| O-6 | ~~Entity topology~~ — **resolved by D-021.** One set: entities in `Waymark.Domain`, configurations in `Waymark.Persistence`. |
| O-7 | ~~`HasPendingModelChanges()` cannot see the v1 schema~~ — **closed 10/09/2026.** Its premise was D-019's hand-pasted `Up()`, which O-9 removed. The concern itself is now covered by `ModelMatchesSchemaTests` and the pending-changes test added in D-029. |
| O-8 | ~~The 85 foreign-key indexes EF adds on its own~~ — **resolved 08/09/2026: suppressed.** `ForeignKeyIndexConvention` is removed in `ConfigureConventions`. If a foreign key later needs an index it is added with `HasIndex`, like every other. |
| O-9 | ~~Does the baseline `Up()` still need the schema SQL pasted into it?~~ — **resolved 09/09/2026: no.** Checked mechanically across all 58 tables, 625 columns, column *order*, types, nullability, keys, 137 foreign keys, unique constraints as groups, index columns, uniqueness, partial-index filters, 167 CHECK expressions and 102 defaults. |
| O-10 | ~~Cash rounding direction~~ — **resolved by D-034: nearest, ties away from zero.** Always-toward-the-store was rejected as systematically taking up to 4,99 DZD from every cash customer. |
| O-11 | ~~Is the discount applied before TVA extraction?~~ — **resolved by D-033: yes**, and TVA is derived by subtraction so `ht + tva == line_ttc` holds by construction. |
| O-12 | ~~HMAC key per store or per chain?~~ — **resolved by D-039: per tenant.** Free at Basic tier; at multi-store it costs an out-of-band provisioning step, and the key must never be issued by Waymark's cloud. |
| O-13 | ~~Pseudonym shape~~ — **resolved by D-039: truncated to 128 bits, Crockford base32**, so a pseudonym is 26 characters like a ULID. |
| O-14 | ~~Does `Money` get the level/change split too?~~ — **resolved by D-036: no.** The credit ledger is append-only and the balance is derived rather than mutated, so the confusion has far less room to occur. |
