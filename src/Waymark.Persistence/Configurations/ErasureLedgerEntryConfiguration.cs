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
/// Maps <see cref="ErasureLedgerEntry"/> to <c>erasure_ledger</c>.
/// </summary>
internal sealed class ErasureLedgerEntryConfiguration : IEntityTypeConfiguration<ErasureLedgerEntry>
{
    public void Configure(EntityTypeBuilder<ErasureLedgerEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("erasure_ledger", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_erasure_ledger_subject_type",
                @"subject_type IN ('customer','staff')");
            table.HasCheckConstraint(
                "ck_erasure_ledger_status",
                @"status IN ('pending','executed','blocked')");
            table.HasCheckConstraint(
                "ck_erasure_ledger_status_2",
                @"status <> 'blocked' OR blocked_reason IS NOT NULL");
            table.HasCheckConstraint(
                "ck_erasure_ledger_status_3",
                @"status <> 'executed' OR executed_at IS NOT NULL");
        });

        builder.HasKey(x => x.ErasureId);

        builder.Property(x => x.ErasureId)
            .HasColumnName("erasure_id");
        builder.Property(x => x.SubjectType)
            .HasColumnName("subject_type")
            .HasConversion(EnumConverters.ErasureLedgerEntrySubjectTypeConverter);
        builder.Property(x => x.SubjectId)
            .HasColumnName("subject_id");
        builder.Property(x => x.RequestId)
            .HasColumnName("request_id");
        builder.Property(x => x.RequestedAt)
            .HasColumnName("requested_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.ExecutedAt)
            .HasColumnName("executed_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.ExecutedBy)
            .HasColumnName("executed_by");
        builder.Property(x => x.ScopeJson)
            .HasColumnName("scope_json");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.ErasureLedgerEntryStatusConverter)
            .HasDefaultValue(ErasureLedgerEntryStatus.Pending)
            .HasSentinel(ErasureLedgerEntryStatus.Pending);
        builder.Property(x => x.BlockedReason)
            .HasColumnName("blocked_reason");
        builder.Property(x => x.CloudConfirmedAt)
            .HasColumnName("cloud_confirmed_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => new { x.SubjectType, x.SubjectId })
            .HasDatabaseName("ix_erasure_subject");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.ExecutedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<DataSubjectRequest>()
            .WithMany()
            .HasForeignKey(x => x.RequestId)
            .HasPrincipalKey(x => x.RequestId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
