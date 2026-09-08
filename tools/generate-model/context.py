"""Writes WaymarkDbContext, so the DbSet list cannot drift from the entities."""
import emit


def context_file(dbsets) -> str:
    by_folder = {}
    for setname, entity, folder in dbsets:
        by_folder.setdefault(folder, []).append((setname, entity))

    usings = "\n".join(f"using Waymark.Domain.{f};" for f in sorted(by_folder))
    blocks = []
    for folder in sorted(by_folder):
        blocks.append(f"    // {folder}")
        for setname, entity in sorted(by_folder[folder]):
            blocks.append(f"    public DbSet<{entity}> {setname} => Set<{entity}>();")
        blocks.append("")

    body = "\n".join(blocks).rstrip()
    return f'''{emit.HEADER}using Microsoft.EntityFrameworkCore;
{usings}

namespace Waymark.Persistence;

/// <summary>
/// The operational store database, <c>waymark-store.db</c>.
///
/// <para>
/// Options arrive through the constructor rather than being built in
/// <c>OnConfiguring</c>. The database path is configuration, not a constant —
/// <c>ProgramData\Waymark\data</c> on a till and a temporary directory under
/// test (D-013) — and a context that builds its own connection string cannot be
/// pointed anywhere else.
/// </para>
/// <para>
/// This context does not own <c>waymark-identity.db</c> and never will. Only
/// <c>Waymark.Pseudonymisation</c> holds that path (CLAUDE.md §3.4).
/// </para>
/// </summary>
public sealed class WaymarkDbContext(DbContextOptions<WaymarkDbContext> options)
    : DbContext(options)
{{
{body}

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {{
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Picks up every IEntityTypeConfiguration in this assembly. Adding an
        // entity means adding one file, never editing this method — which is
        // what keeps it short at 58 tables instead of 1,100 lines.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WaymarkDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }}
}}
'''
