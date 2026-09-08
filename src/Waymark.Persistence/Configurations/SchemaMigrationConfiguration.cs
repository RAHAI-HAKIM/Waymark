// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="SchemaMigration"/> to <c>schema_migrations</c>.
/// </summary>
internal sealed class SchemaMigrationConfiguration : IEntityTypeConfiguration<SchemaMigration>
{
    public void Configure(EntityTypeBuilder<SchemaMigration> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("schema_migrations");

        builder.HasKey(x => x.Version);

        builder.Property(x => x.Version)
            .HasColumnName("version");
        builder.Property(x => x.Description)
            .HasColumnName("description");
        builder.Property(x => x.AppliedAt)
            .HasColumnName("applied_at")
            .HasConversion(WaymarkConverters.Timestamp);
    }
}
