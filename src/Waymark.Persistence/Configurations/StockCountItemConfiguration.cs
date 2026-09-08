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
/// Maps <see cref="StockCountItem"/> to <c>stock_count_items</c>.
/// </summary>
internal sealed class StockCountItemConfiguration : IEntityTypeConfiguration<StockCountItem>
{
    public void Configure(EntityTypeBuilder<StockCountItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_count_items", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_stock_count_items_recount_flag",
                @"recount_flag IN (0,1)");
        });

        builder.HasKey(x => x.CountItemId);

        builder.Property(x => x.CountItemId)
            .HasColumnName("count_item_id");
        builder.Property(x => x.CountId)
            .HasColumnName("count_id");
        builder.Property(x => x.VariantId)
            .HasColumnName("variant_id");
        builder.Property(x => x.BatchId)
            .HasColumnName("batch_id");
        builder.Property(x => x.ExpectedQuantity)
            .HasColumnName("expected_quantity");
        builder.Property(x => x.CountedQuantity)
            .HasColumnName("counted_quantity");
        builder.Property(x => x.VarianceQuantity)
            .HasColumnName("variance_quantity");
        builder.Property(x => x.UnitCost)
            .HasColumnName("unit_cost");
        builder.Property(x => x.VarianceValue)
            .HasColumnName("variance_value");
        builder.Property(x => x.CountedBy)
            .HasColumnName("counted_by");
        builder.Property(x => x.CountedAt)
            .HasColumnName("counted_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.RecountFlag)
            .HasColumnName("recount_flag")
            .HasDefaultValue(false);
        builder.Property(x => x.Note)
            .HasColumnName("note");

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.BatchId).IsUnique();
        builder.HasIndex(x => x.CountId).IsUnique();
        builder.HasIndex(x => x.VariantId).IsUnique();
        builder.HasIndex(x => x.CountId)
            .HasDatabaseName("ix_count_items_count");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.CountedBy)
            .HasPrincipalKey(x => x.StaffId)
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
        builder.HasOne<StockCount>()
            .WithMany()
            .HasForeignKey(x => x.CountId)
            .HasPrincipalKey(x => x.CountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
