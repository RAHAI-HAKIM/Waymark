// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Shift"/> to <c>shifts</c>.
/// </summary>
internal sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("shifts", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_shifts_status",
                @"status IN ('open','closed')");
            table.HasCheckConstraint(
                "ck_shifts_end_time",
                @"end_time IS NULL OR end_time >= start_time");
        });

        builder.HasKey(x => x.ShiftId);

        builder.Property(x => x.ShiftId)
            .HasColumnName("shift_id");
        builder.Property(x => x.StaffId)
            .HasColumnName("staff_id");
        builder.Property(x => x.TerminalId)
            .HasColumnName("terminal_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.StartTime)
            .HasColumnName("start_time");
        builder.Property(x => x.EndTime)
            .HasColumnName("end_time");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.ShiftStatusConverter)
            .HasDefaultValue(ShiftStatus.Open);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.StoreId)
            .HasDatabaseName("ix_shifts_store_open");
        builder.HasIndex(x => new { x.StaffId, x.StartTime })
            .HasDatabaseName("ix_shifts_staff");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Terminal>()
            .WithMany()
            .HasForeignKey(x => x.TerminalId)
            .HasPrincipalKey(x => x.TerminalId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.StaffId)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
