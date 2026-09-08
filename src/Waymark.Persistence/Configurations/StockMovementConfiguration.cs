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
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="StockMovement"/> to <c>stock_movements</c>.
/// </summary>
internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_movements", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_stock_movements_movement_type",
                @"movement_type IN ('receipt','sale','return_in','return_out', 'adjustment','count','write_off','expiry','transfer')");
            table.HasCheckConstraint(
                "ck_stock_movements_reference_type",
                @"reference_type IS NULL OR reference_type IN ('transaction','purchase_order','stock_count','return','manual')");
            table.HasCheckConstraint(
                "ck_stock_movements_quantity_changed",
                @"quantity_changed <> 0");
        });

        builder.HasKey(x => x.MovementId);

        builder.Property(x => x.MovementId)
            .HasColumnName("movement_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.VariantId)
            .HasColumnName("variant_id");
        builder.Property(x => x.BatchId)
            .HasColumnName("batch_id");
        builder.Property(x => x.MovementDate)
            .HasColumnName("movement_date")
            .HasConversion(WaymarkConverters.Date);
        builder.Property(x => x.MovementType)
            .HasColumnName("movement_type")
            .HasConversion(EnumConverters.StockMovementTypeConverter);
        builder.Property(x => x.QuantityChanged)
            .HasColumnName("quantity_changed");
        builder.Property(x => x.UnitCode)
            .HasColumnName("unit_code");
        builder.Property(x => x.UnitCost)
            .HasColumnName("unit_cost");
        builder.Property(x => x.ReferenceType)
            .HasColumnName("reference_type");
        builder.Property(x => x.ReferenceId)
            .HasColumnName("reference_id");
        builder.Property(x => x.ReasonCode)
            .HasColumnName("reason_code");
        builder.Property(x => x.StaffId)
            .HasColumnName("staff_id");
        builder.Property(x => x.Note)
            .HasColumnName("note");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => new { x.StoreId, x.MovementDate })
            .HasDatabaseName("ix_movements_store_date");
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId })
            .HasDatabaseName("ix_movements_reference");
        builder.HasIndex(x => x.BatchId)
            .HasDatabaseName("ix_movements_batch");
        builder.HasIndex(x => new { x.VariantId, x.MovementDate })
            .HasDatabaseName("ix_movements_variant_date");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.StaffId)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ReasonCode>()
            .WithMany()
            .HasForeignKey(x => x.ReasonCode)
            .HasPrincipalKey(x => x.ReasonCodeValue)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(x => x.UnitCode)
            .HasPrincipalKey(x => x.UnitCode)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(x => x.BatchId)
            .HasPrincipalKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Variant>()
            .WithMany()
            .HasForeignKey(x => x.VariantId)
            .HasPrincipalKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
