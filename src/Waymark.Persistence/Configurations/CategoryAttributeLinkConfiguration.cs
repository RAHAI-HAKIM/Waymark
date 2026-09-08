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
/// Maps <see cref="CategoryAttributeLink"/> to <c>category_attributes</c>.
/// </summary>
internal sealed class CategoryAttributeLinkConfiguration : IEntityTypeConfiguration<CategoryAttributeLink>
{
    public void Configure(EntityTypeBuilder<CategoryAttributeLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("category_attributes", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_category_attributes_is_required",
                @"is_required IN (0,1)");
            table.HasCheckConstraint(
                "ck_category_attributes_inherits_to_children",
                @"inherits_to_children IN (0,1)");
        });

        builder.HasKey(x => new { x.CategoryId, x.AttributeCode });

        builder.Property(x => x.CategoryId)
            .HasColumnName("category_id");
        builder.Property(x => x.AttributeCode)
            .HasColumnName("attribute_code");
        builder.Property(x => x.IsRequired)
            .HasColumnName("is_required")
            .HasDefaultValue(false);
        builder.Property(x => x.DisplayOrder)
            .HasColumnName("display_order")
            .HasDefaultValue(0L);
        builder.Property(x => x.InheritsToChildren)
            .HasColumnName("inherits_to_children")
            .HasDefaultValue(true);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.AttributeCode)
            .HasDatabaseName("ix_category_attr_attribute");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<AttributeDefinition>()
            .WithMany()
            .HasForeignKey(x => x.AttributeCode)
            .HasPrincipalKey(x => x.AttributeCode)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .HasPrincipalKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
