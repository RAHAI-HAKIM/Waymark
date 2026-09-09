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
using Waymark.Domain.Reference;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="AttributeDefinition"/> to <c>attribute_definitions</c>.
/// </summary>
internal sealed class AttributeDefinitionConfiguration : IEntityTypeConfiguration<AttributeDefinition>
{
    public void Configure(EntityTypeBuilder<AttributeDefinition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("attribute_definitions", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_attribute_definitions_data_type",
                @"data_type IN ('text','number','bool','date','enum')");
            table.HasCheckConstraint(
                "ck_attribute_definitions_applies_to",
                @"applies_to IN ('product','variant')");
            table.HasCheckConstraint(
                "ck_attribute_definitions_is_groupable",
                @"is_groupable IN (0,1)");
            table.HasCheckConstraint(
                "ck_attribute_definitions_is_filterable",
                @"is_filterable IN (0,1)");
            table.HasCheckConstraint(
                "ck_attribute_definitions_status",
                @"status IN ('active','archived')");
            table.HasCheckConstraint(
                "ck_attribute_definitions_unit_code",
                @"unit_code IS NULL OR data_type = 'number'");
        });

        builder.HasKey(x => x.AttributeCode);

        builder.Property(x => x.AttributeCode)
            .HasColumnName("attribute_code");
        builder.Property(x => x.LabelAr)
            .HasColumnName("label_ar");
        builder.Property(x => x.LabelFr)
            .HasColumnName("label_fr");
        builder.Property(x => x.LabelEn)
            .HasColumnName("label_en");
        builder.Property(x => x.DataType)
            .HasColumnName("data_type")
            .HasConversion(EnumConverters.AttributeDefinitionDataTypeConverter);
        builder.Property(x => x.UnitCode)
            .HasColumnName("unit_code");
        builder.Property(x => x.AppliesTo)
            .HasColumnName("applies_to")
            .HasConversion(EnumConverters.AttributeDefinitionAppliesToConverter);
        builder.Property(x => x.IsGroupable)
            .HasColumnName("is_groupable")
            .HasDefaultValue(false)
            .HasSentinel(false);
        builder.Property(x => x.IsFilterable)
            .HasColumnName("is_filterable")
            .HasDefaultValue(false)
            .HasSentinel(false);
        builder.Property(x => x.HelpText)
            .HasColumnName("help_text");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(EnumConverters.AttributeDefinitionStatusConverter)
            .HasDefaultValue(AttributeDefinitionStatus.Active)
            .HasSentinel(AttributeDefinitionStatus.Active);
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasConversion(WaymarkConverters.Timestamp);
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);

        // Foreign keys are dropped by a rebuild too, for the same reason.
        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(x => x.UnitCode)
            .HasPrincipalKey(x => x.UnitCode)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
