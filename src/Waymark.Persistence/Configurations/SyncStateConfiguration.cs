// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Waymark.Domain.Sync;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// Maps <see cref="SyncState"/> to <c>sync_state</c>.
/// </summary>
internal sealed class SyncStateConfiguration : IEntityTypeConfiguration<SyncState>
{
    public void Configure(EntityTypeBuilder<SyncState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sync_state");

        builder.HasKey(x => x.StateKey);

        builder.Property(x => x.StateKey)
            .HasColumnName("state_key");
        builder.Property(x => x.StateValue)
            .HasColumnName("state_value");
        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasConversion(WaymarkConverters.Timestamp);
    }
}
