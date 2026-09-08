// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Enums;
using Waymark.Domain.Pricing;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="ProductBundle"/> to <c>product_bundles</c>.
/// </summary>
internal sealed class ProductBundleConfiguration : IEntityTypeConfiguration<ProductBundle>
{
    public void Configure(EntityTypeBuilder<ProductBundle> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("product_bundles", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_product_bundles_bundle_type",
                @"bundle_type IN ('fixed','mix_and_match')");
            table.HasCheckConstraint(
                "ck_product_bundles_status",
                @"status IN ('active','inactive','archived')");
        });

        builder.HasKey(x => x.BundleId);

        builder.Property(x => x.BundleId)
            .HasColumnName("bundle_id");
        builder.Property(x => x.Barcode)
            .HasColumnName("barcode");
        builder.Property(x => x.BundleName)
            .HasColumnName("bundle_name");
        builder.Property(x => x.BundleType)
            .HasColumnName("bundle_type")
            .HasConversion(EnumConverters.BundleTypeConverter);
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.ProductBundleStatusConverter)
            .HasDefaultValue(ProductBundleStatus.Active);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.Barcode).IsUnique();
    }
}
