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
using Waymark.Domain.Organisation;
using Waymark.Domain.Purchasing;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="PurchaseOrder"/> to <c>purchase_orders</c>.
/// </summary>
internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_orders", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_purchase_orders_source",
                @"source IN ('manual','recommendation','reorder_rule')");
            table.HasCheckConstraint(
                "ck_purchase_orders_status",
                @"status IN ('draft','sent','partially_received','received','cancelled')");
        });

        builder.HasKey(x => x.OrderId);

        builder.Property(x => x.OrderId)
            .HasColumnName("order_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.SupplierId)
            .HasColumnName("supplier_id");
        builder.Property(x => x.OrderDate)
            .HasColumnName("order_date")
            .HasConversion(WaymarkConverters.Date);
        builder.Property(x => x.ExpectedArrivalDate)
            .HasColumnName("expected_arrival_date")
            .HasConversion(WaymarkConverters.Date);
        builder.Property(x => x.ReceivedAt)
            .HasColumnName("received_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.TotalAmount)
            .HasColumnName("total_amount");
        builder.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasDefaultValue("DZD");
        builder.Property(x => x.Source)
            .HasColumnName("source")
            .HasConversion(EnumConverters.PurchaseOrderSourceConverter)
            .HasDefaultValue(PurchaseOrderSource.Manual);
        builder.Property(x => x.SourceRecommendationId)
            .HasColumnName("source_recommendation_id");
        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.PurchaseOrderStatusConverter)
            .HasDefaultValue(PurchaseOrderStatus.Draft);
        builder.Property(x => x.Notes)
            .HasColumnName("notes");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.SupplierId)
            .HasDatabaseName("ix_po_supplier");
        builder.HasIndex(x => new { x.StoreId, x.Status })
            .HasDatabaseName("ix_po_store_status");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Recommendation>()
            .WithMany()
            .HasForeignKey(x => x.SourceRecommendationId)
            .HasPrincipalKey(x => x.RecommendationId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .HasPrincipalKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
