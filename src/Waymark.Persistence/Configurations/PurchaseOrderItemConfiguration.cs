// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Values;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Purchasing;
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="PurchaseOrderItem"/> to <c>purchase_order_items</c>.
/// </summary>
internal sealed class PurchaseOrderItemConfiguration : IEntityTypeConfiguration<PurchaseOrderItem>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_order_items", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_purchase_order_items_quantity_ordered",
                @"quantity_ordered > 0");
            table.HasCheckConstraint(
                "ck_purchase_order_items_quantity_received",
                @"quantity_received >= 0");
            table.HasCheckConstraint(
                "ck_purchase_order_items_unit_cost",
                @"unit_cost >= 0");
            table.HasCheckConstraint(
                "ck_purchase_order_items_discount",
                @"discount >= 0");
            table.HasCheckConstraint(
                "ck_purchase_order_items_line_total",
                @"line_total >= 0");
        });

        builder.HasKey(x => new { x.OrderId, x.VariantId });

        builder.Property(x => x.OrderId)
            .HasColumnName("order_id");
        builder.Property(x => x.VariantId)
            .HasColumnName("variant_id");
        builder.Property(x => x.QuantityOrdered)
            .HasColumnName("quantity_ordered");
        builder.Property(x => x.QuantityReceived)
            .HasColumnName("quantity_received")
            .HasDefaultValue(0L)
            .HasSentinel(0L);
        builder.Property(x => x.PurchaseUnitCode)
            .HasColumnName("purchase_unit_code");
        builder.Property(x => x.UnitsPerPurchaseUnit)
            .HasColumnName("units_per_purchase_unit")
            .HasDefaultValue(1000L)
            .HasSentinel(1000L);
        builder.Property(x => x.UnitCost)
            .HasColumnName("unit_cost");
        builder.Property(x => x.Discount)
            .HasColumnName("discount")
            .HasDefaultValue(WaymarkConverters.ZeroMoney);
        builder.Property(x => x.LineTotal)
            .HasColumnName("line_total");

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.VariantId)
            .HasDatabaseName("ix_po_items_variant");

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
        builder.HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .HasPrincipalKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
