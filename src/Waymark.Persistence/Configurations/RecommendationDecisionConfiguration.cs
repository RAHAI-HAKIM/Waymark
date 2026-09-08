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
/// Maps <see cref="RecommendationDecision"/> to <c>recommendation_decisions</c>.
/// </summary>
internal sealed class RecommendationDecisionConfiguration : IEntityTypeConfiguration<RecommendationDecision>
{
    public void Configure(EntityTypeBuilder<RecommendationDecision> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("recommendation_decisions", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_recommendation_decisions_decision",
                @"decision IN ('accept','adjust','dismiss','snooze')");
            table.HasCheckConstraint(
                "ck_recommendation_decisions_origin",
                @"origin IN ('store','cloud')");
            table.HasCheckConstraint(
                "ck_recommendation_decisions_decision_2",
                @"decision <> 'snooze' OR snooze_until IS NOT NULL");
            table.HasCheckConstraint(
                "ck_recommendation_decisions_decision_3",
                @"decision <> 'adjust' OR adjusted_payload_json IS NOT NULL");
        });

        builder.HasKey(x => x.DecisionId);

        builder.Property(x => x.DecisionId)
            .HasColumnName("decision_id");
        builder.Property(x => x.RecommendationId)
            .HasColumnName("recommendation_id");
        builder.Property(x => x.Decision)
            .HasColumnName("decision")
            .HasConversion(EnumConverters.DecisionConverter);
        builder.Property(x => x.ChosenOptionId)
            .HasColumnName("chosen_option_id");
        builder.Property(x => x.AdjustedPayloadJson)
            .HasColumnName("adjusted_payload_json");
        builder.Property(x => x.SnoozeUntil)
            .HasColumnName("snooze_until");
        builder.Property(x => x.Origin)
            .HasColumnName("origin")
            .HasConversion(EnumConverters.OriginConverter);
        builder.Property(x => x.DecidedBy)
            .HasColumnName("decided_by");
        builder.Property(x => x.DecidedAt)
            .HasColumnName("decided_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.TerminalId)
            .HasColumnName("terminal_id");
        builder.Property(x => x.AppliedAt)
            .HasColumnName("applied_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.ResultingEntityType)
            .HasColumnName("resulting_entity_type");
        builder.Property(x => x.ResultingEntityId)
            .HasColumnName("resulting_entity_id");

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.DecidedAt)
            .HasDatabaseName("ix_rec_decisions_date");
        builder.HasIndex(x => x.RecommendationId)
            .HasDatabaseName("ix_rec_decisions_rec");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Terminal>()
            .WithMany()
            .HasForeignKey(x => x.TerminalId)
            .HasPrincipalKey(x => x.TerminalId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.DecidedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<RecommendationOption>()
            .WithMany()
            .HasForeignKey(x => x.ChosenOptionId)
            .HasPrincipalKey(x => x.OptionId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Recommendation>()
            .WithMany()
            .HasForeignKey(x => x.RecommendationId)
            .HasPrincipalKey(x => x.RecommendationId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
