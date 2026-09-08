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
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="CashMovement"/> to <c>cash_movements</c>.
/// </summary>
internal sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("cash_movements", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_cash_movements_movement_type",
                @"movement_type IN ('paid_in','paid_out','drop','float_add','float_remove')");
            table.HasCheckConstraint(
                "ck_cash_movements_amount",
                @"amount > 0");
        });

        builder.HasKey(x => x.MovementId);

        builder.Property(x => x.MovementId)
            .HasColumnName("movement_id");
        builder.Property(x => x.SessionId)
            .HasColumnName("session_id");
        builder.Property(x => x.MovementType)
            .HasColumnName("movement_type")
            .HasConversion(EnumConverters.CashMovementTypeConverter);
        builder.Property(x => x.Amount)
            .HasColumnName("amount");
        builder.Property(x => x.ReasonCode)
            .HasColumnName("reason_code");
        builder.Property(x => x.Note)
            .HasColumnName("note");
        builder.Property(x => x.StaffId)
            .HasColumnName("staff_id");
        builder.Property(x => x.AuthorisedBy)
            .HasColumnName("authorised_by");
        builder.Property(x => x.OccurredAt)
            .HasColumnName("occurred_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.SessionId)
            .HasDatabaseName("ix_cash_movements_session");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.AuthorisedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
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
        builder.HasOne<CashSession>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .HasPrincipalKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
