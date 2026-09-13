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
using Waymark.Domain.Values;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Transaction"/> to <c>transactions</c>.
/// </summary>
internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("transactions", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_transactions_ecommerce_flag",
                @"ecommerce_flag IN (0,1)");
            table.HasCheckConstraint(
                "ck_transactions_status",
                @"status IN ('open','parked','completed','voided','refunded','partially_refunded')");
            table.HasCheckConstraint(
                "ck_transactions_status_2",
                @"status <> 'voided' OR (voided_at IS NOT NULL AND void_reason_code IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_transactions_status_3",
                @"status <> 'completed' OR invoice_number IS NOT NULL");
            table.HasCheckConstraint(
                "ck_store_rounding_policy",
                @"rounding_policy IN ('half_even','half_up')");
        });

        builder.HasKey(x => x.TransactionId);

        builder.Property(x => x.TransactionId)
            .HasColumnName("transaction_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.TerminalId)
            .HasColumnName("terminal_id");
        builder.Property(x => x.CashSessionId)
            .HasColumnName("cash_session_id");
        builder.Property(x => x.StaffId)
            .HasColumnName("staff_id");
        builder.Property(x => x.CustomerId)
            .HasColumnName("customer_id");
        builder.Property(x => x.InvoiceNumber)
            .HasColumnName("invoice_number");
        builder.Property(x => x.OccurredAt)
            .HasColumnName("occurred_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.Subtotal)
            .HasColumnName("subtotal")
            .HasDefaultValue(WaymarkConverters.ZeroMoney);
        builder.Property(x => x.DiscountTotal)
            .HasColumnName("discount_total")
            .HasDefaultValue(WaymarkConverters.ZeroMoney);
        builder.Property(x => x.TaxTotal)
            .HasColumnName("tax_total")
            .HasDefaultValue(WaymarkConverters.ZeroMoney);
        builder.Property(x => x.TotalAmount)
            .HasColumnName("total_amount")
            .HasDefaultValue(WaymarkConverters.ZeroMoney);
        builder.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasDefaultValue("DZD")
            .HasSentinel("DZD");
        builder.Property(x => x.RoundingPolicy)
            .HasColumnName("rounding_policy")
            .HasConversion(EnumConverters.RoundingPoliciesConverter)
            .HasDefaultValue(RoundingPolicies.HalfUp)
            .HasSentinel(RoundingPolicies.HalfUp);
        builder.Property(x => x.EcommerceFlag)
            .HasColumnName("ecommerce_flag")
            .HasDefaultValue(false)
            .HasSentinel(false);
        builder.Property(x => x.OriginalTransactionId)
            .HasColumnName("original_transaction_id");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.TransactionStatusConverter)
            .HasDefaultValue(TransactionStatus.Open)
            .HasSentinel(TransactionStatus.Open);
        builder.Property(x => x.VoidedAt)
            .HasColumnName("voided_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.VoidedBy)
            .HasColumnName("voided_by");
        builder.Property(x => x.VoidReasonCode)
            .HasColumnName("void_reason_code");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.OriginalTransactionId)
            .HasDatabaseName("ix_transactions_original");
        builder.HasIndex(x => new { x.StaffId, x.OccurredAt })
            .HasDatabaseName("ix_transactions_staff");
        builder.HasIndex(x => x.CashSessionId)
            .HasDatabaseName("ix_transactions_session");
        builder.HasIndex(x => x.CustomerId)
            .HasDatabaseName("ix_transactions_customer")
            .HasFilter(@"customer_id IS NOT NULL");
        builder.HasIndex(x => new { x.StoreId, x.OccurredAt })
            .HasDatabaseName("ix_transactions_store_date");
        builder.HasIndex(x => new { x.StoreId, x.InvoiceNumber })
            .HasDatabaseName("ux_transactions_invoice")
            .IsUnique()
            .HasFilter(@"invoice_number IS NOT NULL");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<ReasonCode>()
            .WithMany()
            .HasForeignKey(x => x.VoidReasonCode)
            .HasPrincipalKey(x => x.ReasonCodeValue)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.VoidedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(x => x.OriginalTransactionId)
            .HasPrincipalKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .HasPrincipalKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.StaffId)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<CashSession>()
            .WithMany()
            .HasForeignKey(x => x.CashSessionId)
            .HasPrincipalKey(x => x.SessionId)
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
