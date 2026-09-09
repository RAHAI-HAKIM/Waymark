// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Engine;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="RecommendationOption"/> to <c>recommendation_options</c>.
/// </summary>
internal sealed class RecommendationOptionConfiguration : IEntityTypeConfiguration<RecommendationOption>
{
    public void Configure(EntityTypeBuilder<RecommendationOption> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("recommendation_options");

        builder.HasKey(x => x.OptionId);

        builder.Property(x => x.OptionId)
            .HasColumnName("option_id");
        builder.Property(x => x.RecommendationId)
            .HasColumnName("recommendation_id");
        builder.Property(x => x.Label)
            .HasColumnName("label");
        builder.Property(x => x.DisplayOrder)
            .HasColumnName("display_order")
            .HasDefaultValue(0L)
            .HasSentinel(0L);
        builder.Property(x => x.PayloadJson)
            .HasColumnName("payload_json");
        builder.Property(x => x.ProjectedValue)
            .HasColumnName("projected_value");

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => new { x.RecommendationId, x.DisplayOrder }).IsUnique();
        builder.HasIndex(x => x.RecommendationId)
            .HasDatabaseName("ix_rec_options_rec");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Recommendation>()
            .WithMany()
            .HasForeignKey(x => x.RecommendationId)
            .HasPrincipalKey(x => x.RecommendationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
