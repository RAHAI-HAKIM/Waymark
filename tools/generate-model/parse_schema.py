"""Reads the operational schema and returns a structured description of it.

Source of truth is the live database built from schema_v7_1.sql: PRAGMA gives
authoritative columns, keys, foreign keys and indexes. Only CHECK constraints
have to be read out of the DDL text, because SQLite exposes them nowhere else.
"""
import re
import sqlite3
from dataclasses import dataclass, field


@dataclass
class Column:
    name: str
    sql_type: str
    not_null: bool
    default: str | None
    pk_position: int
    checks: list[str] = field(default_factory=list)


@dataclass
class Table:
    name: str
    section: str
    columns: list[Column]
    table_checks: list[str]
    foreign_keys: list[dict]
    indexes: list[dict]
    unique_constraints: list[list[str]]


def _split_top_level(body: str) -> list[str]:
    """Split a CREATE TABLE body on commas that are not inside parentheses."""
    parts, depth, current = [], 0, []
    for ch in body:
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        if ch == "," and depth == 0:
            parts.append("".join(current))
            current = []
        else:
            current.append(ch)
    if current:
        parts.append("".join(current))
    return [p.strip() for p in parts if p.strip()]


def _strip_comments(sql: str) -> str:
    return re.sub(r"--[^\n]*", "", sql)


def _extract_check(fragment: str) -> str | None:
    """Pull the expression out of CHECK ( ... ), respecting nested parens."""
    m = re.search(r"\bCHECK\s*\(", fragment, re.I)
    if not m:
        return None
    i, depth = m.end(), 1
    start = i
    while i < len(fragment) and depth:
        if fragment[i] == "(":
            depth += 1
        elif fragment[i] == ")":
            depth -= 1
        i += 1
    return " ".join(fragment[start:i - 1].split())


def load(db_path: str, schema_path: str) -> list[Table]:
    schema_text = open(schema_path, encoding="utf-8").read()

    # The schema's own SECTION headers become the folder structure.
    section, table_section = None, {}
    for line in schema_text.split("\n"):
        m = re.match(r"^-- SECTION \d+ — (.+)$", line)
        if m:
            section = m.group(1).strip()
        m2 = re.match(r"^CREATE TABLE (\w+)", line)
        if m2 and section:
            table_section[m2.group(1)] = section

    db = sqlite3.connect(db_path)
    names = [r[0] for r in db.execute(
        "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name")]

    tables = []
    for name in names:
        ddl = _strip_comments(db.execute(
            "SELECT sql FROM sqlite_schema WHERE name=?", (name,)).fetchone()[0])
        body = ddl[ddl.index("(") + 1:ddl.rindex(")")]
        fragments = _split_top_level(body)

        inline_checks: dict[str, list[str]] = {}
        table_checks: list[str] = []
        for fragment in fragments:
            head = fragment.split()[0].upper() if fragment.split() else ""
            check = _extract_check(fragment)
            if head in ("CHECK", "FOREIGN", "PRIMARY", "UNIQUE", "CONSTRAINT"):
                if check and head == "CHECK":
                    table_checks.append(check)
            elif check:
                inline_checks.setdefault(fragment.split()[0], []).append(check)

        columns = [
            Column(
                name=r[1], sql_type=r[2], not_null=bool(r[3]), default=r[4],
                pk_position=r[5], checks=inline_checks.get(r[1], []),
            )
            for r in db.execute(f"PRAGMA table_info('{name}')")
        ]

        foreign_keys = [
            {"id": r[0], "seq": r[1], "column": r[3], "table": r[2], "to": r[4], "on_delete": r[6]}
            for r in db.execute(f"PRAGMA foreign_key_list('{name}')")
        ]

        indexes, unique_constraints = [], []
        for r in db.execute(f"PRAGMA index_list('{name}')"):
            index_name, is_unique, origin, partial = r[1], bool(r[2]), r[3], bool(r[4])
            cols = [c[2] for c in db.execute(f"PRAGMA index_info('{index_name}')")]
            if origin == "u":
                # An inline UNIQUE. Kept as a group: UNIQUE(a, b) permits a
                # repeated a, and flattening it to two constraints would
                # forbid one.
                unique_constraints.append(cols)
            elif origin == "c":
                # An explicit CREATE INDEX. The WHERE clause of a partial index
                # is part of the constraint, not decoration: without it
                # ux_parameter_current forbids a parameter having two versions.
                index_sql = db.execute(
                    "SELECT sql FROM sqlite_schema WHERE type='index' AND name=?",
                    (index_name,)).fetchone()
                where = None
                if partial and index_sql and index_sql[0]:
                    m = re.search('\\bWHERE\\b(.+)$', index_sql[0], re.I | re.S)
                    if m:
                        where = " ".join(m.group(1).split())
                indexes.append({"name": index_name, "columns": cols,
                                "unique": is_unique, "filter": where})

        tables.append(Table(
            name=name, section=table_section.get(name, "REFERENCE AND CONFIGURATION"),
            columns=columns, table_checks=table_checks,
            foreign_keys=foreign_keys, indexes=indexes,
            unique_constraints=unique_constraints,
        ))
    return tables
