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
using Waymark.Domain.Sync;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Intent"/> to <c>intents</c>.
/// </summary>
internal sealed class IntentConfiguration : IEntityTypeConfiguration<Intent>
{
    public void Configure(EntityTypeBuilder<Intent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("intents", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_intents_intent_type",
                @"intent_type IN ('accept_reorder','markdown_stage','adjust_quantity','dismiss', 'price_change','promotion','catalogue_edit','supplier_edit', 'rights_action')");
            table.HasCheckConstraint(
                "ck_intents_status",
                @"status IN ('pending','applied','rejected_stale','rejected_invalid')");
            table.HasCheckConstraint(
                "ck_intents_status_2",
                @"status NOT IN ('rejected_stale','rejected_invalid') OR rejection_reason IS NOT NULL");
        });

        builder.HasKey(x => x.IntentId);

        builder.Property(x => x.IntentId)
            .HasColumnName("intent_id");
        builder.Property(x => x.IntentType)
            .HasColumnName("intent_type")
            .HasConversion(EnumConverters.IntentTypeConverter);
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.PayloadJson)
            .HasColumnName("payload_json");
        builder.Property(x => x.PreconditionsJson)
            .HasColumnName("preconditions_json");
        builder.Property(x => x.CreatedAtCloud)
            .HasColumnName("created_at_cloud");
        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.ReceivedAt)
            .HasColumnName("received_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.EvaluatedAt)
            .HasColumnName("evaluated_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.IntentStatusConverter)
            .HasDefaultValue(IntentStatus.Pending)
            .HasSentinel(IntentStatus.Pending);
        builder.Property(x => x.RejectionReason)
            .HasColumnName("rejection_reason");
        builder.Property(x => x.FreshRequestId)
            .HasColumnName("fresh_request_id");
        builder.Property(x => x.DecidedByCloudUser)
            .HasColumnName("decided_by_cloud_user");

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("ix_intents_expiry")
            .HasFilter(@"status = 'pending'");
        builder.HasIndex(x => new { x.StoreId, x.Status })
            .HasDatabaseName("ix_intents_pending")
            .HasFilter(@"status = 'pending'");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Recommendation>()
            .WithMany()
            .HasForeignKey(x => x.FreshRequestId)
            .HasPrincipalKey(x => x.RecommendationId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
