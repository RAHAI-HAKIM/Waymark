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
/// Maps <see cref="PromotionVariant"/> to <c>promotion_variant</c>.
/// </summary>
internal sealed class PromotionVariantConfiguration : IEntityTypeConfiguration<PromotionVariant>
{
    public void Configure(EntityTypeBuilder<PromotionVariant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("promotion_variant", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_promotion_variant_value_type",
                @"value_type IN ('percent','amount','bogo')");
            table.HasCheckConstraint(
                "ck_promotion_variant_promotion_value",
                @"promotion_value >= 0");
            table.HasCheckConstraint(
                "ck_promotion_variant_is_stackable",
                @"is_stackable IN (0,1)");
            table.HasCheckConstraint(
                "ck_promotion_variant_status",
                @"status IN ('active','paused','ended')");
            table.HasCheckConstraint(
                "ck_promotion_variant_valid_to",
                @"valid_to IS NULL OR valid_to > valid_from");
            table.HasCheckConstraint(
                "ck_promotion_variant_value_type_2",
                @"value_type <> 'percent' OR promotion_value <= 10000");
        });

        builder.HasKey(x => new { x.PromotionId, x.VariantId, x.ValidFrom });

        builder.Property(x => x.PromotionId)
            .HasColumnName("promotion_id");
        builder.Property(x => x.VariantId)
            .HasColumnName("variant_id");
        builder.Property(x => x.ValidFrom)
            .HasColumnName("valid_from");
        builder.Property(x => x.ValidTo)
            .HasColumnName("valid_to");
        builder.Property(x => x.ValueType)
            .HasColumnName("value_type")
            .HasConversion(EnumConverters.PromotionVariantValueTypeConverter);
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
            .HasConversion(EnumConverters.PromotionVariantStatusConverter)
            .HasDefaultValue(PromotionVariantStatus.Active)
            .HasSentinel(PromotionVariantStatus.Active);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.VariantId)
            .HasDatabaseName("ix_promo_variant_variant");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Variant>()
            .WithMany()
            .HasForeignKey(x => x.VariantId)
            .HasPrincipalKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Promotion>()
            .WithMany()
            .HasForeignKey(x => x.PromotionId)
            .HasPrincipalKey(x => x.PromotionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
