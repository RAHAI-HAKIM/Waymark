// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Catalogue;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="AttributeOption"/> to <c>attribute_options</c>.
/// </summary>
internal sealed class AttributeOptionConfiguration : IEntityTypeConfiguration<AttributeOption>
{
    public void Configure(EntityTypeBuilder<AttributeOption> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("attribute_options", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_attribute_options_is_active",
                @"is_active IN (0,1)");
        });

        builder.HasKey(x => new { x.AttributeCode, x.OptionCode });

        builder.Property(x => x.AttributeCode)
            .HasColumnName("attribute_code");
        builder.Property(x => x.OptionCode)
            .HasColumnName("option_code");
        builder.Property(x => x.LabelAr)
            .HasColumnName("label_ar");
        builder.Property(x => x.LabelFr)
            .HasColumnName("label_fr");
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

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<AttributeDefinition>()
            .WithMany()
            .HasForeignKey(x => x.AttributeCode)
            .HasPrincipalKey(x => x.AttributeCode)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
