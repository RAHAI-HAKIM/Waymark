// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Values;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Enums;
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
            // An override is recorded whole or not at all (D-092).
            table.HasCheckConstraint(
                "ck_transaction_items_override",
                @"(list_price IS NULL AND override_reason_code IS NULL AND override_authorised_by IS NULL) OR (list_price IS NOT NULL AND override_reason_code IS NOT NULL AND override_authorised_by IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_transaction_items_list_price",
                @"list_price IS NULL OR list_price >= 0");
            table.HasCheckConstraint(
                "ck_transaction_items_quantity_source",
                @"quantity_source IN ('count','typed_weight','label_weight','label_price')");

            // B8: a line struck before the sale says who struck it and when, and was never taken
            // from a batch: it moved no stock and charged nothing.
            table.HasCheckConstraint(
                "ck_transaction_items_removed",
                @"(removed_at IS NULL) = (removed_by IS NULL) AND (removed_at IS NULL OR batch_id IS NULL)");

            // D-106: only a struck line names who let it be struck.
            table.HasCheckConstraint(
                "ck_transaction_items_removed_authorised",
                @"removed_authorised_by IS NULL OR removed_at IS NOT NULL");

            // D-104: a place on the ticket from 1; 0 only on the rows written before the column.
            table.HasCheckConstraint(
                "ck_transaction_items_line_number",
                @"line_number >= 0");
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
            .HasDefaultValue(WaymarkConverters.ZeroMoney);
        builder.Property(x => x.DiscountReasonCode)
            .HasColumnName("discount_reason_code");
        builder.Property(x => x.AuthorisedBy)
            .HasColumnName("authorised_by");
        builder.Property(x => x.RemovedAt)
            .HasColumnName("removed_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.RemovedBy)
            .HasColumnName("removed_by");
        builder.Property(x => x.RemovedAuthorisedBy)
            .HasColumnName("removed_authorised_by");

        // The default is for the rows written before the column; the entity's property is
        // required, so no writer leans on it (D-104).
        builder.Property(x => x.LineNumber)
            .HasColumnName("line_number")
            .HasDefaultValue(0)
            .HasSentinel(0);
        builder.Property(x => x.TaxAmount)
            .HasColumnName("tax_amount")
            .HasDefaultValue(WaymarkConverters.ZeroMoney);
        builder.Property(x => x.LineTotal)
            .HasColumnName("line_total");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // The default is for the rows written before B3, every one a count; the entity's
        // property is required, so no writer leans on it (D-090).
        builder.Property(x => x.ListPrice)
            .HasColumnName("list_price");
        builder.Property(x => x.OverrideReasonCode)
            .HasColumnName("override_reason_code");
        builder.Property(x => x.OverrideAuthorisedBy)
            .HasColumnName("override_authorised_by");
        builder.Property(x => x.DiscountNote)
            .HasColumnName("discount_note");

        builder.Property(x => x.QuantitySource)
            .HasColumnName("quantity_source")
            .HasConversion(EnumConverters.QuantitySourceConverter)
            .HasDefaultValue(QuantitySource.Count)
            .HasSentinel(QuantitySource.Count);

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
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.RemovedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.RemovedAuthorisedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.OverrideAuthorisedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ReasonCode>()
            .WithMany()
            .HasForeignKey(x => x.OverrideReasonCode)
            .HasPrincipalKey(x => x.ReasonCodeValue)
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
