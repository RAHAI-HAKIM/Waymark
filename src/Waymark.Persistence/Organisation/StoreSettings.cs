using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Organisation;

namespace Waymark.Persistence.Organisation;

/// <summary><see cref="IStoreSettings"/> over the store database (session B3, D-090).</summary>
public sealed class StoreSettings(WaymarkDbContext context) : IStoreSettings
{
    public async Task<bool> StageScaleLabelFormatAsync(string? format, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var store = await context.Stores.FirstOrDefaultAsync(cancellationToken);
        if (store is null)
        {
            return false;
        }

        // Init-only properties, so the change goes through the tracked entry, as StaffCredentials
        // does. Written when the executor commits, not here (D-050).
        var entry = context.Entry(store);
        entry.Property(row => row.ScaleLabelFormat).CurrentValue = format;
        entry.Property(row => row.UpdatedAt).CurrentValue = at;
        return true;
    }
}
