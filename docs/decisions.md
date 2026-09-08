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

## Open — decisions waiting on Hakim

These are in CLAUDE.md §7.2 territory and were deliberately **not** guessed at
during scaffolding.

| # | Question | Why it cannot be defaulted |
| :---- | :---- | :---- |
| O-1 | Does `Waymark.Domain` take the `Ulid` NuGet package, or define its own ULID type? | "Zero dependencies" is written about project references. A leaf package is arguably fine, but the rule's value comes from being absolute. `Ulid` is declared in `Directory.Packages.props` and referenced by nobody, pending this. |
| O-2 | ~~Two SQLite providers in one process~~ — **resolved by D-015.** One bundle, SQLCipher, for the whole process. Key custody for `waymark-identity.db` remains open under DPIA §5.4. |
| O-3 | Schema — every table, and the tier 1 → tier 2 mapping | The whole of Phase 0's middle. Nothing was scaffolded here. |
| O-4 | `Money` and `Quantity` — rounding mode, currency handling, negative quantity rules | Money arithmetic is explicitly Hakim's. |
| O-5 | Assertion library for the test projects | FluentAssertions 8.x requires a paid commercial licence from Xceed, and Waymark is a commercial product. The pin was removed rather than shipping a licensing liability into Phase 1. Candidates: `AwesomeAssertions` (MIT fork of FluentAssertions 7), `Shouldly`, or plain xUnit `Assert`. Nothing in the suite uses an assertion library today. |
| O-6 | ~~Entity topology~~ — **resolved by D-021.** One set: entities in `Waymark.Domain`, configurations in `Waymark.Persistence`. |
| O-7 | **`HasPendingModelChanges()` cannot see the v1 schema.** Under D-019 the initial migration's `Up()` is hand-written SQL while the model snapshot is generated from the entity configurations. That check compares model to snapshot, so the configurations and the pasted schema can disagree and nothing reports it. Confined to v1, since every later change flows from the model — and the one-time diff in D-019 is the mitigation, which makes that diff load-bearing rather than advisory. Flagged 08/09/2026 to investigate before the baseline is generated. |
