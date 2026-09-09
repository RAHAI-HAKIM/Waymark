// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Engine;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Promotion"/> to <c>promotions</c>.
/// </summary>
internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("promotions", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_promotions_promotion_type",
                @"promotion_type IN ('discount','bogo','bundle','markdown')");
            table.HasCheckConstraint(
                "ck_promotions_status",
                @"status IN ('draft','scheduled','active','ended','cancelled')");
        });

        builder.HasKey(x => x.PromotionId);

        builder.Property(x => x.PromotionId)
            .HasColumnName("promotion_id");
        builder.Property(x => x.PromotionName)
            .HasColumnName("promotion_name");
        builder.Property(x => x.PromotionType)
            .HasColumnName("promotion_type")
            .HasConversion(EnumConverters.PromotionTypeConverter);
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.SourceRecommendationId)
            .HasColumnName("source_recommendation_id");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.PromotionStatusConverter)
            .HasDefaultValue(PromotionStatus.Draft)
            .HasSentinel(PromotionStatus.Draft);
        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => new { x.Status, x.StoreId })
            .HasDatabaseName("ix_promotions_status");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Recommendation>()
            .WithMany()
            .HasForeignKey(x => x.SourceRecommendationId)
            .HasPrincipalKey(x => x.RecommendationId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
