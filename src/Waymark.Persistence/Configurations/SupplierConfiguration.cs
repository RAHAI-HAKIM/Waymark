// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Enums;
using Waymark.Domain.Purchasing;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Supplier"/> to <c>suppliers</c>.
/// </summary>
internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("suppliers", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_suppliers_net_days",
                @"net_days >= 0");
            table.HasCheckConstraint(
                "ck_suppliers_status",
                @"status IN ('active','inactive','blacklisted')");
        });

        builder.HasKey(x => x.SupplierId);

        builder.Property(x => x.SupplierId)
            .HasColumnName("supplier_id");
        builder.Property(x => x.SupplierCode)
            .HasColumnName("supplier_code");
        builder.Property(x => x.CompanyName)
            .HasColumnName("company_name");
        builder.Property(x => x.ResponsibleName)
            .HasColumnName("responsible_name");
        builder.Property(x => x.ContactPhone)
            .HasColumnName("contact_phone");
        builder.Property(x => x.Email)
            .HasColumnName("email");
        builder.Property(x => x.Website)
            .HasColumnName("website");
        builder.Property(x => x.ShippingAddress)
            .HasColumnName("shipping_address");
        builder.Property(x => x.NetDays)
            .HasColumnName("net_days")
            .HasDefaultValue(0L);
        builder.Property(x => x.CreditLimit)
            .HasColumnName("credit_limit");
        builder.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasDefaultValue("DZD");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.SupplierStatusConverter)
            .HasDefaultValue(SupplierStatus.Active);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.SupplierCode).IsUnique();
    }
}
