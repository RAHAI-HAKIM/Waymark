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
/// Maps <see cref="ProductAttributeValue"/> to <c>product_attribute_values</c>.
/// </summary>
internal sealed class ProductAttributeValueConfiguration : IEntityTypeConfiguration<ProductAttributeValue>
{
    public void Configure(EntityTypeBuilder<ProductAttributeValue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("product_attribute_values", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_product_attribute_values_value_bool",
                @"value_bool IS NULL OR value_bool IN (0,1)");
            table.HasCheckConstraint(
                "ck_product_attribute_values_CASE",
                @"(CASE WHEN value_text IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN value_number IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN value_bool IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN value_date IS NOT NULL THEN 1 ELSE 0 END) + (CASE WHEN option_code IS NOT NULL THEN 1 ELSE 0 END) = 1");
        });

        builder.HasKey(x => new { x.ProductId, x.AttributeCode });

        builder.Property(x => x.ProductId)
            .HasColumnName("product_id");
        builder.Property(x => x.AttributeCode)
            .HasColumnName("attribute_code");
        builder.Property(x => x.ValueText)
            .HasColumnName("value_text");
        builder.Property(x => x.ValueNumber)
            .HasColumnName("value_number");
        builder.Property(x => x.ValueBool)
            .HasColumnName("value_bool");
        builder.Property(x => x.ValueDate)
            .HasColumnName("value_date")
            .HasConversion(WaymarkConverters.Date);
        builder.Property(x => x.OptionCode)
            .HasColumnName("option_code");
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.AttributeCode)
            .HasDatabaseName("ix_pav_attribute");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<AttributeOption>()
            .WithMany()
            .HasForeignKey(x => new { x.AttributeCode, x.OptionCode })
            .HasPrincipalKey(x => new { x.AttributeCode, x.OptionCode })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<AttributeDefinition>()
            .WithMany()
            .HasForeignKey(x => x.AttributeCode)
            .HasPrincipalKey(x => x.AttributeCode)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .HasPrincipalKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
