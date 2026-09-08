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
/// Maps <see cref="RetentionPolicy"/> to <c>retention_policies</c>.
/// </summary>
internal sealed class RetentionPolicyConfiguration : IEntityTypeConfiguration<RetentionPolicy>
{
    public void Configure(EntityTypeBuilder<RetentionPolicy> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("retention_policies", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_retention_policies_entity_type",
                @"entity_type IN ('transaction','customer','staff','processing_log', 'consent_event','recommendation','stock_movement')");
            table.HasCheckConstraint(
                "ck_retention_policies_retention_days",
                @"retention_days > 0");
            table.HasCheckConstraint(
                "ck_retention_policies_action_on_expiry",
                @"action_on_expiry IN ('delete','unlink','archive')");
            table.HasCheckConstraint(
                "ck_retention_policies_is_active",
                @"is_active IN (0,1)");
        });

        builder.HasKey(x => x.PolicyCode);

        builder.Property(x => x.PolicyCode)
            .HasColumnName("policy_code");
        builder.Property(x => x.EntityType)
            .HasColumnName("entity_type")
            .HasConversion(EnumConverters.EntityTypeConverter);
        builder.Property(x => x.RetentionDays)
            .HasColumnName("retention_days");
        builder.Property(x => x.LegalBasisReference)
            .HasColumnName("legal_basis_reference");
        builder.Property(x => x.ActionOnExpiry)
            .HasColumnName("action_on_expiry")
            .HasConversion(EnumConverters.ActionOnExpiryConverter);
        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);
    }
}
