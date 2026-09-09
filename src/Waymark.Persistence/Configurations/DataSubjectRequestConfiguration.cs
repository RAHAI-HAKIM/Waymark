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

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="DataSubjectRequest"/> to <c>data_subject_requests</c>.
/// </summary>
internal sealed class DataSubjectRequestConfiguration : IEntityTypeConfiguration<DataSubjectRequest>
{
    public void Configure(EntityTypeBuilder<DataSubjectRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("data_subject_requests", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_data_subject_requests_request_type",
                @"request_type IN ('information','access','rectification','objection','erasure')");
            table.HasCheckConstraint(
                "ck_data_subject_requests_status",
                @"status IN ('open','in_progress','fulfilled','refused','blocked')");
            table.HasCheckConstraint(
                "ck_data_subject_requests_status_2",
                @"status NOT IN ('refused','blocked') OR resolution_note IS NOT NULL");
        });

        builder.HasKey(x => x.RequestId);

        builder.Property(x => x.RequestId)
            .HasColumnName("request_id");
        builder.Property(x => x.CustomerId)
            .HasColumnName("customer_id");
        builder.Property(x => x.RequestType)
            .HasColumnName("request_type")
            .HasConversion(EnumConverters.RequestTypeConverter);
        builder.Property(x => x.ReceivedAt)
            .HasColumnName("received_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.DueAt)
            .HasColumnName("due_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.ReceivedBy)
            .HasColumnName("received_by");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.DataSubjectRequestStatusConverter)
            .HasDefaultValue(DataSubjectRequestStatus.Open)
            .HasSentinel(DataSubjectRequestStatus.Open);
        builder.Property(x => x.ResolutionNote)
            .HasColumnName("resolution_note");
        builder.Property(x => x.ResolvedAt)
            .HasColumnName("resolved_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.DueAt)
            .HasDatabaseName("ix_dsr_open")
            .HasFilter(@"status IN ('open','in_progress')");
        builder.HasIndex(x => x.CustomerId)
            .HasDatabaseName("ix_dsr_customer");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.ReceivedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .HasPrincipalKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
