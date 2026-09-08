# generate-model

**One-shot.** This wrote the 58 entities, 70 enums and 58 EF configurations on
08/09/2026 from `schema_v7_1.sql`. It has done its job.

> **Do not re-run it.** It deletes and rewrites every file it owns, so anything
> hand-edited since is lost. Under D-019 the schema is evolved by migrations
> from here on, not regenerated — a generator pointed at a frozen file would
> only ever reproduce v1.

It is kept because the provenance matters. Fifty-eight configurations that
nobody can account for are fifty-eight places to hide a mistake, and "these
were derived mechanically from the reviewed schema, and here is the derivation"
is a better answer than "they were typed in".

## How it worked

| File | Job |
| :---- | :---- |
| `parse_schema.py` | Reads the database built from the schema. PRAGMA gives columns, keys, foreign keys and indexes; CHECK constraints are parsed out of the DDL, because SQLite exposes them nowhere else |
| `naming.py` | snake_case to C#, singularisation, and the names the default rule gets wrong |
| `model.py` | Type mapping, enum discovery, CHECK naming, key and foreign-key grouping |
| `emit.py` | Writes the entity and configuration source |
| `context.py` | Writes `WaymarkDbContext`, so the DbSet list cannot drift from the entities |
| `compare.py` | Structural diff: schema-built database against model-built database |

## The rules it applied

- **Every `INTEGER` column becomes `long`.** No exceptions, including counts
  and display orders. `int` is where silent overflow lives — the scaffolder's
  default overflows a money column at 21,474,836.47 DZD — and a uniform rule
  cannot be misapplied by judgment.
- **`INTEGER` with `CHECK (col IN (0,1))` becomes `bool`.**
- **`TEXT` ending `_at` becomes `DateTimeOffset`**, `_date` becomes `DateOnly`,
  both through the converters in `WaymarkConverters`.
- **`TEXT` with `CHECK (col IN ('a','b'))` becomes an enum**, with an explicit
  converter. Never `HasConversion<string>()`, which would store the C# member
  name: `"Standard"` into a column whose CHECK allows only `"standard"`.
- **Every CHECK, index and foreign key is declared in the configuration**, because
  an EF table rebuild recreates the table from the model alone and drops what it
  does not know about (D-022).
- **`required` only where a value must be supplied** — not nullable, and no
  database default to fall back on.

## Checking its work

`compare.py` builds a database from the model's own create script and diffs it
against the schema-built one:

```
python tools/generate-model/compare.py \
    C:/ProgramData/Waymark/data/waymark-store.db \
    path/to/ef-model.sql
```

The same comparison runs permanently as `ModelMatchesSchemaTests`, which is the
one that matters — `HasPendingModelChanges()` compares the model to its own
snapshot and cannot see the schema at all.
