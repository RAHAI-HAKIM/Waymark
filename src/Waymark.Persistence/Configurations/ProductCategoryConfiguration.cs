// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Catalogue;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="ProductCategory"/> to <c>product_category</c>.
/// </summary>
internal sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("product_category", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_product_category_is_primary",
                @"is_primary IN (0,1)");
        });

        builder.HasKey(x => new { x.ProductId, x.CategoryId });

        builder.Property(x => x.ProductId)
            .HasColumnName("product_id");
        builder.Property(x => x.CategoryId)
            .HasColumnName("category_id");
        builder.Property(x => x.IsPrimary)
            .HasColumnName("is_primary")
            .HasDefaultValue(false)
            .HasSentinel(false);
        builder.Property(x => x.AddedAt)
            .HasColumnName("added_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.CategoryId)
            .HasDatabaseName("ix_product_category_cat");
        builder.HasIndex(x => x.ProductId)
            .HasDatabaseName("ux_product_category_primary")
            .IsUnique()
            .HasFilter(@"is_primary = 1");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .HasPrincipalKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .HasPrincipalKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
