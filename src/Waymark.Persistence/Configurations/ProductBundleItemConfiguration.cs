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
/// Maps <see cref="ProductBundleItem"/> to <c>product_bundle_items</c>.
/// </summary>
internal sealed class ProductBundleItemConfiguration : IEntityTypeConfiguration<ProductBundleItem>
{
    public void Configure(EntityTypeBuilder<ProductBundleItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("product_bundle_items", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_product_bundle_items_quantity",
                @"quantity > 0");
            table.HasCheckConstraint(
                "ck_product_bundle_items_status",
                @"status IN ('active','inactive')");
            table.HasCheckConstraint(
                "ck_product_bundle_items_valid_to",
                @"valid_to IS NULL OR valid_to > valid_from");
        });

        builder.HasKey(x => x.BundleItemId);

        builder.Property(x => x.BundleItemId)
            .HasColumnName("bundle_item_id");
        builder.Property(x => x.BundleId)
            .HasColumnName("bundle_id");
        builder.Property(x => x.VariantId)
            .HasColumnName("variant_id");
        builder.Property(x => x.Quantity)
            .HasColumnName("quantity");
        builder.Property(x => x.ValidFrom)
            .HasColumnName("valid_from");
        builder.Property(x => x.ValidTo)
            .HasColumnName("valid_to");
        builder.Property(x => x.Description)
            .HasColumnName("description");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.ProductBundleItemStatusConverter)
            .HasDefaultValue(ProductBundleItemStatus.Active);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.VariantId)
            .HasDatabaseName("ix_bundle_items_variant");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Variant>()
            .WithMany()
            .HasForeignKey(x => x.VariantId)
            .HasPrincipalKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ProductBundle>()
            .WithMany()
            .HasForeignKey(x => x.BundleId)
            .HasPrincipalKey(x => x.BundleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
