"""Structural diff between the schema-built database and the EF model's own.

This is the D-019 one-time comparison, run mechanically: the only moment the
58 configurations can be checked against the reviewed schema for free.
"""
import os
import re
import sqlite3
import sys
import tempfile


def describe(path: str) -> dict:
    db = sqlite3.connect(path)
    out = {"tables": {}, "indexes": {}, "strict": {}}
    for (name,) in db.execute(
            "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' "
            "AND name NOT LIKE '__EF%' ORDER BY name"):
        cols = {}
        for r in db.execute(f"PRAGMA table_info('{name}')"):
            cols[r[1]] = {"type": r[2].upper(), "notnull": bool(r[3]), "pk": r[5]}
        fks = sorted(
            (r[3], r[2], r[4]) for r in db.execute(f"PRAGMA foreign_key_list('{name}')"))
        out["tables"][name] = {"columns": cols, "fks": fks}
    for r in db.execute("SELECT name, tbl_name, sql FROM sqlite_schema WHERE type='index' "
                        "AND name NOT LIKE 'sqlite_%' ORDER BY name"):
        cols = tuple(c[2] for c in db.execute(f"PRAGMA index_info('{r[0]}')"))
        sql = r[2] or ""
        unique = " UNIQUE " in sql.upper()
        m = re.search(r"\bWHERE\b(.+)$", sql, re.I | re.S)
        # Identifier quoting differs between the schema and EF, so compare the
        # filter with quotes stripped and whitespace collapsed.
        where = " ".join(m.group(1).replace('"', "").split()).lower() if m else None
        out["indexes"][r[0]] = (r[1], cols, unique, where)

    # Inline UNIQUE(a, b) makes an implicit index. Compare it as a set of
    # column groups, since EF must name its equivalent differently.
    out["unique_groups"] = set()
    for name in out["tables"]:
        for r in db.execute(f"PRAGMA index_list('{name}')"):
            if r[3] in ("u", "c") and r[2]:
                cols = tuple(c[2] for c in db.execute(f"PRAGMA index_info('{r[1]}')"))
                out["unique_groups"].add((name, cols))
    for r in db.execute("SELECT name, strict FROM pragma_table_list WHERE schema='main' "
                        "AND type='table' AND name NOT LIKE 'sqlite_%'"):
        out["strict"][r[0]] = bool(r[1])
    return out


def main(schema_db: str, ef_sql: str) -> int:
    tmp = os.path.join(tempfile.mkdtemp(), "ef.db")
    db = sqlite3.connect(tmp)
    db.executescript(open(ef_sql, encoding="utf-8").read())
    db.commit()
    db.close()

    a, b = describe(schema_db), describe(tmp)
    problems, notes = [], []

    missing = sorted(set(a["tables"]) - set(b["tables"]))
    extra = sorted(set(b["tables"]) - set(a["tables"]))
    if missing:
        problems.append(f"tables in the schema but not the model: {missing}")
    if extra:
        problems.append(f"tables in the model but not the schema: {extra}")

    for name in sorted(set(a["tables"]) & set(b["tables"])):
        ac, bc = a["tables"][name]["columns"], b["tables"][name]["columns"]
        for col in sorted(set(ac) - set(bc)):
            problems.append(f"{name}.{col}: in the schema, missing from the model")
        for col in sorted(set(bc) - set(ac)):
            problems.append(f"{name}.{col}: in the model, not in the schema")
        for col in sorted(set(ac) & set(bc)):
            if ac[col]["type"] != bc[col]["type"]:
                problems.append(f"{name}.{col}: type {ac[col]['type']} vs {bc[col]['type']}")
            if ac[col]["notnull"] != bc[col]["notnull"]:
                problems.append(
                    f"{name}.{col}: NOT NULL {ac[col]['notnull']} vs {bc[col]['notnull']}")
            if (ac[col]["pk"] > 0) != (bc[col]["pk"] > 0):
                problems.append(f"{name}.{col}: primary key membership differs")
        if a["tables"][name]["fks"] != b["tables"][name]["fks"]:
            only_a = set(a["tables"][name]["fks"]) - set(b["tables"][name]["fks"])
            only_b = set(b["tables"][name]["fks"]) - set(a["tables"][name]["fks"])
            if only_a:
                problems.append(f"{name}: foreign keys missing from the model: {sorted(only_a)}")
            if only_b:
                problems.append(f"{name}: foreign keys only in the model: {sorted(only_b)}")
        if a["strict"].get(name) and not b["strict"].get(name):
            problems.append(f"{name}: STRICT in the schema, not in the model")

    for name, (table, cols, unique, where) in sorted(a["indexes"].items()):
        if name not in b["indexes"]:
            problems.append(f"index {name} on {table}{cols}: missing from the model")
            continue
        _, bcols, bunique, bwhere = b["indexes"][name]
        if bcols != cols:
            problems.append(f"index {name}: columns {cols} vs {bcols}")
        if bunique != unique:
            problems.append(f"index {name}: unique {unique} vs {bunique}")
        if bwhere != where:
            problems.append(f"index {name}: filter {where!r} vs {bwhere!r}")
    for name, (table, cols, _, _) in sorted(b["indexes"].items()):
        if name not in a["indexes"]:
            notes.append(f"index {name} on {table}{cols}: added by EF, not in the schema")

    for entry in sorted(a["unique_groups"] - b["unique_groups"]):
        problems.append(f"unique constraint {entry[0]}{entry[1]}: missing from the model")
    for entry in sorted(b["unique_groups"] - a["unique_groups"]):
        problems.append(f"unique constraint {entry[0]}{entry[1]}: only in the model "
                        f"(over-constrains the data)")

    print(f"schema tables: {len(a['tables'])}   model tables: {len(b['tables'])}")
    print(f"schema indexes: {len(a['indexes'])}  model indexes: {len(b['indexes'])}")
    print()
    if problems:
        print(f"DIFFERENCES THAT MATTER ({len(problems)})")
        for p in problems:
            print("  " + p)
    else:
        print("No structural differences in tables, columns, types, nullability,")
        print("primary keys, foreign keys or STRICT.")
    if notes:
        print(f"\nEXPECTED EXTRAS ({len(notes)}) — EF indexes every foreign key")
        for n in notes[:8]:
            print("  " + n)
        if len(notes) > 8:
            print(f"  ... and {len(notes) - 8} more")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
