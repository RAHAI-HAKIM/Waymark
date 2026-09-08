"""Emits the C# entities, enums and EF configurations."""
import naming

DELETE_BEHAVIOUR = {
    "NO ACTION": "NoAction",
    "RESTRICT": "Restrict",
    "CASCADE": "Cascade",
    "SET NULL": "SetNull",
    "SET DEFAULT": "ClientSetNull",
}

HEADER = """// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.
"""


def enum_file(enum) -> str:
    lines = [HEADER, "namespace Waymark.Domain.Enums;", "", "/// <summary>",
             f"/// Stored as TEXT with a CHECK constraint: {', '.join(repr(v) for v in enum.values)}.",
             "/// </summary>", f"public enum {enum.name}", "{"]
    for i, (value, member) in enumerate(zip(enum.values, enum.members)):
        comma = "," if i < len(enum.values) - 1 else ""
        lines.append(f"    /// <summary>Stored as <c>{value}</c>.</summary>")
        lines.append(f"    {member}{comma}")
        if comma:
            lines.append("")
    lines += ["}", ""]
    return "\n".join(lines)


def entity_file(table, entity, folder, props) -> str:
    needs_enums = any(p.enum for p in props)
    lines = [HEADER]
    if needs_enums:
        lines += ["using Waymark.Domain.Enums;", ""]
    lines += [f"namespace Waymark.Domain.{folder};", "", "/// <summary>",
              f"/// Maps to <c>{table.name}</c>.",
              "///", "/// <para>",
              "/// A plain class: no attributes, no EF Core, nothing that leaves this",
              "/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),",
              f"/// and how this reaches SQLite lives in <c>{entity}Configuration</c>.",
              "/// </para>", "/// </summary>", f"public sealed class {entity}", "{"]

    for i, p in enumerate(props):
        if i:
            lines.append("")
        kind = p.csharp_type + ("?" if p.nullable else "")
        # `required` only where the caller genuinely must supply a value: not
        # nullable, and no database default to fall back on.
        needs_value = not p.nullable and p.default is None
        modifier = "required " if needs_value else ""
        # A database default of 0 or false is already the C# default, and
        # writing it out trips CA1805. The property still is not `required`,
        # because the database will supply the value.
        redundant = p.default in ("0L", "false", "0")
        suffix = f" = {p.default};" if p.default is not None and not redundant else ""
        if p.is_key:
            lines.append(f"    /// <summary>Part of the primary key (<c>{p.column}</c>).</summary>"
                         if len([q for q in props if q.is_key]) > 1
                         else f"    /// <summary>Primary key (<c>{p.column}</c>).</summary>")
        lines.append(f"    public {modifier}{kind} {p.name} {{ get; init; }}{suffix}")

    lines += ["}", ""]
    return "\n".join(lines)


def configuration_file(table, entity, folder, props, checks, keys, fks, entity_of, folder_of) -> str:
    usings = {f"using Waymark.Domain.{folder};"}
    if any(p.enum for p in props):
        usings.add("using Waymark.Domain.Enums;")
    for fk in fks:
        target_folder = folder_of[fk["table"]]
        usings.add(f"using Waymark.Domain.{target_folder};")

    lines = [HEADER, "using Microsoft.EntityFrameworkCore;",
             "using Microsoft.EntityFrameworkCore.Metadata.Builders;"]
    lines += sorted(usings)
    lines += ["", "namespace Waymark.Persistence.Configurations;", "",
              "/// <summary>",
              f"/// Maps <see cref=\"{entity}\"/> to <c>{table.name}</c>.",
              "/// </summary>",
              f"internal sealed class {entity}Configuration : IEntityTypeConfiguration<{entity}>",
              "{", f"    public void Configure(EntityTypeBuilder<{entity}> builder)", "    {",
              "        ArgumentNullException.ThrowIfNull(builder);", ""]

    if checks:
        lines.append(f"        builder.ToTable(\"{table.name}\", table =>")
        lines.append("        {")
        lines.append("            // Declared here as well as in the schema: an EF table rebuild")
        lines.append("            // recreates the table from the model alone and drops every CHECK")
        lines.append("            // it does not know about (decisions.md D-022).")
        for name, expression in checks:
            escaped = expression.replace('"', '""')
            lines.append(f"            table.HasCheckConstraint(")
            lines.append(f"                \"{name}\",")
            lines.append(f"                @\"{escaped}\");")
        lines.append("        });")
    else:
        lines.append(f"        builder.ToTable(\"{table.name}\");")
    lines.append("")

    if len(keys) == 1:
        lines.append(f"        builder.HasKey(x => x.{keys[0].name});")
    else:
        joined = ", ".join(f"x.{k.name}" for k in keys)
        lines.append(f"        builder.HasKey(x => new {{ {joined} }});")
    lines.append("")

    for p in props:
        call = [f"        builder.Property(x => x.{p.name})", f"            .HasColumnName(\"{p.column}\")"]
        if p.converter:
            call.append(f"            .HasConversion({p.converter})")
        if p.default is not None:
            call.append(f"            .HasDefaultValue({p.default})")
        lines.append("\n".join(call) + ";")

    index_lines = []
    for column in sorted(set(table.unique_columns)):
        prop = next(p for p in props if p.column == column)
        index_lines.append(
            f"        builder.HasIndex(x => x.{prop.name}).IsUnique();")
    for index in table.indexes:
        names = [next(p for p in props if p.column == c).name for c in index["columns"]]
        target = f"x.{names[0]}" if len(names) == 1 else "new { " + ", ".join(f"x.{n}" for n in names) + " }"
        unique = ".IsUnique()" if index["unique"] else ""
        index_lines.append(
            f"        builder.HasIndex(x => {target})\n"
            f"            .HasDatabaseName(\"{index['name']}\"){unique};")
    if index_lines:
        lines += ["", "        // A rebuild recreates only the indexes the model declares.", *index_lines]

    fk_lines = []
    for fk in fks:
        target = entity_of[fk["table"]]
        fk_props = [next(p for p in props if p.column == c).name for c in fk["columns"]]
        principal_entity_props = fk["principal"]
        behaviour = DELETE_BEHAVIOUR.get(fk["on_delete"], "NoAction")
        fk_target = (f"x.{fk_props[0]}" if len(fk_props) == 1
                     else "new { " + ", ".join(f"x.{n}" for n in fk_props) + " }")
        principal_names = [naming.property_name(c, target) for c in principal_entity_props]
        principal_target = (f"x.{principal_names[0]}" if len(principal_names) == 1
                            else "new { " + ", ".join(f"x.{n}" for n in principal_names) + " }")
        fk_lines.append(
            f"        builder.HasOne<{target}>()\n"
            f"            .WithMany()\n"
            f"            .HasForeignKey(x => {fk_target})\n"
            f"            .HasPrincipalKey(x => {principal_target})\n"
            f"            .OnDelete(DeleteBehavior.{behaviour});")
    if fk_lines:
        lines += ["", "        // Foreign keys are dropped by a rebuild too, for the same reason.",
                  *fk_lines]

    lines += ["    }", "}", ""]
    return "\n".join(lines)
