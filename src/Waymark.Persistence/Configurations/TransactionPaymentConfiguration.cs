// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Enums;
using Waymark.Domain.Sales;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="TransactionPayment"/> to <c>transaction_payments</c>.
/// </summary>
internal sealed class TransactionPaymentConfiguration : IEntityTypeConfiguration<TransactionPayment>
{
    public void Configure(EntityTypeBuilder<TransactionPayment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("transaction_payments", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_transaction_payments_payment_method",
                @"payment_method IN ('cash','card','mobile_wallet','store_credit','on_account')");
            table.HasCheckConstraint(
                "ck_transaction_payments_amount",
                @"amount <> 0");
        });

        builder.HasKey(x => x.PaymentId);

        builder.Property(x => x.PaymentId)
            .HasColumnName("payment_id");
        builder.Property(x => x.TransactionId)
            .HasColumnName("transaction_id");
        builder.Property(x => x.Sequence)
            .HasColumnName("sequence");
        builder.Property(x => x.PaymentMethod)
            .HasColumnName("payment_method")
            .HasConversion(EnumConverters.PaymentMethodConverter);
        builder.Property(x => x.Amount)
            .HasColumnName("amount");
        builder.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasDefaultValue("DZD");
        builder.Property(x => x.Reference)
            .HasColumnName("reference");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => new { x.TransactionId, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.PaymentMethod, x.CreatedAt })
            .HasDatabaseName("ix_payments_method");
        builder.HasIndex(x => x.TransactionId)
            .HasDatabaseName("ix_payments_transaction");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(x => x.TransactionId)
            .HasPrincipalKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
