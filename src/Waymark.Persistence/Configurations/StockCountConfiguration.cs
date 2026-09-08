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

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="StockCount"/> to <c>stock_counts</c>.
/// </summary>
internal sealed class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_counts", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_stock_counts_count_type",
                @"count_type IN ('full','cycle','spot')");
            table.HasCheckConstraint(
                "ck_stock_counts_status",
                @"status IN ('draft','counting','review','posted','cancelled')");
            table.HasCheckConstraint(
                "ck_stock_counts_status_2",
                @"status <> 'posted' OR approved_by IS NOT NULL");
        });

        builder.HasKey(x => x.CountId);

        builder.Property(x => x.CountId)
            .HasColumnName("count_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.CountType)
            .HasColumnName("count_type")
            .HasConversion(EnumConverters.CountTypeConverter);
        builder.Property(x => x.ScopeCategoryId)
            .HasColumnName("scope_category_id");
        builder.Property(x => x.StartedBy)
            .HasColumnName("started_by");
        builder.Property(x => x.StartedAt)
            .HasColumnName("started_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.CompletedAt)
            .HasColumnName("completed_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.ApprovedBy)
            .HasColumnName("approved_by");
        builder.Property(x => x.ApprovedAt)
            .HasColumnName("approved_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.TotalVarianceValue)
            .HasColumnName("total_variance_value");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.StockCountStatusConverter)
            .HasDefaultValue(StockCountStatus.Draft);
        builder.Property(x => x.Notes)
            .HasColumnName("notes");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => new { x.StoreId, x.Status })
            .HasDatabaseName("ix_stock_counts_store");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.ApprovedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.StartedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(x => x.ScopeCategoryId)
            .HasPrincipalKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
