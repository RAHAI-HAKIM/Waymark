using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Customers;
using Waymark.Domain.Organisation;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// <c>credit_limit_events</c> (B7, D-096): append-only, its three guards in <c>triggers.sql</c>
/// (D-058). The tenant's, like the customer it belongs to.
/// </summary>
internal sealed class CreditLimitEventConfiguration : IEntityTypeConfiguration<CreditLimitEvent>
{
    public void Configure(EntityTypeBuilder<CreditLimitEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("credit_limit_events", table =>
        {
            table.HasCheckConstraint("ck_credit_limit_events_event_type", "event_type IN ('set','frozen','unfrozen')");

            // A freeze or an unfreeze says nothing of the limit, which it leaves as it was.
            table.HasCheckConstraint(
                "ck_credit_limit_events_limits",
                "event_type = 'set' OR (previous_limit IS NULL AND new_limit IS NULL)");
            table.HasCheckConstraint(
                "ck_credit_limit_events_new_limit",
                "new_limit IS NULL OR new_limit >= 0");
        });

        builder.HasKey(x => x.EventId);
        builder.Property(x => x.EventId)
            .HasColumnName("event_id");
        builder.Property(x => x.CustomerId)
            .HasColumnName("customer_id");
        builder.Property(x => x.EventType)
            .HasColumnName("event_type")
            .HasConversion(EnumConverters.CreditLimitEventTypeConverter);
        builder.Property(x => x.PreviousLimit)
            .HasColumnName("previous_limit");
        builder.Property(x => x.NewLimit)
            .HasColumnName("new_limit");
        builder.Property(x => x.StaffId)
            .HasColumnName("staff_id");
        builder.Property(x => x.OccurredAt)
            .HasColumnName("occurred_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A customer's history, oldest first.
        builder.HasIndex(x => new { x.CustomerId, x.OccurredAt })
            .HasDatabaseName("ix_credit_limit_events_customer");

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .HasPrincipalKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.StaffId)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
