// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Engine;
using Waymark.Domain.Enums;
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="ParameterRegistryEntry"/> to <c>parameter_registry</c>.
/// </summary>
internal sealed class ParameterRegistryEntryConfiguration : IEntityTypeConfiguration<ParameterRegistryEntry>
{
    public void Configure(EntityTypeBuilder<ParameterRegistryEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("parameter_registry", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_parameter_registry_scope_type",
                @"scope_type IN ('global','store','category','variant','supplier')");
            table.HasCheckConstraint(
                "ck_parameter_registry_source",
                @"source IN ('engine','cold_start_default','manual_override')");
            table.HasCheckConstraint(
                "ck_parameter_registry_is_current",
                @"is_current IN (0,1)");
            table.HasCheckConstraint(
                "ck_parameter_registry_value_number",
                @"value_number IS NOT NULL OR value_text IS NOT NULL");
            table.HasCheckConstraint(
                "ck_parameter_registry_interval_low",
                @"interval_low IS NULL OR interval_high IS NULL OR interval_high >= interval_low");
        });

        builder.HasKey(x => new { x.ParameterCode, x.ScopeType, x.ScopeId, x.Version });

        builder.Property(x => x.ParameterCode)
            .HasColumnName("parameter_code");
        builder.Property(x => x.ScopeType)
            .HasColumnName("scope_type")
            .HasConversion(EnumConverters.ScopeTypeConverter);
        builder.Property(x => x.ScopeId)
            .HasColumnName("scope_id")
            .HasDefaultValue("")
            .HasSentinel("")
            // Part of the primary key: always supplied, never generated (see PriceConfiguration).
            .ValueGeneratedNever();
        builder.Property(x => x.Version)
            .HasColumnName("version");
        builder.Property(x => x.ValueNumber)
            .HasColumnName("value_number");
        builder.Property(x => x.ValueText)
            .HasColumnName("value_text");
        builder.Property(x => x.UnitCode)
            .HasColumnName("unit_code");
        builder.Property(x => x.IntervalLow)
            .HasColumnName("interval_low");
        builder.Property(x => x.IntervalHigh)
            .HasColumnName("interval_high");
        builder.Property(x => x.Method)
            .HasColumnName("method");
        builder.Property(x => x.Source)
            .HasColumnName("source")
            .HasConversion(EnumConverters.ParameterRegistryEntrySourceConverter);
        builder.Property(x => x.ObservationCount)
            .HasColumnName("observation_count")
            .HasDefaultValue(0L)
            .HasSentinel(0L);
        builder.Property(x => x.ComputedAt)
            .HasColumnName("computed_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.IsCurrent)
            .HasColumnName("is_current")
            .HasDefaultValue(true)
            .HasSentinel(true);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => new { x.ParameterCode, x.ScopeType, x.ScopeId })
            .HasDatabaseName("ux_parameter_current")
            .IsUnique()
            .HasFilter(@"is_current = 1");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(x => x.UnitCode)
            .HasPrincipalKey(x => x.UnitCode)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
