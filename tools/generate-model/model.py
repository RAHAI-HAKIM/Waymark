"""Turns the parsed schema into the C# model: types, enums, keys, constraints."""
import re
from dataclasses import dataclass, field

import naming


@dataclass
class EnumType:
    name: str
    values: list[str]                     # database spellings, in schema order
    members: list[str] = field(default_factory=list)


@dataclass
class Prop:
    column: str
    name: str
    csharp_type: str
    nullable: bool
    is_key: bool
    enum: EnumType | None
    converter: str | None
    default: str | None                   # C# literal for HasDefaultValue
    comment: str | None = None


# Names that would collide with a type in System, so the bare column name
# cannot be used: `Action` and `ValueType` both exist there, and the ambiguity
# is a compile error rather than a subtle one.
RESERVED = {
    "Action", "ValueType", "Type", "Object", "String", "Enum", "Attribute",
    "Exception", "Delegate", "Array", "Index", "Range", "Version", "Guid",
    "Convert", "Math", "Environment", "Console", "Random", "Comparison",
    "Predicate", "Func", "Task", "Tuple", "Span", "Memory", "Activator",
}


def _enum_values(check: str, column: str) -> list[str] | None:
    """'status IN (\'a\',\'b\')' -> ['a', 'b'], else None."""
    m = re.match(rf"^{re.escape(column)}\s+IN\s*\((.*)\)$", check.strip(), re.I)
    if not m or "'" not in m.group(1):
        return None
    return re.findall(r"'([^']*)'", m.group(1))


def _is_boolean(check: str, column: str) -> bool:
    m = re.match(rf"^{re.escape(column)}\s+IN\s*\(\s*0\s*,\s*1\s*\)$", check.strip(), re.I)
    return m is not None


def collect_enums(tables) -> tuple[dict, dict]:
    """Name every enum. A column name shared by two different value sets gets
    the entity name in front of it, so ProductStatus and PromotionStatus can
    both exist."""
    by_column: dict[str, set] = {}
    for t in tables:
        for c in t.columns:
            for chk in c.checks:
                values = _enum_values(chk, c.name)
                if values:
                    by_column.setdefault(c.name, set()).add(tuple(values))

    enums, per_column = {}, {}
    for t in tables:
        entity = naming.entity_name(t.name)
        for c in t.columns:
            for chk in c.checks:
                values = _enum_values(chk, c.name)
                if not values:
                    continue
                shared = len(by_column[c.name]) == 1
                bare = naming.pascal(c.name)
                name = (bare if shared and bare not in RESERVED
                        else naming.qualified_enum_name(entity, c.name))
                if name in enums and enums[name].values != values:
                    name = naming.qualified_enum_name(entity, c.name)
                enums.setdefault(name, EnumType(
                    name=name, values=values,
                    members=[naming.enum_member(v) for v in values]))
                per_column[(t.name, c.name)] = enums[name]
    return enums, per_column


def _default_literal(default: str, prop_type: str, enum: EnumType | None) -> str | None:
    if default is None:
        return None
    raw = default.strip()
    if enum is not None:
        value = raw.strip("'")
        if value not in enum.values:
            return None
        return f"{enum.name}.{enum.members[enum.values.index(value)]}"
    if prop_type == "bool":
        return "true" if raw == "1" else "false"
    if prop_type == "long":
        return f"{int(raw)}L"
    if prop_type in ("string",):
        return '"' + raw.strip("'").replace('"', '\\"') + '"'
    return None                                   # dates and timestamps: left to the schema


def build_props(table, per_column) -> list[Prop]:
    props = []
    entity = naming.entity_name(table.name)
    for c in table.columns:
        enum = per_column.get((table.name, c.name))
        boolean = any(_is_boolean(chk, c.name) for chk in c.checks)

        if enum is not None:
            csharp, converter = enum.name, f"EnumConverters.{enum.name}Converter"
        elif boolean:
            csharp, converter = "bool", None
        elif c.sql_type == "TEXT" and c.name.endswith("_at"):
            csharp, converter = "DateTimeOffset", "WaymarkConverters.Timestamp"
        elif c.sql_type == "TEXT" and c.name.endswith("_date"):
            csharp, converter = "DateOnly", "WaymarkConverters.Date"
        elif c.sql_type == "TEXT":
            csharp, converter = "string", None
        elif c.sql_type == "INTEGER":
            # long for every integer column without exception. int is where
            # silent overflow lives, and the saving is four bytes in memory.
            csharp, converter = "long", None
        else:
            raise ValueError(f"unmapped type {c.sql_type} on {table.name}.{c.name}")

        props.append(Prop(
            column=c.name,
            name=naming.property_name(c.name, entity),
            csharp_type=csharp,
            nullable=not c.not_null,
            is_key=c.pk_position > 0,
            enum=enum,
            converter=converter,
            default=_default_literal(c.default, csharp, enum),
        ))
    return props


def check_constraints(table) -> list[tuple[str, str]]:
    """Every CHECK, with a unique name. Nameless in SQLite; EF requires names."""
    used, out = set(), []
    for c in table.columns:
        for chk in c.checks:
            out.append((f"ck_{table.name}_{c.name}", chk))
    for chk in table.table_checks:
        first = re.match(r"^\(*\s*(\w+)", chk)
        stem = first.group(1) if first else "rule"
        out.append((f"ck_{table.name}_{stem}", chk))

    named = []
    for name, chk in out:
        candidate, n = name, 2
        while candidate in used:
            candidate, n = f"{name}_{n}", n + 1
        used.add(candidate)
        named.append((candidate, chk))
    return named


def key_props(table, props) -> list[Prop]:
    ordered = sorted(
        [c for c in table.columns if c.pk_position > 0], key=lambda c: c.pk_position)
    by_column = {p.column: p for p in props}
    return [by_column[c.name] for c in ordered]


def foreign_keys(table) -> list[dict]:
    """Group PRAGMA rows into one entry per constraint."""
    groups: dict[int, list] = {}
    for fk in table.foreign_keys:
        groups.setdefault(fk["id"], []).append(fk)
    out = []
    for _, rows in sorted(groups.items()):
        rows.sort(key=lambda r: r["seq"])
        out.append({
            "columns": [r["column"] for r in rows],
            "table": rows[0]["table"],
            "principal": [r["to"] for r in rows],
            "on_delete": rows[0]["on_delete"],
        })
    return out
