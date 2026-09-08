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

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Recommendation"/> to <c>recommendations</c>.
/// </summary>
internal sealed class RecommendationConfiguration : IEntityTypeConfiguration<Recommendation>
{
    public void Configure(EntityTypeBuilder<Recommendation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("recommendations", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_recommendations_department",
                @"department IN ('inventory','sales_demand','supply','planning','customer')");
            table.HasCheckConstraint(
                "ck_recommendations_urgency",
                @"urgency IN ('quiet','standard','warning','critical')");
            table.HasCheckConstraint(
                "ck_recommendations_action_type",
                @"action_type IN ('binary','menu')");
            table.HasCheckConstraint(
                "ck_recommendations_subject_type",
                @"subject_type IN ('variant','batch','product','supplier','customer','store')");
            table.HasCheckConstraint(
                "ck_recommendations_status",
                @"status IN ('pending','delivered','decided','expired','superseded')");
            table.HasCheckConstraint(
                "ck_recommendations_interval_low",
                @"interval_low IS NULL OR interval_high IS NULL OR interval_high >= interval_low");
        });

        builder.HasKey(x => x.RecommendationId);

        builder.Property(x => x.RecommendationId)
            .HasColumnName("recommendation_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.Department)
            .HasColumnName("department")
            .HasConversion(EnumConverters.DepartmentConverter);
        builder.Property(x => x.Urgency)
            .HasColumnName("urgency")
            .HasConversion(EnumConverters.UrgencyConverter);
        builder.Property(x => x.ActionType)
            .HasColumnName("action_type")
            .HasConversion(EnumConverters.ActionTypeConverter);
        builder.Property(x => x.SubjectType)
            .HasColumnName("subject_type")
            .HasConversion(EnumConverters.RecommendationSubjectTypeConverter);
        builder.Property(x => x.SubjectId)
            .HasColumnName("subject_id");
        builder.Property(x => x.Headline)
            .HasColumnName("headline");
        builder.Property(x => x.BecauseJson)
            .HasColumnName("because_json");
        builder.Property(x => x.IntervalLow)
            .HasColumnName("interval_low");
        builder.Property(x => x.IntervalHigh)
            .HasColumnName("interval_high");
        builder.Property(x => x.ComputedAt)
            .HasColumnName("computed_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.ParameterVersion)
            .HasColumnName("parameter_version");
        builder.Property(x => x.Source)
            .HasColumnName("source");
        builder.Property(x => x.MinimumRequiredRole)
            .HasColumnName("minimum_required_role");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.RecommendationStatusConverter)
            .HasDefaultValue(RecommendationStatus.Pending);
        builder.Property(x => x.IssuedAt)
            .HasColumnName("issued_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.DeliveredAt)
            .HasColumnName("delivered_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => new { x.SubjectType, x.SubjectId })
            .HasDatabaseName("ix_recs_subject");
        builder.HasIndex(x => new { x.StoreId, x.Status, x.Urgency })
            .HasDatabaseName("ix_recs_pending")
            .HasFilter(@"status IN ('pending','delivered')");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(x => x.MinimumRequiredRole)
            .HasPrincipalKey(x => x.RoleCode)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
