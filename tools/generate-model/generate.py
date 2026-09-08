"""One-shot bootstrap: writes the 58 entities, their enums and configurations.

Run once, review the output, then maintain the result by hand. Re-running after
hand edits overwrites them.
"""
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import context
import emit
import model
import naming
import parse_schema

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DB = r"C:\ProgramData\Waymark\data\waymark-store.db"
SCHEMA = os.path.join(ROOT, "src", "Waymark.Persistence", "schema_v7_1.sql")
DOMAIN = os.path.join(ROOT, "src", "Waymark.Domain")
CONFIGS = os.path.join(ROOT, "src", "Waymark.Persistence", "Configurations")


def write(path: str, text: str) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def enum_converters_file(enums) -> str:
    lines = [emit.HEADER,
             "using Microsoft.EntityFrameworkCore.Storage.ValueConversion;",
             "using Waymark.Domain.Enums;", "",
             "namespace Waymark.Persistence.Configurations;", "",
             "/// <summary>",
             "/// Enum to TEXT, one converter per enum.",
             "///",
             "/// <para>",
             "/// Written out rather than using <c>HasConversion&lt;string&gt;()</c>, which",
             "/// stores the C# member name: \"Standard\" into a column whose CHECK",
             "/// constraint only allows \"standard\". The sync channels settle it — their",
             "/// values are <c>A_statistics</c> and <c>D_intents</c>, which no mechanical",
             "/// rule produces.",
             "/// </para>",
             "/// <para>",
             "/// Both directions throw on an unknown value. A row carrying a spelling the",
             "/// model does not know is a real problem, and silently mapping it to the",
             "/// first member would hide it.",
             "/// </para>",
             "/// </summary>",
             "internal static class EnumConverters", "{"]

    for name in sorted(enums):
        e = enums[name]
        lines.append(f"    public static readonly ValueConverter<{name}, string> {name}Converter =")
        lines.append(f"        new(value => ToDatabase(value), text => To{name}(text));")
        lines.append("")

    for name in sorted(enums):
        e = enums[name]
        lines.append(f"    private static string ToDatabase({name} value) => value switch")
        lines.append("    {")
        for value, member in zip(e.values, e.members):
            lines.append(f"        {name}.{member} => \"{value}\",")
        lines.append(f"        _ => throw new ArgumentOutOfRangeException(")
        lines.append(f"            nameof(value), value, \"Unmapped {name}.\")")
        lines.append("    };")
        lines.append("")
        lines.append(f"    private static {name} To{name}(string text) => text switch")
        lines.append("    {")
        for value, member in zip(e.values, e.members):
            lines.append(f"        \"{value}\" => {name}.{member},")
        lines.append(f"        _ => throw new ArgumentOutOfRangeException(")
        lines.append(f"            nameof(text), text, \"Unknown {name} value in the database.\")")
        lines.append("    };")
        lines.append("")

    lines += ["}", ""]
    return "\n".join(lines)


def main() -> None:
    tables = parse_schema.load(DB, SCHEMA)
    enums, per_column = model.collect_enums(tables)

    entity_of = {t.name: naming.entity_name(t.name) for t in tables}
    folder_of = {t.name: naming.SECTION_FOLDERS[t.section] for t in tables}

    # Start from clean folders so a renamed table cannot leave an orphan behind.
    for folder in set(folder_of.values()) | {"Enums"}:
        path = os.path.join(DOMAIN, folder)
        if os.path.isdir(path):
            shutil.rmtree(path)
    for old in ("Catalogue", "Cash", "Reason", "ProductVariant"):
        path = os.path.join(DOMAIN, old)
        if os.path.isdir(path):
            shutil.rmtree(path)
    if os.path.isdir(CONFIGS):
        for f in os.listdir(CONFIGS):
            if f.endswith("Configuration.cs"):
                os.remove(os.path.join(CONFIGS, f))

    for name, e in enums.items():
        write(os.path.join(DOMAIN, "Enums", f"{name}.cs"), emit.enum_file(e))

    dbsets = []
    for t in tables:
        entity, folder = entity_of[t.name], folder_of[t.name]
        props = model.build_props(t, per_column)
        write(os.path.join(DOMAIN, folder, f"{entity}.cs"),
              emit.entity_file(t, entity, folder, props))
        write(os.path.join(CONFIGS, f"{entity}Configuration.cs"),
              emit.configuration_file(
                  t, entity, folder, props, model.check_constraints(t),
                  model.key_props(t, props), model.foreign_keys(t), entity_of, folder_of))
        dbsets.append((naming.pascal(t.name), entity, folder))

    write(os.path.join(CONFIGS, "EnumConverters.cs"), enum_converters_file(enums))

    write(os.path.join(ROOT, "src", "Waymark.Persistence", "WaymarkDbContext.cs"),
          context.context_file(sorted(dbsets)))

    print(f"{len(tables)} entities, {len(enums)} enums, {len(tables)} configurations, "
          f"{len(dbsets)} DbSets")


if __name__ == "__main__":
    main()
