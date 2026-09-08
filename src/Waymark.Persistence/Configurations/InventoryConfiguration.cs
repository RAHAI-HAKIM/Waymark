// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Inventory;
using Waymark.Domain.Organisation;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Inventory"/> to <c>inventories</c>.
/// </summary>
internal sealed class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("inventories", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_inventories_reserved_quantity",
                @"reserved_quantity >= 0");
        });

        builder.HasKey(x => new { x.StoreId, x.VariantId, x.BatchId });

        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.VariantId)
            .HasColumnName("variant_id");
        builder.Property(x => x.BatchId)
            .HasColumnName("batch_id");
        builder.Property(x => x.Quantity)
            .HasColumnName("quantity")
            .HasDefaultValue(0L);
        builder.Property(x => x.ReservedQuantity)
            .HasColumnName("reserved_quantity")
            .HasDefaultValue(0L);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.BatchId)
            .HasDatabaseName("ix_inventories_batch");
        builder.HasIndex(x => new { x.VariantId, x.StoreId })
            .HasDatabaseName("ix_inventories_variant");

        // Foreign keys are dropped by a rebuild too, for the same reason.
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
