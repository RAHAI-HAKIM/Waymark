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
/// Maps <see cref="StoreEntitlement"/> to <c>store_entitlements</c>.
/// </summary>
internal sealed class StoreEntitlementConfiguration : IEntityTypeConfiguration<StoreEntitlement>
{
    public void Configure(EntityTypeBuilder<StoreEntitlement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("store_entitlements", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_store_entitlements_is_enabled",
                @"is_enabled IN (0,1)");
            table.HasCheckConstraint(
                "ck_store_entitlements_tier",
                @"tier IN ('basic','pro','enterprise')");
        });

        builder.HasKey(x => x.EntitlementCode);

        builder.Property(x => x.EntitlementCode)
            .HasColumnName("entitlement_code");
        builder.Property(x => x.IsEnabled)
            .HasColumnName("is_enabled")
            .HasDefaultValue(false)
            .HasSentinel(false);
        builder.Property(x => x.Tier)
            .HasColumnName("tier")
            .HasConversion(EnumConverters.TierConverter);
        builder.Property(x => x.ValidFrom)
            .HasColumnName("valid_from");
        builder.Property(x => x.ValidTo)
            .HasColumnName("valid_to");
        builder.Property(x => x.SyncedAt)
            .HasColumnName("synced_at")
            .HasConversion(WaymarkConverters.Timestamp);
    }
}
