using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Cash;
using Waymark.Domain.Catalogue;

namespace Waymark.Persistence;

/// <summary>
/// The operational store database, <c>waymark-store.db</c>.
///
/// <para>
/// Options arrive through the constructor rather than being built in
/// <c>OnConfiguring</c>. The database path is configuration, not a constant —
/// it is <c>C:\ProgramData\Waymark\data</c> on a till and a temporary directory
/// under test (D-013) — and a context that builds its own connection string
/// cannot be pointed anywhere else.
/// </para>
/// <para>
/// This context does not own <c>waymark-identity.db</c> and never will. Only
/// <c>Waymark.Pseudonymisation</c> holds that path (CLAUDE.md §3.4).
/// </para>
/// </summary>
public sealed class WaymarkDbContext(DbContextOptions<WaymarkDbContext> options)
    : DbContext(options)
{
    // Expression-bodied, so no nullable warning and no `= null!` to silence it.
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    public DbSet<CashMovement> CashMovements => Set<CashMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Picks up every IEntityTypeConfiguration in this assembly. Adding an
        // entity means adding one file, never editing this method — which is
        // what keeps it three lines long at 58 tables instead of 1,100.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WaymarkDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
