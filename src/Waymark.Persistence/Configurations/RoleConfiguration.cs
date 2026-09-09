// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Engine;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Role"/> to <c>roles</c>.
/// </summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("roles", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_roles_rank",
                @"rank > 0");
            table.HasCheckConstraint(
                "ck_roles_is_active",
                @"is_active IN (0,1)");
        });

        builder.HasKey(x => x.RoleCode);

        builder.Property(x => x.RoleCode)
            .HasColumnName("role_code");
        builder.Property(x => x.Rank)
            .HasColumnName("rank");
        builder.Property(x => x.LabelAr)
            .HasColumnName("label_ar");
        builder.Property(x => x.LabelFr)
            .HasColumnName("label_fr");
        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .HasSentinel(true);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.Rank).IsUnique();
    }
}
