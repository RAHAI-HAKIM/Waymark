// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Enums;
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="SystemConfigEntry"/> to <c>system_config</c>.
/// </summary>
internal sealed class SystemConfigEntryConfiguration : IEntityTypeConfiguration<SystemConfigEntry>
{
    public void Configure(EntityTypeBuilder<SystemConfigEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("system_config", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_system_config_data_type",
                @"data_type IN ('text','integer','money','percent','bool','date')");
        });

        builder.HasKey(x => x.ConfigKey);

        builder.Property(x => x.ConfigKey)
            .HasColumnName("config_key");
        builder.Property(x => x.ConfigValue)
            .HasColumnName("config_value");
        builder.Property(x => x.DataType)
            .HasColumnName("data_type")
            .HasConversion(EnumConverters.SystemConfigEntryDataTypeConverter);
        builder.Property(x => x.Description)
            .HasColumnName("description");
        builder.Property(x => x.UpdatedBy)
            .HasColumnName("updated_by");
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);
    }
}
