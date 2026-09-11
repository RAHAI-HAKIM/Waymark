using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="RoundingVariance"/> to <c>rounding_variance</c>.
/// </summary>
internal sealed class RoundingVarianceConfiguration : IEntityTypeConfiguration<RoundingVariance>
{
    public void Configure(EntityTypeBuilder<RoundingVariance> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("rounding_variance", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            //
            // The values are quoted. Unquoted words in an IN list are column
            // names to SQLite, not strings, and the table would fail to create.
            table.HasCheckConstraint(
                "ck_rounding_variance_reference_type",
                @"reference_type IN ('transaction','purchase_order','batch')");
            table.HasCheckConstraint(
                "ck_rounding_variance_source",
                @"source IN ('cash_tender','currency_conversion')");
            table.HasCheckConstraint(
                "ck_rounding_variance_policy",
                @"policy IN ('half_even','half_up')");

            // A variance of nothing is not a rounding event. Writing one would
            // mean the caller could not tell whether rounding happened.
            table.HasCheckConstraint(
                "ck_rounding_variance_amount",
                @"amount <> 0");
        });

        builder.HasKey(x => x.VarianceId);

        builder.Property(x => x.VarianceId)
            .HasColumnName("variance_id");
        builder.Property(x => x.StoreId)
            .HasColumnName("store_id");
        builder.Property(x => x.OccurredAt)
            .HasColumnName("occurred_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.ReferenceType)
            .HasColumnName("reference_type")
            .HasConversion(EnumConverters.VarianceReferenceTypeConverter);
        builder.Property(x => x.ReferenceId)
            .HasColumnName("reference_id");
        builder.Property(x => x.Source)
            .HasColumnName("source")
            .HasConversion(EnumConverters.VarianceSourceConverter);
        builder.Property(x => x.Amount)
            .HasColumnName("amount");
        builder.Property(x => x.Policy)
            .HasColumnName("policy")
            .HasConversion(EnumConverters.RoundingConverter);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // A rebuild recreates only the indexes the model declares.
        //
        // The first answers "what is the net rounding for this cash session, or
        // this day" — the reconciliation D-034 exists for. The second is the
        // polymorphic lookup, and it is what a reference with no foreign key
        // gets instead: the same shape as ix_movements_reference.
        builder.HasIndex(x => new { x.StoreId, x.OccurredAt })
            .HasDatabaseName("ix_rounding_variance_store_date");
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId })
            .HasDatabaseName("ix_rounding_variance_reference");

        // Foreign keys are dropped by a rebuild too, for the same reason.
        // store_id is the only one: reference_id points at three different
        // tables depending on reference_type, which SQLite cannot express.
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .HasPrincipalKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
