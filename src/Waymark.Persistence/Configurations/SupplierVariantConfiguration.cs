// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Purchasing;
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="SupplierVariant"/> to <c>supplier_variant</c>.
/// </summary>
internal sealed class SupplierVariantConfiguration : IEntityTypeConfiguration<SupplierVariant>
{
    public void Configure(EntityTypeBuilder<SupplierVariant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("supplier_variant", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_supplier_variant_units_per_purchase_unit",
                @"units_per_purchase_unit > 0");
            table.HasCheckConstraint(
                "ck_supplier_variant_stated_lead_time_days",
                @"stated_lead_time_days IS NULL OR stated_lead_time_days >= 0");
            table.HasCheckConstraint(
                "ck_supplier_variant_purchase_price",
                @"purchase_price >= 0");
            table.HasCheckConstraint(
                "ck_supplier_variant_is_active",
                @"is_active IN (0,1)");
        });

        builder.HasKey(x => new { x.SupplierId, x.VariantId });

        builder.Property(x => x.SupplierId)
            .HasColumnName("supplier_id");
        builder.Property(x => x.VariantId)
            .HasColumnName("variant_id");
        builder.Property(x => x.PurchaseUnitCode)
            .HasColumnName("purchase_unit_code");
        builder.Property(x => x.UnitsPerPurchaseUnit)
            .HasColumnName("units_per_purchase_unit")
            .HasDefaultValue(1000L)
            .HasSentinel(1000L);
        builder.Property(x => x.MinimumOrderQuantity)
            .HasColumnName("minimum_order_quantity")
            .HasDefaultValue(0L)
            .HasSentinel(0L);
        builder.Property(x => x.StatedLeadTimeDays)
            .HasColumnName("stated_lead_time_days");
        builder.Property(x => x.PurchasePrice)
            .HasColumnName("purchase_price");
        builder.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasDefaultValue("DZD")
            .HasSentinel("DZD");
        builder.Property(x => x.NetDays)
            .HasColumnName("net_days");
        builder.Property(x => x.LastPriceAt)
            .HasColumnName("last_price_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .HasSentinel(true);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.VariantId)
            .HasDatabaseName("ix_supplier_variant_variant");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(x => x.PurchaseUnitCode)
            .HasPrincipalKey(x => x.UnitCode)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Variant>()
            .WithMany()
            .HasForeignKey(x => x.VariantId)
            .HasPrincipalKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .HasPrincipalKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
