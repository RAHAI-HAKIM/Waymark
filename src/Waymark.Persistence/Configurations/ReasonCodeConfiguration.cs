// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Enums;
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="ReasonCode"/> to <c>reason_codes</c>.
/// </summary>
internal sealed class ReasonCodeConfiguration : IEntityTypeConfiguration<ReasonCode>
{
    public void Configure(EntityTypeBuilder<ReasonCode> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("reason_codes", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_reason_codes_applies_to",
                @"applies_to IN ('discount','price_override','adjustment','void', 'return','no_sale','cash_movement','write_off')");
            table.HasCheckConstraint(
                "ck_reason_codes_requires_note",
                @"requires_note IN (0,1)");
            table.HasCheckConstraint(
                "ck_reason_codes_requires_manager",
                @"requires_manager IN (0,1)");
            table.HasCheckConstraint(
                "ck_reason_codes_is_active",
                @"is_active IN (0,1)");
        });

        builder.HasKey(x => x.ReasonCodeValue);

        builder.Property(x => x.ReasonCodeValue)
            .HasColumnName("reason_code");
        builder.Property(x => x.AppliesTo)
            .HasColumnName("applies_to")
            .HasConversion(EnumConverters.ReasonCodeAppliesToConverter);
        builder.Property(x => x.LabelAr)
            .HasColumnName("label_ar");
        builder.Property(x => x.LabelFr)
            .HasColumnName("label_fr");
        builder.Property(x => x.RequiresNote)
            .HasColumnName("requires_note")
            .HasDefaultValue(false)
            .HasSentinel(false);
        builder.Property(x => x.RequiresManager)
            .HasColumnName("requires_manager")
            .HasDefaultValue(false)
            .HasSentinel(false);
        builder.Property(x => x.DisplayOrder)
            .HasColumnName("display_order")
            .HasDefaultValue(0L)
            .HasSentinel(0L);
        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .HasSentinel(true);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
    }
}
