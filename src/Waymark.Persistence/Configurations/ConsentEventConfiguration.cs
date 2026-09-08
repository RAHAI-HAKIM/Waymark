// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Customers;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="ConsentEvent"/> to <c>consent_events</c>.
/// </summary>
internal sealed class ConsentEventConfiguration : IEntityTypeConfiguration<ConsentEvent>
{
    public void Configure(EntityTypeBuilder<ConsentEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("consent_events", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_consent_events_action",
                @"action IN ('granted','withdrawn','renewed')");
            table.HasCheckConstraint(
                "ck_consent_events_consent_type",
                @"consent_type IN ('processing','marketing')");
            table.HasCheckConstraint(
                "ck_consent_events_method",
                @"method IN ('verbal','written','digital')");
        });

        builder.HasKey(x => x.ConsentEventId);

        builder.Property(x => x.ConsentEventId)
            .HasColumnName("consent_event_id");
        builder.Property(x => x.CustomerId)
            .HasColumnName("customer_id");
        builder.Property(x => x.OccurredAt)
            .HasColumnName("occurred_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.Action)
            .HasColumnName("action")
            .HasConversion(EnumConverters.ConsentEventActionConverter);
        builder.Property(x => x.ConsentType)
            .HasColumnName("consent_type")
            .HasConversion(EnumConverters.ConsentTypeConverter);
        builder.Property(x => x.NoticeVersion)
            .HasColumnName("notice_version");
        builder.Property(x => x.CapturedBy)
            .HasColumnName("captured_by");
        builder.Property(x => x.Method)
            .HasColumnName("method")
            .HasConversion(EnumConverters.MethodConverter);
        builder.Property(x => x.TerminalId)
            .HasColumnName("terminal_id");

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => new { x.CustomerId, x.OccurredAt })
            .HasDatabaseName("ix_consent_customer");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Terminal>()
            .WithMany()
            .HasForeignKey(x => x.TerminalId)
            .HasPrincipalKey(x => x.TerminalId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.CapturedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<NoticeVersion>()
            .WithMany()
            .HasForeignKey(x => x.NoticeVersion)
            .HasPrincipalKey(x => x.VersionCode)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .HasPrincipalKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
