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
/// Maps <see cref="Category"/> to <c>categories</c>.
/// </summary>
internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("categories", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_categories_sensitive_flag",
                @"sensitive_flag IN (0,1)");
            table.HasCheckConstraint(
                "ck_categories_status",
                @"status IN ('active','archived')");
            table.HasCheckConstraint(
                "ck_categories_tax_rate",
                @"tax_rate IS NULL OR tax_rate BETWEEN 0 AND 10000");
            table.HasCheckConstraint(
                "ck_categories_sensitive_flag_2",
                @"sensitive_flag = 0 OR sensitive_reason IS NOT NULL");
            table.HasCheckConstraint(
                "ck_categories_parent_id",
                @"parent_id IS NULL OR parent_id <> category_id");
        });

        builder.HasKey(x => x.CategoryId);

        builder.Property(x => x.CategoryId)
            .HasColumnName("category_id");
        builder.Property(x => x.CategoryName)
            .HasColumnName("category_name");
        builder.Property(x => x.ParentId)
            .HasColumnName("parent_id");
        builder.Property(x => x.Slug)
            .HasColumnName("slug");
        builder.Property(x => x.Description)
            .HasColumnName("description");
        builder.Property(x => x.Image)
            .HasColumnName("image");
        builder.Property(x => x.TaxRate)
            .HasColumnName("tax_rate");
        builder.Property(x => x.SensitiveFlag)
            .HasColumnName("sensitive_flag")
            .HasDefaultValue(false);
        builder.Property(x => x.SensitiveReason)
            .HasColumnName("sensitive_reason");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.CategoryStatusConverter)
            .HasDefaultValue(CategoryStatus.Active);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.ParentId)
            .HasDatabaseName("ix_categories_parent");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(x => x.ParentId)
            .HasPrincipalKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
