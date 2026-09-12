using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Customers;
using Waymark.Domain.Organisation;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="ProcessingCounter"/> to <c>processing_counters</c>.
/// </summary>
internal sealed class ProcessingCounterConfiguration : IEntityTypeConfiguration<ProcessingCounter>
{
    public void Configure(EntityTypeBuilder<ProcessingCounter> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("processing_counters", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK it
            // does not know about (decisions.md D-022).
            //
            // No CHECK on operation or purpose. They are enums validated by the
            // converter, matching processing_log — and a CHECK on either would
            // have to be amended to add a purpose, which is a rebuild (D-045).
            table.HasCheckConstraint(
                "ck_processing_counters_event_count",
                @"event_count > 0");
        });

        builder.HasKey(x => x.CounterId);

        builder.Property(x => x.CounterId)
            .HasColumnName("counter_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.Day)
            .HasColumnName("day")
            .HasConversion(WaymarkConverters.Date);
        builder.Property(x => x.Operation)
            .HasColumnName("operation")
            .HasConversion(EnumConverters.OperationConverter);
        builder.Property(x => x.Purpose)
            .HasColumnName("purpose")
            .HasConversion(EnumConverters.ProcessingPurposeConverter);
        builder.Property(x => x.EventCount)
            .HasColumnName("event_count");
        builder.Property(x => x.RolledUpAt)
            .HasColumnName("rolled_up_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        //
        // Unique on what a roll-up is keyed by, so rerunning a purge cannot
        // double-count. One caveat worth knowing: SQLite treats NULLs as
        // distinct in a UNIQUE index, so rows with no store are not protected by
        // it — the roll-up deletes a day's rows before writing them, which is
        // what actually makes it repeatable.
        builder.HasIndex(x => new { x.StoreId, x.Day, x.Operation, x.Purpose })
            .IsUnique()
            .HasDatabaseName("ux_processing_counters_day");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
