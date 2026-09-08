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
/// Maps <see cref="OutboxMessage"/> to <c>outbox</c>.
/// </summary>
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("outbox", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_outbox_channel",
                @"channel IN ('A_statistics','B_operational','D_decisions')");
            table.HasCheckConstraint(
                "ck_outbox_is_priority",
                @"is_priority IN (0,1)");
        });

        builder.HasKey(x => x.OutboxId);

        builder.Property(x => x.OutboxId)
            .HasColumnName("outbox_id");
        builder.Property(x => x.SequenceNumber)
            .HasColumnName("sequence_number");
        builder.Property(x => x.Channel)
            .HasColumnName("channel")
            .HasConversion(EnumConverters.OutboxMessageChannelConverter);
        builder.Property(x => x.MessageType)
            .HasColumnName("message_type");
        builder.Property(x => x.EntityType)
            .HasColumnName("entity_type");
        builder.Property(x => x.EntityId)
            .HasColumnName("entity_id");
        builder.Property(x => x.PayloadJson)
            .HasColumnName("payload_json");
        builder.Property(x => x.IsPriority)
            .HasColumnName("is_priority")
            .HasDefaultValue(false);
        builder.Property(x => x.Attempts)
            .HasColumnName("attempts")
            .HasDefaultValue(0L);
        builder.Property(x => x.LastAttemptAt)
            .HasColumnName("last_attempt_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.LastError)
            .HasColumnName("last_error");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.SequenceNumber).IsUnique();
        builder.HasIndex(x => new { x.IsPriority, x.SequenceNumber })
            .HasDatabaseName("ix_outbox_drain");
    }
}
