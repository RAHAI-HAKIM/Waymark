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
/// Maps <see cref="ProcessingLogEntry"/> to <c>processing_log</c>.
/// </summary>
internal sealed class ProcessingLogEntryConfiguration : IEntityTypeConfiguration<ProcessingLogEntry>
{
    public void Configure(EntityTypeBuilder<ProcessingLogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("processing_log", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_processing_log_operation",
                @"operation IN ('collection','consultation','disclosure','transmission', 'modification','erasure','pseudonymisation','re_identification', 'recording','organisation','storage','extraction','use','dissemination','alignment','interconnection','locking','encryption','destruction')");
            table.HasCheckConstraint(
                "ck_processing_log_subject_type",
                @"subject_type IN ('customer','staff','supplier')");
            table.HasCheckConstraint(
                "ck_processing_log_actor_type",
                @"actor_type IN ('staff','system','engine','processor')");

            // D-115: a bulk operation says how many subjects it ran over, and only a row that names
            // no one may: a count beside a pseudonym would be a row about one person claiming many.
            table.HasCheckConstraint(
                "ck_processing_log_subject_count",
                @"subject_count IS NULL OR (subject_count > 0 AND subject_id IS NULL)");
        });

        builder.HasKey(x => x.LogId);

        builder.Property(x => x.LogId)
            .HasColumnName("log_id");
        builder.Property(x => x.OccurredAt)
            .HasColumnName("occurred_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.Operation)
            .HasColumnName("operation")
            .HasConversion(EnumConverters.OperationConverter);
        builder.Property(x => x.SubjectType)
            .HasColumnName("subject_type")
            .HasConversion(EnumConverters.ProcessingLogEntrySubjectTypeConverter);
        builder.Property(x => x.SubjectId)
            .HasColumnName("subject_id");
        builder.Property(x => x.ActorType)
            .HasColumnName("actor_type")
            .HasConversion(EnumConverters.ActorTypeConverter);
        builder.Property(x => x.ActorId)
            .HasColumnName("actor_id");
        builder.Property(x => x.Purpose)
            .HasColumnName("purpose")
            .HasConversion(EnumConverters.ProcessingPurposeConverter);
        builder.Property(x => x.LegalBasis)
            .HasColumnName("legal_basis")
            .HasConversion(EnumConverters.ProcessingLegalBasisConverter);
        builder.Property(x => x.Recipient)
            .HasColumnName("recipient");
        builder.Property(x => x.SubjectCount)
            .HasColumnName("subject_count");
        builder.Property(x => x.SourceModule)
            .HasColumnName("source_module");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.TerminalId)
            .HasColumnName("terminal_id");

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.OccurredAt)
            .HasDatabaseName("ix_proclog_occurred");
        builder.HasIndex(x => new { x.SubjectType, x.SubjectId, x.OccurredAt })
            .HasDatabaseName("ix_proclog_subject");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Terminal>()
            .WithMany()
            .HasForeignKey(x => x.TerminalId)
            .HasPrincipalKey(x => x.TerminalId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
