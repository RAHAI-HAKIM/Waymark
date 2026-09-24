using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;

namespace Waymark.Persistence.Organisation;

/// <summary>
/// <see cref="ITillDirectory"/> over the store database (session A4).
///
/// <para>
/// <c>stores</c>, <c>terminals</c> and <c>staff</c> are all store-scoped, so the global filter
/// narrows each of these reads to the current store without this class naming it. <c>roles</c>
/// is the tenant's vocabulary and is read unfiltered (<c>ParentScopeTests</c>).
/// </para>
/// </summary>
public sealed class TillDirectory(WaymarkDbContext context) : ITillDirectory
{
    public async Task<TillDescription?> DescribeAsync(
        string terminalId, string? staffId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalId);

        var terminal = await context.Terminals
            .Where(t => t.TerminalId == terminalId)
            .Select(t => new { t.TerminalName, t.StoreId })
            .FirstOrDefaultAsync(cancellationToken);

        if (terminal is null)
        {
            return null;
        }

        // Single, not FirstOrDefault: the terminal's foreign key guarantees its store exists, and
        // the filter lets through only this store, which is the terminal's.
        var store = await context.Stores
            .Where(s => s.StoreId == terminal.StoreId)
            .Select(s => new { s.StoreName, s.Currency })
            .SingleAsync(cancellationToken);

        return new TillDescription(
            store.StoreName, terminal.TerminalName, store.Currency, await StaffAsync(staffId, cancellationToken));
    }

    private async Task<StaffDescription?> StaffAsync(string? staffId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(staffId))
        {
            return null;
        }

        // Active staff only, and an active role only: the same people RecommendationBoard.StaffAsync
        // gives a rank to. Anyone else is shown as nobody rather than with a stale title.
        var person = await context.Staff
            .Where(member => member.StaffId == staffId && member.Status == StaffStatus.Active)
            .Select(member => new { member.StaffName, member.Role })
            .FirstOrDefaultAsync(cancellationToken);

        if (person is null)
        {
            return null;
        }

        var role = await context.Roles
            .Where(r => r.RoleCode == person.Role && r.IsActive)
            .Select(r => new { r.LabelFr, r.LabelAr })
            .FirstOrDefaultAsync(cancellationToken);

        return role is null ? null : new StaffDescription(person.StaffName, role.LabelFr, role.LabelAr);
    }
}
