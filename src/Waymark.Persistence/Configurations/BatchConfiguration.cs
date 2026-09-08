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
using Waymark.Domain.Inventory;
using Waymark.Domain.Organisation;
using Waymark.Domain.Purchasing;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Batch"/> to <c>batches</c>.
/// </summary>
internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("batches", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_batches_status",
                @"status IN ('active','depleted','written_off','quarantined')");
            table.HasCheckConstraint(
                "ck_batches_expiration_date",
                @"expiration_date IS NULL OR expiration_date >= received_date");
        });

        builder.HasKey(x => x.BatchId);

        builder.Property(x => x.BatchId)
            .HasColumnName("batch_id");
        builder.Property(x => x.LotNumber)
            .HasColumnName("lot_number");
        builder.Property(x => x.SupplierDocumentRef)
            .HasColumnName("supplier_document_ref");
        builder.Property(x => x.ProductId)
            .HasColumnName("product_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.SupplierId)
            .HasColumnName("supplier_id");
        builder.Property(x => x.OrderId)
            .HasColumnName("order_id");
        builder.Property(x => x.ReceivedDate)
            .HasColumnName("received_date")
            .HasConversion(WaymarkConverters.Date);
        builder.Property(x => x.ExpirationDate)
            .HasColumnName("expiration_date")
            .HasConversion(WaymarkConverters.Date);
        builder.Property(x => x.ReceivedBy)
            .HasColumnName("received_by");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.BatchStatusConverter)
            .HasDefaultValue(BatchStatus.Active);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.OrderId)
            .HasDatabaseName("ix_batches_order");
        builder.HasIndex(x => x.ProductId)
            .HasDatabaseName("ix_batches_product");
        builder.HasIndex(x => new { x.StoreId, x.ExpirationDate })
            .HasDatabaseName("ix_batches_expiry");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.ReceivedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<PurchaseOrder>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .HasPrincipalKey(x => x.OrderId)
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
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .HasPrincipalKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
