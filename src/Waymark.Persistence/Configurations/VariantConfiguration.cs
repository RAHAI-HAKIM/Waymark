// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Enums;
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Variant"/> to <c>variants</c>.
/// </summary>
internal sealed class VariantConfiguration : IEntityTypeConfiguration<Variant>
{
    public void Configure(EntityTypeBuilder<Variant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("variants", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_variants_barcode_type",
                @"barcode_type IN ('standard', 'weight_embedded', 'price_embedded')");
            table.HasCheckConstraint(
                "ck_variants_is_weighted",
                @"is_weighted IN (0,1)");
            table.HasCheckConstraint(
                "ck_variants_status",
                @"status IN ('active','discontinued','archived')");
            table.HasCheckConstraint(
                "ck_variants_tare_weight",
                @"tare_weight >= 0");
            table.HasCheckConstraint(
                "ck_variants_is_weighted_2",
                @"is_weighted = 0 OR plu IS NOT NULL OR barcode IS NOT NULL");
        });

        builder.HasKey(x => x.VariantId);

        builder.Property(x => x.VariantId)
            .HasColumnName("variant_id");
        builder.Property(x => x.ProductId)
            .HasColumnName("product_id");
        builder.Property(x => x.VariantName)
            .HasColumnName("variant_name");
        builder.Property(x => x.Barcode)
            .HasColumnName("barcode");
        builder.Property(x => x.Plu)
            .HasColumnName("plu");
        builder.Property(x => x.Sku)
            .HasColumnName("sku");
        builder.Property(x => x.BarcodeType)
            .HasColumnName("barcode_type")
            .HasConversion(EnumConverters.BarcodeTypeConverter)
            .HasDefaultValue(BarcodeType.Standard);
        builder.Property(x => x.SellingUnitCode)
            .HasColumnName("selling_unit_code");
        builder.Property(x => x.IsWeighted)
            .HasColumnName("is_weighted")
            .HasDefaultValue(false);
        builder.Property(x => x.TareWeight)
            .HasColumnName("tare_weight")
            .HasDefaultValue(0L);
        builder.Property(x => x.Image)
            .HasColumnName("image");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.VariantStatusConverter)
            .HasDefaultValue(VariantStatus.Active);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.Sku).IsUnique();
        builder.HasIndex(x => x.Plu).IsUnique();
        builder.HasIndex(x => x.Barcode).IsUnique();
        builder.HasIndex(x => x.Status)
            .HasDatabaseName("ix_variants_status");
        builder.HasIndex(x => x.ProductId)
            .HasDatabaseName("ix_variants_product");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(x => x.SellingUnitCode)
            .HasPrincipalKey(x => x.UnitCode)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .HasPrincipalKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
