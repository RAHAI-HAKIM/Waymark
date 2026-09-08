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
/// Maps <see cref="CashSession"/> to <c>cash_sessions</c>.
/// </summary>
internal sealed class CashSessionConfiguration : IEntityTypeConfiguration<CashSession>
{
    public void Configure(EntityTypeBuilder<CashSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("cash_sessions", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_cash_sessions_status",
                @"status IN ('open','closed','suspended')");
            table.HasCheckConstraint(
                "ck_cash_sessions_closed_at",
                @"closed_at IS NULL OR closed_at >= opened_at");
            table.HasCheckConstraint(
                "ck_cash_sessions_status_2",
                @"status <> 'closed' OR counted_cash IS NOT NULL");
        });

        builder.HasKey(x => x.SessionId);

        builder.Property(x => x.SessionId)
            .HasColumnName("session_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.TerminalId)
            .HasColumnName("terminal_id");
        builder.Property(x => x.OpenedBy)
            .HasColumnName("opened_by");
        builder.Property(x => x.OpenedAt)
            .HasColumnName("opened_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.OpeningFloat)
            .HasColumnName("opening_float")
            .HasDefaultValue(0L);
        builder.Property(x => x.ClosedBy)
            .HasColumnName("closed_by");
        builder.Property(x => x.ClosedAt)
            .HasColumnName("closed_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.CountedCash)
            .HasColumnName("counted_cash");
        builder.Property(x => x.ExpectedCash)
            .HasColumnName("expected_cash");
        builder.Property(x => x.Variance)
            .HasColumnName("variance");
        builder.Property(x => x.ZReportNumber)
            .HasColumnName("z_report_number");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.CashSessionStatusConverter)
            .HasDefaultValue(CashSessionStatus.Open);
        builder.Property(x => x.Notes)
            .HasColumnName("notes");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.StoreId)
            .HasDatabaseName("ix_cash_sessions_open")
            .HasFilter(@"status = 'open'");
        builder.HasIndex(x => new { x.TerminalId, x.OpenedAt })
            .HasDatabaseName("ix_cash_sessions_terminal");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.ClosedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.OpenedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Terminal>()
            .WithMany()
            .HasForeignKey(x => x.TerminalId)
            .HasPrincipalKey(x => x.TerminalId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
