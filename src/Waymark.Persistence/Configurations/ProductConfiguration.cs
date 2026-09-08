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

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Product"/> to <c>products</c>.
/// </summary>
internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("products", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_products_status",
                @"status IN ('active','discontinued','archived')");
        });

        builder.HasKey(x => x.ProductId);

        builder.Property(x => x.ProductId)
            .HasColumnName("product_id");
        builder.Property(x => x.ProductName)
            .HasColumnName("product_name");
        builder.Property(x => x.Description)
            .HasColumnName("description");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.ProductStatusConverter)
            .HasDefaultValue(ProductStatus.Active);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);
    }
}
