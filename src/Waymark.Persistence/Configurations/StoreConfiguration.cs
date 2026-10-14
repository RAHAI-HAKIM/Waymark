// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Values;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Store"/> to <c>stores</c>.
/// </summary>
internal sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stores", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_stores_status",
                @"status IN ('active','suspended','closed')");

            // D-122: whether the store charges TVA.
            table.HasCheckConstraint(
                "ck_stores_tax_regime",
                @"tax_regime IN ('not_subject','subject')");
            table.HasCheckConstraint(
                "ck_stores_rounding_policy",
                @"rounding_policy IN ('half_even','half_up')");
        });

        builder.HasKey(x => x.StoreId);

        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.StoreCode)
            .HasColumnName("store_code");
        builder.Property(x => x.StoreName)
            .HasColumnName("store_name");
        builder.Property(x => x.ContactPhone)
            .HasColumnName("contact_phone");
        builder.Property(x => x.Email)
            .HasColumnName("email");
        builder.Property(x => x.Address)
            .HasColumnName("address");
        builder.Property(x => x.Latitude)
            .HasColumnName("latitude");
        builder.Property(x => x.Longitude)
            .HasColumnName("longitude");
        builder.Property(x => x.StoreType)
            .HasColumnName("store_type");
        builder.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasDefaultValue("DZD")
            .HasSentinel("DZD");
        builder.Property(x => x.Timezone)
            .HasColumnName("timezone")
            .HasDefaultValue("Africa/Algiers")
            .HasSentinel("Africa/Algiers");
        builder.Property(x => x.TaxRegistrationNumber)
            .HasColumnName("tax_registration_number");
        builder.Property(x => x.FiscalYearStart)
            .HasColumnName("fiscal_year_start")
            .HasDefaultValue("01-01")
            .HasSentinel("01-01");
        builder.Property(x => x.ManagerId)
            .HasColumnName("manager_id");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.StoreStatusConverter)
            .HasDefaultValue(StoreStatus.Active)
            .HasSentinel(StoreStatus.Active);
        builder.Property(x => x.RoundingPolicy)
            .HasColumnName("rounding_policy")
            .HasConversion(EnumConverters.RoundingConverter)
            .HasDefaultValue(Rounding.HalfUp)
            .HasSentinel(Rounding.HalfUp);
        builder.Property(x => x.ScaleLabelFormat)
            .HasColumnName("scale_label_format");

        // D-122: not subject until somebody says the store charges TVA.
        builder.Property(x => x.TaxRegime)
            .HasColumnName("tax_regime")
            .HasConversion(EnumConverters.TaxRegimeConverter)
            .HasDefaultValue(TaxRegime.NotSubject)
            .HasSentinel(TaxRegime.NotSubject);

        // D-125: the store's own text at the foot of its receipts. Its length is the command's to
        // refuse: a CHECK would rebuild stores for a column added.
        builder.Property(x => x.ReceiptFooter)
            .HasColumnName("receipt_footer");
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.StoreCode).IsUnique();

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<Staff>()
            .WithMany()
            .HasForeignKey(x => x.ManagerId)
            .HasPrincipalKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
