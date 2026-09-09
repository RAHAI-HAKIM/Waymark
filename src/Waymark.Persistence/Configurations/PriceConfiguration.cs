// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Price"/> to <c>prices</c>.
/// </summary>
internal sealed class PriceConfiguration : IEntityTypeConfiguration<Price>
{
    public void Configure(EntityTypeBuilder<Price> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("prices", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_prices_price_type",
                @"price_type IN ('retail','wholesale','staff','promotional')");
            table.HasCheckConstraint(
                "ck_prices_price",
                @"price >= 0");
            table.HasCheckConstraint(
                "ck_prices_is_tax_inclusive",
                @"is_tax_inclusive IN (0,1)");
            table.HasCheckConstraint(
                "ck_prices_valid_to",
                @"valid_to IS NULL OR valid_to > valid_from");
        });

        builder.HasKey(x => new { x.VariantId, x.ValidFrom, x.StoreId, x.PriceType });

        builder.Property(x => x.VariantId)
            .HasColumnName("variant_id");
        builder.Property(x => x.ValidFrom)
            .HasColumnName("valid_from");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.PriceType)
            .HasColumnName("price_type")
            .HasConversion(EnumConverters.PriceTypeConverter)
            .HasDefaultValue(PriceType.Retail)
            .HasSentinel(PriceType.Retail);
        builder.Property(x => x.PriceValue)
            .HasColumnName("price");
        builder.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasDefaultValue("DZD")
            .HasSentinel("DZD");
        builder.Property(x => x.IsTaxInclusive)
            .HasColumnName("is_tax_inclusive")
            .HasDefaultValue(true)
            .HasSentinel(true);
        builder.Property(x => x.ValidTo)
            .HasColumnName("valid_to");
        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => new { x.VariantId, x.StoreId, x.ValidFrom })
            .HasDatabaseName("ix_prices_lookup");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.CreatedBy)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Variant>()
            .WithMany()
            .HasForeignKey(x => x.VariantId)
            .HasPrincipalKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
