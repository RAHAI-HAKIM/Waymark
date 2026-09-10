# Working with the schema

Everything about how the database is defined, created, changed and checked. The
short rules are CLAUDE.md §3.7; the reasoning is decisions.md D-016, D-019,
D-021 to D-029.

---

## 1. The pieces, and what each is for

| File | Role |
| :---- | :---- |
| `src/Waymark.Persistence/schema_v7_1.sql` | **Frozen.** The hand-written schema, reviewed once. Never edited, never executed on a store. It is the reference side of the fidelity comparison |
| `src/Waymark.Persistence/triggers.sql` | The 11 append-only triggers. Not in the EF model and cannot be. Idempotent — every statement drops before it creates |
| `src/Waymark.Persistence/schema_current.sql` | **Generated documentation.** What the database looks like now. Committed, read, never executed |
| `src/Waymark.Persistence/Migrations/` | EF migrations. `InitialSchema` is the baseline; everything after is a change |
| `src/Waymark.Domain/**` | 58 entities and 70 enums. Plain classes, no EF, no attributes |
| `src/Waymark.Persistence/Configurations/` | One `IEntityTypeConfiguration` per entity — column names, converters, defaults, sentinels, CHECKs, indexes, keys |
| `tools/generate-model/` | The one-shot generator that produced the model. Kept for provenance. **Do not re-run** |

Three of these are not obvious and are worth knowing about.

**`schema_v7_1.sql` is frozen.** Changing the schema means adding a migration.
Editing that file changes nothing about any database, and it breaks the
comparison that proves the baseline migration reproduces it.

It follows that this file falls further behind the live schema with every
migration, **by design**. It is not a description of the database now — that is
`schema_current.sql`. It is the record of what was reviewed once, kept so the
baseline can still be checked against it.

**Triggers are not in the EF model.** EF does not know triggers exist, and its
table rebuild issues `DROP TABLE`, which takes them along. Reproduced during the
D-016 work: a table went through one rebuild and came out with its trigger gone,
while EF reported success. So they live in `triggers.sql` and are re-applied
after every migration.

**`schema_current.sql` is never executed.** It exists so the whole schema stays
readable in one file, which `schema_v7_1.sql` stopped being the moment it froze.

---

## 2. How a database is created

One way, and it is the same on a till, in a test and on your machine:

```csharp
context.MigrateAndApplyTriggers();
```

`Waymark.StoreServer` calls it at startup, before serving anything. If a trigger
is missing afterwards it throws and the server does not start — a till that will
not start is a phone call, a till that has quietly stopped enforcing consent
history is a finding.

**`Database.Migrate()` on its own is not a creation path.** It applies migrations
and stops, leaving a database with no append-only guards, and says nothing.
`Migrate_alone_leaves_the_database_unprotected` pins that down as a test rather
than folklore.

**Neither is `dotnet ef database update`**, for the same reason. It is fine for
inspecting what a migration produces; it is not how a usable database is made.

### Where the file lives

`%ProgramData%\Waymark\data\waymark-store.db`, overridable with
`Waymark:Storage:DataDirectory` (D-013). Not beside the executable — a
self-contained publish is a folder replaced wholesale on upgrade, and data
inside it dies with the update. Not under the user profile, which is frequently
OneDrive-redirected, and OneDrive syncing an open SQLite file corrupts it.

```powershell
$env:Waymark__Storage__DataDirectory = "$env:TEMP\waymark-dev\data"
dotnet run --project src/Waymark.StoreServer
```

---

## 3. Changing the schema

### 3.1 Change the entity

```csharp
// src/Waymark.Domain/Catalogue/Variant.cs
public long ReorderPoint { get; init; }
```

`long`, not `int` — every INTEGER column is `long` here, no exceptions. No
`required`, because the column has a database default.

### 3.2 Change the configuration

```csharp
// src/Waymark.Persistence/Configurations/VariantConfiguration.cs
builder.Property(x => x.ReorderPoint)
    .HasColumnName("reorder_point")
    .HasDefaultValue(0L)
    .HasSentinel(0L);
```

**`HasDefaultValue` is always paired with `HasSentinel`, with the same value.**
The sentinel is what EF reads as "not set" and omits from the INSERT, letting the
database default apply. It defaults to the CLR default of the type, so without
this, setting `IsActive = false` on a column declared `DEFAULT 1` is dropped and
the row comes back active. Setting the sentinel to the database default makes
omission harmless: the value EF omits and the value the database writes become
the same thing (D-027).

**Anything the table constrains has to be declared here**: CHECK constraints,
indexes with their filters, unique constraints as groups, foreign keys. A
rebuild recreates the table from the model alone.

```csharp
builder.ToTable("variants", table =>
{
    table.HasCheckConstraint("ck_variants_tare_weight", @"tare_weight >= 0");
});

// A group, not three constraints: UNIQUE(a, b) allows a to repeat.
builder.HasIndex(x => new { x.CountId, x.VariantId, x.BatchId }).IsUnique();

// The WHERE clause is part of the constraint, not decoration.
builder.HasIndex(x => new { x.ParameterCode, x.ScopeType, x.ScopeId })
    .HasDatabaseName("ux_parameter_current")
    .IsUnique()
    .HasFilter(@"is_current = 1");
```

### 3.3 Generate the migration

```bash
dotnet ef migrations add AddVariantReorderPoint --project src/Waymark.Persistence
```

### 3.4 Read the generated file — always

CLAUDE.md §3.7 requires it, and §4 below is why. A safe change is one statement:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<long>(
        name: "reorder_point",
        table: "variants",
        type: "INTEGER",
        nullable: false,
        defaultValue: 0L);
}
```

### 3.5 Regenerate `schema_current.sql`

```powershell
$env:WAYMARK_UPDATE_SCHEMA_CURRENT = "1"
dotnet test src/Waymark.sln --filter FullyQualifiedName~SchemaCurrent
```

Then **read the diff**. It is the human-readable summary of what your migration
actually did, and it is the cheapest review you will get.

### 3.6 Run the tests, then commit the migration and the model together

```bash
dotnet test src/Waymark.sln -maxcpucount:1
```

A migration without its configuration change, or the other way round, leaves the
repository in a state that builds and is wrong.

---

## 4. The table rebuild

SQLite cannot `ALTER` most things, so EF creates a replacement table, copies
every row, drops the original and renames. **Changing nullability, changing a
type, dropping a column or adding a CHECK all do this.**

Search the generated migration for `ef_temp`:

```csharp
migrationBuilder.Sql("CREATE TABLE \"ef_temp_variants\" ( ... ) STRICT;");
migrationBuilder.Sql("INSERT INTO \"ef_temp_variants\" (...) SELECT ... FROM \"variants\";");
migrationBuilder.Sql("DROP TABLE \"variants\";");
migrationBuilder.Sql("ALTER TABLE \"ef_temp_variants\" RENAME TO \"variants\";");
```

When you see it, three things are true.

**Every trigger on that table is gone.** `DROP TABLE` takes them.
`ApplyTriggers()` puts them back, which is why it and `Migrate()` are one call.

**Only what the model declares comes back** — indexes, CHECK constraints, foreign
keys. Anything you forgot to configure is now permanently absent, and nothing
reports it. This was verified, not assumed: a rebuild took `CHECK (amount > 0)`
off a table and a negative amount was accepted afterwards, with EF printing
`Done.`

**It copies the whole table.** On a shop with two years of `transactions` or
`stock_movements` that is a real pause. A change touching those tables is an
operational event to be planned, not slipped into a routine update.

---

## 5. What the tests catch

58 tests. These are the ones that exist because the failure they catch is
silent.

| Suite | Catches |
| :---- | :---- |
| `ModelMatchesSchemaTests` (baseline) | the **baseline migration** or `schema_v7_1.sql` being edited — columns, order, types, nullability, keys, STRICT, index columns, index uniqueness, partial-index filters, triggers. It compares the baseline alone, never head |
| `ModelMatchesSchemaTests` (pending) | a configuration changed with no migration to carry it |
| `SchemaCurrentTests` | `schema_current.sql` no longer describing the database |
| `TriggerApplicationTests` | `triggers.sql` drifting, `ApplyTriggers()` not restoring, `Migrate()` alone leaving the database unprotected |
| `AppendOnlyTests` | an audit table accepting an UPDATE or DELETE — asserted by attempting the write, not by looking for the trigger |
| `DefaultValueSentinelTests` | an explicitly set value being swallowed by a database default |
| `SchemaInvariantTests` | a table that is not STRICT, a REAL column, a money column that is not INTEGER, a rowid-alias primary key |
| `EntityMappingTests` | a converter writing the wrong thing — enum spelling, timestamp format, integer money |
| `ArchitectureTests` | `Sync` reaching `Pseudonymisation`, EF Core or HTTP below the hosts |

Two habits worth keeping, because both caught real defects here.

**Assert behaviour, not declaration.** A trigger named correctly, on the right
table, with the right message, can still carry a `WHEN` clause that narrows it to
nothing. Only executing the `DELETE` finds that.

**Read the raw column, not the round trip.** EF reads back whatever it wrote, so
a converter emitting `"PaidIn"` into a column whose CHECK allows only `paid_in`
round-trips perfectly and fails in production. `EntityMappingTests` and
`DefaultValueSentinelTests` both open a raw SQL connection for this reason.

**And a comparison is worth exactly what it compares.** An earlier version of
`ModelMatchesSchemaTests` checked index names and columns but not uniqueness or
filters, and reported "no structural differences" while a composite UNIQUE had
been flattened into three single-column constraints and twelve partial indexes
had lost their filters (D-024).

**Check the right two things, as well.** That same test then compared the
reviewed schema against the database at *head*, which is correct exactly until
the first migration and wrong forever after: the frozen file cannot gain a
column, so every future migration would fail it. Adding `variants.reorder_point`
is what surfaced it. It now compares the baseline alone, and the pending-changes
test covers what that no longer sees (D-029).

---

## 6. Undoing a migration

`dotnet ef migrations remove` deletes the migration file and rewinds the model
snapshot. It does **not** touch anything else, and two of those are easy to
forget.

**It will not undo an applied migration.** If the migration has been applied to
the database the tools can reach, EF refuses:

```
The migration '20260910134838_AddVariantReorderPoint' has already been applied
to the database. Revert it and try again.
```

Revert the database first, naming the migration you want to end at — everything
after it is undone by running its `Down()`:

```bash
dotnet ef database update InitialSchema --project src/Waymark.Persistence
```

```bash
dotnet ef migrations remove --project src/Waymark.Persistence
```

**"The database" here is the design-time one**, not the one your application
uses. See the trap in §7: the EF tools build a context from
`WaymarkDbContextDesignTimeFactory`, which points at
`%TEMP%\waymark-design-time\waymark-store.db`. That is the database EF checks
before refusing, and the one `database update` acts on.

**It will not revert your entity or configuration changes.** Those are ordinary
source edits and are yours to undo. If you leave them, the model no longer
matches any migration and
`ModelMatchesSchemaTests.The_model_has_no_changes_waiting_for_a_migration`
fails — which is the intended outcome, not a nuisance.

**It will not regenerate `schema_current.sql`.** That file only ever changes
when you regenerate it deliberately:

```powershell
$env:WAYMARK_UPDATE_SCHEMA_CURRENT = "1"
dotnet test src/Waymark.sln --filter FullyQualifiedName~SchemaCurrent
```

Until you do, `SchemaCurrentTests` fails and names the first differing line.
That is the file doing its job: it is documentation, and documentation that
updated itself silently would document nothing.

So the full sequence to undo a migration is four steps, in this order:

1. `dotnet ef database update <previous migration>`
2. `dotnet ef migrations remove`
3. revert the entity and configuration edits
4. regenerate `schema_current.sql`, then run the tests

Step 4 is the one that gets skipped, and the test suite is what remembers.

---

## 7. Things that will bite

**Never call `Migrate()` inside a transaction.** EF Core 9+ starts its own and
uses an execution strategy. `MigrateAndApplyTriggers()` checks and throws with a
message that says so, because EF's own error does not.

**Always configure the context with `UseWaymarkSqlite`, never `UseSqlite`.**
Plain `UseSqlite` omits the STRICT generator, and tables created without it
accept a REAL into a money column. Not hypothetical: the first version of
`ModelMatchesSchemaTests` used `UseSqlite` and produced 58 non-STRICT tables with
the build green.

**`dotnet ef` does not touch the database your application uses.** Every EF
command builds a context from `WaymarkDbContextDesignTimeFactory`, which points
at a scratch path under `%TEMP%`, deliberately — a mistyped command must not be
able to reach a store's data. So `database update` migrates the scratch
database, `migrations remove` checks the scratch database, and neither of them
has any opinion about `%ProgramData%\Waymark\data`. The application's database
is brought up to date by the application, at startup, through
`MigrateAndApplyTriggers()`.

A consequence worth knowing: a scratch database built by `dotnet ef database
update` **has no triggers**, because nothing ran `ApplyTriggers()`. That is fine
for inspecting migrations and wrong for anything else.

**Your `sqlite3` CLI is not the application's SQLite.** The CLI is 3.53;
SQLCipher gives the application 3.39.2 (D-015). DDL that works at the prompt can
still fail at runtime. `dotnet ef` is the authority.

**A database created before the baseline has no migration history.** If you have
an old `waymark-store.db` built by running `schema_v7_1.sql` through the CLI,
`Migrate()` will try to create tables that already exist and fail. Delete it and
let the application build it.

**Do not re-run the generator.** `tools/generate-model` rewrites all 58
configurations from the frozen schema. It would undo every migration's worth of
model changes and reproduce v1.

---

## 8. Command reference

```bash
# add a migration
dotnet ef migrations add <Name> --project src/Waymark.Persistence
```

```bash
# see what it would run, without running it
dotnet ef migrations script --project src/Waymark.Persistence
```

```bash
# revert the database to a named migration, before removing one
dotnet ef database update <MigrationName> --project src/Waymark.Persistence
```

```bash
# undo the last migration, once the database no longer has it applied
dotnet ef migrations remove --project src/Waymark.Persistence
```

```bash
# list migrations and whether they have been applied
dotnet ef migrations list --project src/Waymark.Persistence
```

```bash
# the DDL the model alone would produce
dotnet ef dbcontext script --project src/Waymark.Persistence
```

```bash
# everything, including the fidelity and golden-file checks
dotnet test src/Waymark.sln -maxcpucount:1
```

`-maxcpucount:1` is a local workaround, not a project rule: MSBuild tries to
spawn a node per core and this machine runs out of memory doing it.
