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
/// Maps <see cref="Terminal"/> to <c>terminals</c>.
/// </summary>
internal sealed class TerminalConfiguration : IEntityTypeConfiguration<Terminal>
{
    public void Configure(EntityTypeBuilder<Terminal> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("terminals", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_terminals_is_replica",
                @"is_replica IN (0,1)");
            table.HasCheckConstraint(
                "ck_terminals_status",
                @"status IN ('active','inactive','retired')");
        });

        builder.HasKey(x => x.TerminalId);

        builder.Property(x => x.TerminalId)
            .HasColumnName("terminal_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.TerminalName)
            .HasColumnName("terminal_name");
        builder.Property(x => x.IsReplica)
            .HasColumnName("is_replica")
            .HasDefaultValue(false);
        builder.Property(x => x.PrinterPort)
            .HasColumnName("printer_port");
        builder.Property(x => x.HardwareNotes)
            .HasColumnName("hardware_notes");
        builder.Property(x => x.LastSeenAt)
            .HasColumnName("last_seen_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.TerminalStatusConverter)
            .HasDefaultValue(TerminalStatus.Active);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.StoreId)
            .HasDatabaseName("ix_terminals_store");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
