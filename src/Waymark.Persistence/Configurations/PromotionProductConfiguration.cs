// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Enums;
using Waymark.Domain.Pricing;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="PromotionProduct"/> to <c>promotion_product</c>.
/// </summary>
internal sealed class PromotionProductConfiguration : IEntityTypeConfiguration<PromotionProduct>
{
    public void Configure(EntityTypeBuilder<PromotionProduct> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("promotion_product", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_promotion_product_value_type",
                @"value_type IN ('percent','amount','bogo')");
            table.HasCheckConstraint(
                "ck_promotion_product_promotion_value",
                @"promotion_value >= 0");
            table.HasCheckConstraint(
                "ck_promotion_product_is_stackable",
                @"is_stackable IN (0,1)");
            table.HasCheckConstraint(
                "ck_promotion_product_status",
                @"status IN ('active','paused','ended')");
            table.HasCheckConstraint(
                "ck_promotion_product_valid_to",
                @"valid_to IS NULL OR valid_to > valid_from");
            table.HasCheckConstraint(
                "ck_promotion_product_value_type_2",
                @"value_type <> 'percent' OR promotion_value <= 10000");
        });

        builder.HasKey(x => new { x.PromotionId, x.ProductId, x.ValidFrom });

        builder.Property(x => x.PromotionId)
            .HasColumnName("promotion_id");
        builder.Property(x => x.ProductId)
            .HasColumnName("product_id");
        builder.Property(x => x.ValidFrom)
            .HasColumnName("valid_from");
        builder.Property(x => x.ValidTo)
            .HasColumnName("valid_to");
        builder.Property(x => x.ValueType)
            .HasColumnName("value_type")
            .HasConversion(EnumConverters.PromotionProductValueTypeConverter);
        builder.Property(x => x.PromotionValue)
            .HasColumnName("promotion_value");
        builder.Property(x => x.MinQuantity)
            .HasColumnName("min_quantity")
            .HasDefaultValue(0L)
            .HasSentinel(0L);
        builder.Property(x => x.MaxRedemptions)
            .HasColumnName("max_redemptions");
        builder.Property(x => x.RedemptionCount)
            .HasColumnName("redemption_count")
            .HasDefaultValue(0L)
            .HasSentinel(0L);
        builder.Property(x => x.Priority)
            .HasColumnName("priority")
            .HasDefaultValue(100L)
            .HasSentinel(100L);
        builder.Property(x => x.IsStackable)
            .HasColumnName("is_stackable")
            .HasDefaultValue(false)
            .HasSentinel(false);
        builder.Property(x => x.Description)
            .HasColumnName("description");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.PromotionProductStatusConverter)
            .HasDefaultValue(PromotionProductStatus.Active)
            .HasSentinel(PromotionProductStatus.Active);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.ProductId)
            .HasDatabaseName("ix_promo_product_product");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .HasPrincipalKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Promotion>()
            .WithMany()
            .HasForeignKey(x => x.PromotionId)
            .HasPrincipalKey(x => x.PromotionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
