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
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Staff"/> to <c>staff</c>.
/// </summary>
internal sealed class StaffConfiguration : IEntityTypeConfiguration<Staff>
{
    public void Configure(EntityTypeBuilder<Staff> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("staff", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_staff_status",
                @"status IN ('active','suspended','terminated')");
        });

        builder.HasKey(x => x.StaffId);

        builder.Property(x => x.StaffId)
            .HasColumnName("staff_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.StaffName)
            .HasColumnName("staff_name");
        builder.Property(x => x.ContactPhone)
            .HasColumnName("contact_phone");
        builder.Property(x => x.Email)
            .HasColumnName("email");
        builder.Property(x => x.Role)
            .HasColumnName("role");
        builder.Property(x => x.PinHash)
            .HasColumnName("pin_hash");
        builder.Property(x => x.JoinDate)
            .HasColumnName("join_date")
            .HasConversion(WaymarkConverters.Date);
        builder.Property(x => x.LastSeenDate)
            .HasColumnName("last_seen_date")
            .HasConversion(WaymarkConverters.Date);
        builder.Property(x => x.TerminationDate)
            .HasColumnName("termination_date")
            .HasConversion(WaymarkConverters.Date);
        builder.Property(x => x.NoticeVersionAcknowledged)
            .HasColumnName("notice_version_acknowledged");
        builder.Property(x => x.NoticeAcknowledgedAt)
            .HasColumnName("notice_acknowledged_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.StaffStatusConverter)
            .HasDefaultValue(StaffStatus.Active);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => new { x.StoreId, x.Status })
            .HasDatabaseName("ix_staff_store");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<NoticeVersion>()
            .WithMany()
            .HasForeignKey(x => x.NoticeVersionAcknowledged)
            .HasPrincipalKey(x => x.VersionCode)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(x => x.Role)
            .HasPrincipalKey(x => x.RoleCode)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
