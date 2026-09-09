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
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Customer"/> to <c>customers</c>.
/// </summary>
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("customers", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_customers_preferred_language",
                @"preferred_language IN ('ar','fr','en')");
            table.HasCheckConstraint(
                "ck_customers_ecommerce_flag",
                @"ecommerce_flag IN (0,1)");
            table.HasCheckConstraint(
                "ck_customers_legal_basis",
                @"legal_basis IN ('consent','contract','legal_obligation','legitimate_interest')");
            table.HasCheckConstraint(
                "ck_customers_consent_profiling",
                @"consent_profiling IN (0,1)");
            table.HasCheckConstraint(
                "ck_customers_consent_marketing",
                @"consent_marketing IN (0,1)");
            table.HasCheckConstraint(
                "ck_customers_objection_flag",
                @"objection_flag IN (0,1)");
            table.HasCheckConstraint(
                "ck_customers_status",
                @"status IN ('active','inactive','erased')");
            table.HasCheckConstraint(
                "ck_customers_consent_profiling_2",
                @"consent_profiling = 0 OR consent_profiling_at IS NOT NULL");
            table.HasCheckConstraint(
                "ck_customers_consent_marketing_2",
                @"consent_marketing = 0 OR consent_marketing_at IS NOT NULL");
            table.HasCheckConstraint(
                "ck_customers_discount",
                @"discount BETWEEN 0 AND 10000");
        });

        builder.HasKey(x => x.CustomerId);

        builder.Property(x => x.CustomerId)
            .HasColumnName("customer_id");
        builder.Property(x => x.CustomerName)
            .HasColumnName("customer_name");
        builder.Property(x => x.ContactPhone)
            .HasColumnName("contact_phone");
        builder.Property(x => x.Email)
            .HasColumnName("email");
        builder.Property(x => x.PreferredLanguage)
            .HasColumnName("preferred_language")
            .HasConversion(EnumConverters.PreferredLanguageConverter)
            .HasDefaultValue(PreferredLanguage.Ar)
            .HasSentinel(PreferredLanguage.Ar);
        builder.Property(x => x.JoinDate)
            .HasColumnName("join_date")
            .HasConversion(WaymarkConverters.Date);
        builder.Property(x => x.LastOrderDate)
            .HasColumnName("last_order_date")
            .HasConversion(WaymarkConverters.Date);
        builder.Property(x => x.Points)
            .HasColumnName("points")
            .HasDefaultValue(0L)
            .HasSentinel(0L);
        builder.Property(x => x.Credit)
            .HasColumnName("credit")
            .HasDefaultValue(0L)
            .HasSentinel(0L);
        builder.Property(x => x.Discount)
            .HasColumnName("discount")
            .HasDefaultValue(0L)
            .HasSentinel(0L);
        builder.Property(x => x.TierRanking)
            .HasColumnName("tier_ranking");
        builder.Property(x => x.EcommerceFlag)
            .HasColumnName("ecommerce_flag")
            .HasDefaultValue(false)
            .HasSentinel(false);
        builder.Property(x => x.LegalBasis)
            .HasColumnName("legal_basis")
            .HasConversion(EnumConverters.LegalBasisConverter)
            .HasDefaultValue(LegalBasis.Contract)
            .HasSentinel(LegalBasis.Contract);
        builder.Property(x => x.ConsentProfiling)
            .HasColumnName("consent_profiling")
            .HasDefaultValue(false)
            .HasSentinel(false);
        builder.Property(x => x.ConsentProfilingAt)
            .HasColumnName("consent_profiling_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.ConsentProfilingNoticeVersion)
            .HasColumnName("consent_profiling_notice_version");
        builder.Property(x => x.ConsentMarketing)
            .HasColumnName("consent_marketing")
            .HasDefaultValue(false)
            .HasSentinel(false);
        builder.Property(x => x.ConsentMarketingAt)
            .HasColumnName("consent_marketing_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.ConsentMarketingNoticeVersion)
            .HasColumnName("consent_marketing_notice_version");
        builder.Property(x => x.ObjectionFlag)
            .HasColumnName("objection_flag")
            .HasDefaultValue(false)
            .HasSentinel(false);
        builder.Property(x => x.DeletionRequestedAt)
            .HasColumnName("deletion_requested_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.CustomerStatusConverter)
            .HasDefaultValue(CustomerStatus.Active)
            .HasSentinel(CustomerStatus.Active);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        builder.HasIndex(x => x.ContactPhone)
            .HasDatabaseName("ix_customers_phone");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<NoticeVersion>()
            .WithMany()
            .HasForeignKey(x => x.ConsentMarketingNoticeVersion)
            .HasPrincipalKey(x => x.VersionCode)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<NoticeVersion>()
            .WithMany()
            .HasForeignKey(x => x.ConsentProfilingNoticeVersion)
            .HasPrincipalKey(x => x.VersionCode)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
