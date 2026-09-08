// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Enums;
using Waymark.Domain.Sync;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="InboxMessage"/> to <c>inbox</c>.
/// </summary>
internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("inbox", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_inbox_channel",
                @"channel IN ('C_recommendations','D_intents','E_control','F_parameters')");
            table.HasCheckConstraint(
                "ck_inbox_status",
                @"status IN ('pending','applied','duplicate','rejected')");
        });

        builder.HasKey(x => x.InboxId);

        builder.Property(x => x.InboxId)
            .HasColumnName("inbox_id");
        builder.Property(x => x.MessageId)
            .HasColumnName("message_id");
        builder.Property(x => x.CloudSequence)
            .HasColumnName("cloud_sequence");
        builder.Property(x => x.Channel)
            .HasColumnName("channel")
            .HasConversion(EnumConverters.InboxMessageChannelConverter);
        builder.Property(x => x.MessageType)
            .HasColumnName("message_type");
        builder.Property(x => x.PayloadJson)
            .HasColumnName("payload_json");
        builder.Property(x => x.ReceivedAt)
            .HasColumnName("received_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.AppliedAt)
            .HasColumnName("applied_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.InboxMessageStatusConverter)
            .HasDefaultValue(InboxMessageStatus.Pending);
        builder.Property(x => x.RejectionReason)
            .HasColumnName("rejection_reason");

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.MessageId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CloudSequence })
            .HasDatabaseName("ix_inbox_pending")
            .HasFilter(@"status = 'pending'");
    }
}
