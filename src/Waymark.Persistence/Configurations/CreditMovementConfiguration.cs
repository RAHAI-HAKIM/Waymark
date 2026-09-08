// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Customers;
using Waymark.Domain.Enums;
using Waymark.Domain.Ledgers;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="CreditMovement"/> to <c>credit_movements</c>.
/// </summary>
internal sealed class CreditMovementConfiguration : IEntityTypeConfiguration<CreditMovement>
{
    public void Configure(EntityTypeBuilder<CreditMovement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("credit_movements", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_credit_movements_movement_type",
                @"movement_type IN ('issue','redeem','adjust','expire','reverse')");
            table.HasCheckConstraint(
                "ck_credit_movements_amount",
                @"amount <> 0");
        });

        builder.HasKey(x => x.MovementId);

        builder.Property(x => x.MovementId)
            .HasColumnName("movement_id");
        builder.Property(x => x.CustomerId)
            .HasColumnName("customer_id");
        builder.Property(x => x.MovementType)
            .HasColumnName("movement_type")
            .HasConversion(EnumConverters.CreditMovementTypeConverter);
        builder.Property(x => x.Amount)
            .HasColumnName("amount");
        builder.Property(x => x.BalanceAfter)
            .HasColumnName("balance_after");
        builder.Property(x => x.TransactionId)
            .HasColumnName("transaction_id");
        builder.Property(x => x.ReturnId)
            .HasColumnName("return_id");
        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.StaffId)
            .HasColumnName("staff_id");
        builder.Property(x => x.TerminalId)
            .HasColumnName("terminal_id");
        builder.Property(x => x.ReasonCode)
            .HasColumnName("reason_code");
        builder.Property(x => x.Note)
            .HasColumnName("note");
        builder.Property(x => x.OccurredAt)
            .HasColumnName("occurred_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => new { x.CustomerId, x.OccurredAt })
            .HasDatabaseName("ix_credit_customer");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<ReasonCode>()
            .WithMany()
            .HasForeignKey(x => x.ReasonCode)
            .HasPrincipalKey(x => x.ReasonCodeValue)
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
        builder.HasOne<SalesReturn>()
            .WithMany()
            .HasForeignKey(x => x.ReturnId)
            .HasPrincipalKey(x => x.ReturnId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(x => x.TransactionId)
            .HasPrincipalKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .HasPrincipalKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
