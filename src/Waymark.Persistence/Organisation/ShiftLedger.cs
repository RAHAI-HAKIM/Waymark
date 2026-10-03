using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;

namespace Waymark.Persistence.Organisation;

/// <summary><see cref="IShiftLedger"/> over the store database (B10, D-102). Never names the store: <c>shifts</c> is filtered.</summary>
public sealed class ShiftLedger(WaymarkDbContext context) : IShiftLedger
{
    /// <summary>How <c>shifts.start_time</c> and <c>end_time</c> are written: UTC, to the second, as the generator writes them.</summary>
    public static string Timestamp(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public Task<Shift?> OpenShiftAsync(string staffId, CancellationToken cancellationToken = default) =>
        // The text sorts as the instant: one format, to the second.
        context.Shifts
            .Where(shift => shift.StaffId == staffId && shift.Status == ShiftStatus.Open)
            .OrderByDescending(shift => shift.StartTime)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task StageCloseAsync(string shiftId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var shift = await context.Shifts.FirstAsync(row => row.ShiftId == shiftId, cancellationToken);

        // Init-only properties: the change goes through the tracked entry, as StoreSettings does.
        var entry = context.Entry(shift);
        entry.Property(row => row.EndTime).CurrentValue = Timestamp(at);
        entry.Property(row => row.Status).CurrentValue = ShiftStatus.Closed;
        entry.Property(row => row.UpdatedAt).CurrentValue = at;
    }
}
