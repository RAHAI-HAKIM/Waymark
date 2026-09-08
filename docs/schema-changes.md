# Changing the database

How a schema change is made, from the model that exists today to a migration on
a shop's till. The rules behind it are CLAUDE.md §3.7; the reasoning is
decisions.md D-016, D-019 and D-022.

> **Where this stands.** The model exists. **No migration exists yet**, and
> neither do `triggers.sql`, `ApplyTriggers()`, the bootstrapper or
> `schema_current.sql`. Step 0 below has not been done. Everything after it
> describes the loop once it has.

---

## Step 0 — the baseline, once, and carefully

This is the only step that is not routine, and it is the one that everything
downstream trusts.

```bash
dotnet ef migrations add InitialSchema --project src/Waymark.Persistence
```

EF writes an `Up()` containing `CreateTable` calls for all 58 tables, generated
from the configurations. **Do not delete it yet.**

**Diff that generated code against `schema_v7_1.sql` before replacing it.** This
is the one moment a complete comparison of 58 configurations against the
reviewed schema comes for free, and after this the two artifacts diverge and it
never comes free again. `ModelMatchesSchemaTests` already compares tables,
columns, types, nullability, keys, foreign keys and STRICT — what it does *not*
compare is CHECK constraint expressions and default values, so those are what to
read.

Then replace the body of `Up()` with the contents of `schema_v7_1.sql`, minus
its triggers, as one `migrationBuilder.Sql(...)`. From that point:

- `schema_v7_1.sql` is **frozen**. It is history. Never edit it again.
- `Migrate()` is the only way a database gets created.
- Triggers move to `triggers.sql` and are applied by `ApplyTriggers()` after
  every `Migrate()`.

---

## Every change after that

### 1. Change the entity

```csharp
// src/Waymark.Domain/Catalogue/Variant.cs
public long ReorderPoint { get; init; }
```

`long`, not `int` — every INTEGER column is `long` in this codebase, with no
exceptions. No `required`, because the column has a database default.

### 2. Change the configuration

```csharp
// src/Waymark.Persistence/Configurations/VariantConfiguration.cs
builder.Property(x => x.ReorderPoint)
    .HasColumnName("reorder_point")
    .HasDefaultValue(0L);
```

If the change adds a CHECK, an index or a foreign key, **declare it here too**.
A rebuild recreates the table from the model alone and drops anything the model
does not know about (D-022).

### 3. Generate the migration

```bash
dotnet ef migrations add AddVariantReorderPoint --project src/Waymark.Persistence
```

### 4. Read the generated file — always

CLAUDE.md §3.7 requires it, and this is why. A safe change looks like this:

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

One statement, no table touched beyond adding a column. Fine.

**A rebuild looks completely different**, and the word to search for is
`ef_temp`:

```csharp
migrationBuilder.Sql("CREATE TABLE \"ef_temp_variants\" ( ... ) STRICT;");
migrationBuilder.Sql("INSERT INTO \"ef_temp_variants\" (...) SELECT ... FROM \"variants\";");
migrationBuilder.Sql("DROP TABLE \"variants\";");
migrationBuilder.Sql("ALTER TABLE \"ef_temp_variants\" RENAME TO \"variants\";");
```

SQLite cannot `ALTER` most things, so EF creates a replacement table, copies
every row, drops the original and renames. Changing nullability, changing a
type, dropping a column or adding a CHECK all do this.

When you see `ef_temp`, three things are true:

1. **Every trigger on that table is gone.** `DROP TABLE` takes them with it.
   `ApplyTriggers()` puts them back, which is why it must run after every
   `Migrate()` and never be skipped.
2. **Only what the model declares comes back** — indexes, CHECK constraints and
   foreign keys. Anything you forgot to configure is now permanently absent, and
   nothing will tell you.
3. **It copies the whole table.** On a shop with two years of `transactions` or
   `stock_movements`, that is a real pause. A change touching those tables is an
   operational event to be planned, not something slipped into a routine update.

### 5. Regenerate `schema_current.sql` and commit it with the migration

`schema_v7_1.sql` is frozen, so `schema_current.sql` is what keeps the whole
schema readable in one file. It is documentation and is never executed. It must
be regenerated from a database that has had `ApplyTriggers()` run on it —
regenerate it from one created by `dotnet ef database update` alone and it will
record *zero triggers as correct*, forever.

### 6. Run the tests

```bash
dotnet test src/Waymark.sln -maxcpucount:1
```

The ones that matter here:

| Test | Catches |
| :---- | :---- |
| `ModelMatchesSchemaTests` | the model and the schema disagreeing |
| `SchemaInvariantTests` | a table that is not STRICT, a REAL column, a rowid-alias key |
| `AppendOnlyTests` | a trigger dropped by a rebuild — asserted by attempting the write |
| `StrictSqliteMigrationsSqlGeneratorTests` | new tables losing STRICT |

### 7. Commit the migration and the model together

A migration without its configuration change, or the other way round, leaves the
repository in a state that builds and is wrong.

---

## Things that will bite

**Never call `Migrate()` inside a transaction.** From EF Core 9 it starts its
own and uses an execution strategy; an ambient transaction raises
`MigrationsUserTransactionWarning` and throws.

**`dotnet ef database update` is not a creation path.** It applies migrations
and stops. No `ApplyTriggers()`, so the database has no append-only guards. Use
the application's own startup path, which does both.

**Always configure the context with `UseWaymarkSqlite`, never `UseSqlite`.**
Plain `UseSqlite` does not register the STRICT generator, and tables created
without it accept a REAL into a money column. This is not hypothetical — the
first version of `ModelMatchesSchemaTests` used `UseSqlite` and produced 58
non-STRICT tables while the build stayed green.

**Your `sqlite3` CLI is newer than the application's SQLite.** The CLI is 3.53;
SQLCipher gives the application 3.39.2. DDL that works at the prompt can still
fail at runtime. `dotnet ef` is the authority.

---

## Command reference

```bash
# add a migration
dotnet ef migrations add <Name> --project src/Waymark.Persistence

# see what it would run, without running it
dotnet ef migrations script --project src/Waymark.Persistence

# undo the last migration, before it has been applied anywhere
dotnet ef migrations remove --project src/Waymark.Persistence

# list migrations and whether they have been applied
dotnet ef migrations list --project src/Waymark.Persistence

# the DDL the model alone would produce - the D-019 diff, any time
dotnet ef dbcontext script --project src/Waymark.Persistence
```
