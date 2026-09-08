// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Enums;
using Waymark.Domain.Inventory;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="SalesReturn"/> to <c>returns</c>.
/// </summary>
internal sealed class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    public void Configure(EntityTypeBuilder<SalesReturn> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("returns", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_returns_quantity_returned",
                @"quantity_returned > 0");
            table.HasCheckConstraint(
                "ck_returns_refund_amount",
                @"refund_amount >= 0");
            table.HasCheckConstraint(
                "ck_returns_refund_method",
                @"refund_method IN ('cash','card','store_credit','exchange')");
            table.HasCheckConstraint(
                "ck_returns_restock_flag",
                @"restock_flag IN (0,1)");
        });

        builder.HasKey(x => x.ReturnId);

        builder.Property(x => x.ReturnId)
            .HasColumnName("return_id");
        builder.Property(x => x.TransactionItemId)
            .HasColumnName("transaction_item_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.TerminalId)
            .HasColumnName("terminal_id");
        builder.Property(x => x.BatchId)
            .HasColumnName("batch_id");
        builder.Property(x => x.StaffId)
            .HasColumnName("staff_id");
        builder.Property(x => x.ApprovedBy)
            .HasColumnName("approved_by");
        builder.Property(x => x.QuantityReturned)
            .HasColumnName("quantity_returned");
        builder.Property(x => x.RefundAmount)
            .HasColumnName("refund_amount");
        builder.Property(x => x.RefundMethod)
            .HasColumnName("refund_method")
            .HasConversion(EnumConverters.RefundMethodConverter);
        builder.Property(x => x.RestockFlag)
            .HasColumnName("restock_flag");
        builder.Property(x => x.ReasonCode)
            .HasColumnName("reason_code");
        builder.Property(x => x.Note)
            .HasColumnName("note");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => new { x.StoreId, x.CreatedAt })
            .HasDatabaseName("ix_returns_store_date");
        builder.HasIndex(x => x.TransactionItemId)
            .HasDatabaseName("ix_returns_item");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<ReasonCode>()
            .WithMany()
            .HasForeignKey(x => x.ReasonCode)
            .HasPrincipalKey(x => x.ReasonCodeValue)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.ApprovedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.StaffId)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(x => x.BatchId)
            .HasPrincipalKey(x => x.BatchId)
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
        builder.HasOne<TransactionItem>()
            .WithMany()
            .HasForeignKey(x => x.TransactionItemId)
            .HasPrincipalKey(x => x.TransactionItemId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
