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
/// Maps <see cref="UnitOfMeasure"/> to <c>units_of_measure</c>.
/// </summary>
internal sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("units_of_measure", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_units_of_measure_dimension",
                @"dimension IN ('count','weight','volume','length')");
            table.HasCheckConstraint(
                "ck_units_of_measure_factor_to_base",
                @"factor_to_base > 0");
            table.HasCheckConstraint(
                "ck_units_of_measure_decimal_places",
                @"decimal_places BETWEEN 0 AND 3");
            table.HasCheckConstraint(
                "ck_units_of_measure_is_active",
                @"is_active IN (0,1)");
        });

        builder.HasKey(x => x.UnitCode);

        builder.Property(x => x.UnitCode)
            .HasColumnName("unit_code");
        builder.Property(x => x.NameAr)
            .HasColumnName("name_ar");
        builder.Property(x => x.NameFr)
            .HasColumnName("name_fr");
        builder.Property(x => x.Dimension)
            .HasColumnName("dimension")
            .HasConversion(EnumConverters.DimensionConverter);
        builder.Property(x => x.BaseUnitCode)
            .HasColumnName("base_unit_code");
        builder.Property(x => x.FactorToBase)
            .HasColumnName("factor_to_base")
            .HasDefaultValue(1000000L);
        builder.Property(x => x.DecimalPlaces)
            .HasColumnName("decimal_places")
            .HasDefaultValue(0L);
        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(x => x.BaseUnitCode)
            .HasPrincipalKey(x => x.UnitCode)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
