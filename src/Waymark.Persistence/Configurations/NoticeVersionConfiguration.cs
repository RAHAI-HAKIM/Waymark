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
/// Maps <see cref="NoticeVersion"/> to <c>notice_versions</c>.
/// </summary>
internal sealed class NoticeVersionConfiguration : IEntityTypeConfiguration<NoticeVersion>
{
    public void Configure(EntityTypeBuilder<NoticeVersion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("notice_versions", table =>
        {
            // Declared here as well as in the schema: an EF table rebuild
            // recreates the table from the model alone and drops every CHECK
            // it does not know about (decisions.md D-022).
            table.HasCheckConstraint(
                "ck_notice_versions_notice_type",
                @"notice_type IN ('processing','marketing','staff')");
            table.HasCheckConstraint(
                "ck_notice_versions_language",
                @"language IN ('ar','fr','en')");
            table.HasCheckConstraint(
                "ck_notice_versions_effective_to",
                @"effective_to IS NULL OR effective_to > effective_from");
        });

        builder.HasKey(x => x.VersionCode);

        builder.Property(x => x.VersionCode)
            .HasColumnName("version_code");
        builder.Property(x => x.NoticeType)
            .HasColumnName("notice_type")
            .HasConversion(EnumConverters.NoticeTypeConverter);
        builder.Property(x => x.Language)
            .HasColumnName("language")
            .HasConversion(EnumConverters.LanguageConverter);
        builder.Property(x => x.BodyText)
            .HasColumnName("body_text");
        builder.Property(x => x.EffectiveFrom)
            .HasColumnName("effective_from");
        builder.Property(x => x.EffectiveTo)
            .HasColumnName("effective_to");
        builder.Property(x => x.PublishedAt)
            .HasColumnName("published_at")
            .HasConversion(WaymarkConverters.Timestamp);
    }
}
