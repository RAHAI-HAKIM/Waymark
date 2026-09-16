

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Enums;
using Waymark.Domain.Ledgers;
using Waymark.Domain.Reference;
using Waymark.Domain.Organisation;
using Waymark.Domain.Customers;
using Waymark.Domain.Sales;



namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="ReceivableMovement"/> to <c>receivable_movements</c>.
/// </summary>
internal sealed class ReceivableMovementConfiguration : IEntityTypeConfiguration<ReceivableMovement>
{
    public void Configure(EntityTypeBuilder<ReceivableMovement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("receivable_movements", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_receivable_movements_movement_type",
                @"movement_type IN ('charge', 'payment', 'adjustment', 'write_off')");
            table.HasCheckConstraint(
                "ck_credit_movements_amount",
                @"amount <> 0");
        });

        builder.HasKey(x => x.MovementId);

        builder.Property(x => x.MovementId)
            .HasColumnName("movement_id");
        builder.Property(x => x.MovementType)
            .HasColumnName("movement_type")
            .HasConversion(EnumConverters.ReceivableMovementTypeConverter);
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.StaffId)
            .HasColumnName("staff_id");
        builder.Property(x => x.CustomerId)
            .HasColumnName("customer_id");
        builder.Property(x => x.Amount)
            .HasColumnName("amount");
        builder.Property(x => x.OccurredAt)
            .HasColumnName("occured_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.PaymentId)
            .HasColumnName("payment_id");
        builder.Property(x => x.ReasonCode)
            .HasColumnName("reason_code");

        builder.HasIndex(x => new { x.CustomerId, x.OccurredAt })
            .HasDatabaseName("ix_receivable_movements_customer_id_occured_at");

        builder.HasOne<ReasonCode>()
            .WithMany()
            .HasForeignKey(x => x.ReasonCode)
            .HasPrincipalKey(x => x.ReasonCodeValue)
            .OnDelete(DeleteBehavior.NoAction);
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
    }
}
