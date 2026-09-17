using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Customers;
using Waymark.Domain.Ledgers;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="ReceivableMovement"/> to <c>receivable_movements</c> (F-16). A new table,
/// so it cost a <c>CREATE TABLE</c> and no rebuild.
/// </summary>
internal sealed class ReceivableMovementConfiguration : IEntityTypeConfiguration<ReceivableMovement>
{
    public void Configure(EntityTypeBuilder<ReceivableMovement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("receivable_movements", table =>
        {
            // Declared in the model so a rebuild keeps them (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_receivable_movements_movement_type",
                "movement_type IN ('charge','payment','adjustment','write_off')");
            table.HasCheckConstraint(
                "ck_receivable_movements_amount",
                "amount <> 0");

            // Positive means the customer owes more. A charge mirrors its payment row's sign.
            table.HasCheckConstraint(
                "ck_receivable_movements_sign",
                "movement_type IN ('charge','adjustment') OR amount < 0");

            // A charge is exactly one on-account payment row; nothing else points at one.
            table.HasCheckConstraint(
                "ck_receivable_movements_payment_id",
                "(movement_type = 'charge') = (payment_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_receivable_movements_cash_movement_id",
                "cash_movement_id IS NULL OR movement_type = 'payment'");
            table.HasCheckConstraint(
                "ck_receivable_movements_reason_code",
                "movement_type NOT IN ('adjustment','write_off') OR reason_code IS NOT NULL");
        });

        builder.HasKey(x => x.MovementId);

        builder.Property(x => x.MovementId)
            .HasColumnName("movement_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.CustomerId)
            .HasColumnName("customer_id");
        builder.Property(x => x.MovementType)
            .HasColumnName("movement_type")
            .HasConversion(EnumConverters.ReceivableMovementTypeConverter);
        builder.Property(x => x.Amount)
            .HasColumnName("amount");
        builder.Property(x => x.OccurredAt)
            .HasColumnName("occurred_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.PaymentId)
            .HasColumnName("payment_id");
        builder.Property(x => x.CashMovementId)
            .HasColumnName("cash_movement_id");
        builder.Property(x => x.ReasonCode)
            .HasColumnName("reason_code");
        builder.Property(x => x.StaffId)
            .HasColumnName("staff_id");

        // The balance is a sum over this index, never a cached column.
        builder.HasIndex(x => new { x.CustomerId, x.OccurredAt })
            .HasDatabaseName("ix_receivable_customer");

        // One charge per on-account payment row, and one payment per cash movement.
        builder.HasIndex(x => x.PaymentId)
            .IsUnique()
            .HasFilter("payment_id IS NOT NULL")
            .HasDatabaseName("ux_receivable_payment");
        builder.HasIndex(x => x.CashMovementId)
            .IsUnique()
            .HasFilter("cash_movement_id IS NOT NULL")
            .HasDatabaseName("ux_receivable_cash_movement");

        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .HasPrincipalKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<TransactionPayment>()
            .WithMany()
            .HasForeignKey(x => x.PaymentId)
            .HasPrincipalKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CashMovement>()
            .WithMany()
            .HasForeignKey(x => x.CashMovementId)
            .HasPrincipalKey(x => x.MovementId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ReasonCode>()
            .WithMany()
            .HasForeignKey(x => x.ReasonCode)
            .HasPrincipalKey(x => x.ReasonCodeValue)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.StaffId)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
