using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Waymark.Domain.Catalogue;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Everything SQLite needs to know about <see cref="UnitOfMeasure"/>, kept out
/// of the entity so Domain stays free of EF Core.
///
/// <para>
/// One file per entity rather than a single <c>OnModelCreating</c>. With 58
/// tables that method reaches about 1,100 lines, which is the readability
/// problem that made option B unattractive in D-016.
/// </para>
/// </summary>
internal sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    /// <summary>
    /// The database spelling of <see cref="UnitDimension"/>. Written explicitly
    /// rather than with <c>HasConversion&lt;string&gt;()</c>, which would use
    /// the C# member names and put "Count" in a column whose CHECK constraint
    /// only allows "count".
    /// </summary>
    private static readonly ValueConverter<UnitDimension, string> DimensionConverter = new(
        dimension => dimension == UnitDimension.Count ? "count"
            : dimension == UnitDimension.Weight ? "weight"
            : dimension == UnitDimension.Volume ? "volume"
            : "length",
        text => text == "count" ? UnitDimension.Count
            : text == "weight" ? UnitDimension.Weight
            : text == "volume" ? UnitDimension.Volume
            : UnitDimension.Length);

    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("units_of_measure", table =>
        {
            // CHECK constraints belong in the model for the same reason indexes
            // do: an EF table rebuild recreates the table from the model alone,
            // and anything not declared here is silently dropped. Verified —
            // a rebuild took CHECK (amount > 0) off a table and a negative
            // amount was accepted afterwards with no error.
            table.HasCheckConstraint(
                "ck_units_of_measure_dimension",
                "dimension IN ('count','weight','volume','length')");
            table.HasCheckConstraint("ck_units_of_measure_factor_to_base", "factor_to_base > 0");
            table.HasCheckConstraint("ck_units_of_measure_decimal_places", "decimal_places BETWEEN 0 AND 3");
            table.HasCheckConstraint("ck_units_of_measure_is_active", "is_active IN (0,1)");
        });

        builder.HasKey(unit => unit.UnitCode);

        builder.Property(unit => unit.UnitCode).HasColumnName("unit_code");
        builder.Property(unit => unit.NameAr).HasColumnName("name_ar");
        builder.Property(unit => unit.NameFr).HasColumnName("name_fr");

        builder.Property(unit => unit.Dimension)
            .HasColumnName("dimension")
            .HasConversion(DimensionConverter);

        builder.Property(unit => unit.BaseUnitCode).HasColumnName("base_unit_code");

        builder.Property(unit => unit.FactorToBase)
            .HasColumnName("factor_to_base")
            .HasDefaultValue(1_000_000L);

        builder.Property(unit => unit.DecimalPlaces)
            .HasColumnName("decimal_places")
            .HasDefaultValue(0);

        // bool maps to INTEGER 0/1 on SQLite without help, which is the
        // schema's convention already.
        builder.Property(unit => unit.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(unit => unit.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // Self-referencing: grams reduce to kilograms. No navigation property —
        // nothing needs to walk it yet, and an unused navigation is a lazy-load
        // surprise waiting to happen.
        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(unit => unit.BaseUnitCode)
            .HasPrincipalKey(unit => unit.UnitCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
