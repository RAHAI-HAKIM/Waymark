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
ignore would work until someone named a file slightly differently at 2 a.m.; a
blanket pattern cannot be got round by accident. Every database in this project
is generated — from migrations or from the synthetic store generator — so
nothing is lost.

**Rejected.** Ignoring the identity file by exact name. The blast radius of
being wrong is a regulator's question, not a merge conflict.

---

## Open — decisions waiting on Hakim

These are in CLAUDE.md §7.2 territory and were deliberately **not** guessed at
during scaffolding.

| # | Question | Why it cannot be defaulted |
| :---- | :---- | :---- |
| O-1 | Does `Waymark.Domain` take the `Ulid` NuGet package, or define its own ULID type? | "Zero dependencies" is written about project references. A leaf package is arguably fine, but the rule's value comes from being absolute. `Ulid` is declared in `Directory.Packages.props` and referenced by nobody, pending this. |
| O-2 | SQLCipher provider for `waymark-identity.db`, and where its key lives | `SQLitePCLRaw.bundle_e_sqlcipher` is a placeholder version pin. Key custody is a DPIA §5.4 commitment, not a package choice. |
| O-3 | Schema — every table, and the tier 1 → tier 2 mapping | The whole of Phase 0's middle. Nothing was scaffolded here. |
| O-4 | `Money` and `Quantity` — rounding mode, currency handling, negative quantity rules | Money arithmetic is explicitly Hakim's. |
| O-5 | Remote repository: keep `RAHAI-HAKIM/QRetail` under a rename, or start `Waymark` fresh | Affects clone URLs, history continuity and anything already linked to the old name. |
