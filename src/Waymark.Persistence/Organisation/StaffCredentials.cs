using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;

namespace Waymark.Persistence.Organisation;

/// <summary>
/// <see cref="IStaffCredentials"/> over the store database (session A5, D-083).
///
/// <para>
/// <c>staff</c> is store-scoped, so the global filter narrows every read and the tracked row to
/// the current store; <c>roles</c> is the tenant's vocabulary and is read unfiltered, as in
/// <see cref="TillDirectory"/>. The hash leaves this class only through
/// <see cref="PinHashAsync"/>, to the one caller that verifies it.
/// </para>
/// </summary>
public sealed class StaffCredentials(WaymarkDbContext context) : IStaffCredentials
{
    public async Task<IReadOnlyList<SignInCandidate>> CandidatesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (
                from member in context.Staff
                join role in context.Roles on member.Role equals role.RoleCode
                where member.Status == StaffStatus.Active && role.IsActive
                select new { member.StaffId, member.StaffName, role.LabelFr, role.LabelAr, member.PinHash })
            .ToListAsync(cancellationToken);

        // Ordered here rather than in SQL: SQLite's collation is byte order, which sorts "Émilie"
        // after "Zoubir". Ordinal ignoring case is not a French collation either, but the till
        // runs with InvariantGlobalization (D-067) and a stable order is what the list needs.
        return [.. rows
            .OrderBy(row => row.StaffName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.StaffId, StringComparer.Ordinal)
            .Select(row => new SignInCandidate(
                row.StaffId, row.StaffName, row.LabelFr, row.LabelAr, StaffPin.IsUsable(row.PinHash)))];
    }

    public async Task<string?> PinHashAsync(string staffId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(staffId);

        return await (
                from member in context.Staff
                join role in context.Roles on member.Role equals role.RoleCode
                where member.StaffId == staffId && member.Status == StaffStatus.Active && role.IsActive
                select member.PinHash)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> StagePinHashAsync(
        string staffId, string pinHash, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(staffId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pinHash);

        // The same people PinHashAsync reads a hash for: active staff with an active role. With
        // the role left out, a person whose role was retired was given a PIN and told it worked
        // at the next sign-in, which it never could.
        var member = await (
                from row in context.Staff
                join role in context.Roles on row.Role equals role.RoleCode
                where row.StaffId == staffId && row.Status == StaffStatus.Active && role.IsActive
                select row)
            .FirstOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            return false;
        }

        // Init-only properties, so the change goes through the tracked entry, as the board's
        // MarkDecided does. Written when the executor commits, not here (D-050).
        var entry = context.Entry(member);
        entry.Property(row => row.PinHash).CurrentValue = pinHash;
        entry.Property(row => row.UpdatedAt).CurrentValue = at;
        return true;
    }
}
