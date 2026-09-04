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
