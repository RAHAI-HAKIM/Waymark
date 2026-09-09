// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Inventory;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="TransactionItem"/> to <c>transaction_items</c>.
/// </summary>
internal sealed class TransactionItemConfiguration : IEntityTypeConfiguration<TransactionItem>
{
    public void Configure(EntityTypeBuilder<TransactionItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("transaction_items", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_transaction_items_quantity",
                @"quantity <> 0");
            table.HasCheckConstraint(
                "ck_transaction_items_sell_price",
                @"sell_price >= 0");
            table.HasCheckConstraint(
                "ck_transaction_items_discount_amount",
                @"discount_amount >= 0");
            table.HasCheckConstraint(
                "ck_transaction_items_discount_amount_2",
                @"discount_amount = 0 OR discount_reason_code IS NOT NULL");
        });

        builder.HasKey(x => x.TransactionItemId);

        builder.Property(x => x.TransactionItemId)
            .HasColumnName("transaction_item_id");
        builder.Property(x => x.TransactionId)
            .HasColumnName("transaction_id");
        builder.Property(x => x.VariantId)
            .HasColumnName("variant_id");
        builder.Property(x => x.BatchId)
            .HasColumnName("batch_id");
        builder.Property(x => x.PromotionId)
            .HasColumnName("promotion_id");
        builder.Property(x => x.Quantity)
            .HasColumnName("quantity");
        builder.Property(x => x.UnitCode)
            .HasColumnName("unit_code");
        builder.Property(x => x.SellPrice)
            .HasColumnName("sell_price");
        builder.Property(x => x.UnitCostAtSale)
            .HasColumnName("unit_cost_at_sale");
        builder.Property(x => x.DiscountAmount)
            .HasColumnName("discount_amount")
            .HasDefaultValue(0L)
            .HasSentinel(0L);
        builder.Property(x => x.DiscountReasonCode)
            .HasColumnName("discount_reason_code");
        builder.Property(x => x.AuthorisedBy)
            .HasColumnName("authorised_by");
        builder.Property(x => x.TaxAmount)
            .HasColumnName("tax_amount")
            .HasDefaultValue(0L)
            .HasSentinel(0L);
        builder.Property(x => x.LineTotal)
            .HasColumnName("line_total");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.BatchId)
            .HasDatabaseName("ix_txn_items_batch");
        builder.HasIndex(x => new { x.VariantId, x.CreatedAt })
            .HasDatabaseName("ix_txn_items_variant");
        builder.HasIndex(x => x.TransactionId)
            .HasDatabaseName("ix_txn_items_transaction");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.AuthorisedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ReasonCode>()
            .WithMany()
            .HasForeignKey(x => x.DiscountReasonCode)
            .HasPrincipalKey(x => x.ReasonCodeValue)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(x => x.UnitCode)
            .HasPrincipalKey(x => x.UnitCode)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Promotion>()
            .WithMany()
            .HasForeignKey(x => x.PromotionId)
            .HasPrincipalKey(x => x.PromotionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(x => x.BatchId)
            .HasPrincipalKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Variant>()
            .WithMany()
            .HasForeignKey(x => x.VariantId)
            .HasPrincipalKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(x => x.TransactionId)
            .HasPrincipalKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
