using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="NoSaleOpening"/> to <c>no_sale_openings</c> (D-123): append-only, its three guards
/// in <c>triggers.sql</c>.
/// </summary>
internal sealed class NoSaleOpeningConfiguration : IEntityTypeConfiguration<NoSaleOpening>
{
    public void Configure(EntityTypeBuilder<NoSaleOpening> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("no_sale_openings");
        builder.HasKey(x => x.NoSaleId);

        builder.Property(x => x.NoSaleId)
            .HasColumnName("no_sale_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.SessionId)
            .HasColumnName("session_id");
        builder.Property(x => x.TerminalId)
            .HasColumnName("terminal_id");
        builder.Property(x => x.StaffId)
            .HasColumnName("staff_id");
        builder.Property(x => x.AuthorisedBy)
            .HasColumnName("authorised_by");
        builder.Property(x => x.ReasonCode)
            .HasColumnName("reason_code");
        builder.Property(x => x.Note)
            .HasColumnName("note");
        builder.Property(x => x.OccurredAt)
            .HasColumnName("occurred_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A session's report counts them (D-112 kept the line's place).
        builder.HasIndex(x => x.SessionId)
            .HasDatabaseName("ix_no_sale_openings_session");

        builder.HasOne<CashSession>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .HasPrincipalKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.NoAction);
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
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.StaffId)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.AuthorisedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ReasonCode>()
            .WithMany()
            .HasForeignKey(x => x.ReasonCode)
            .HasPrincipalKey(x => x.ReasonCodeValue)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
