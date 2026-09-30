using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Customers;
using Waymark.Domain.Enums;
using Waymark.Domain.Ledgers;
using Waymark.Domain.Values;

namespace Waymark.Persistence.Customers;

/// <summary>The customers and their tabs (B7, D-096). Reads only what a command asks for; new rows go through staging.</summary>
public sealed class CustomerLedger(WaymarkDbContext context) : ICustomerLedger
{
    public async Task<IReadOnlyList<Customer>> FindByPhoneAsync(string normalisedPhone, CancellationToken cancellationToken = default) =>
        await context.Customers.AsNoTracking()
            .Where(customer => customer.ContactPhone == normalisedPhone && customer.Status == CustomerStatus.Active)
            .OrderBy(customer => customer.CustomerName)
            .ToListAsync(cancellationToken);

    public async Task<Customer?> FindAsync(string customerId, CancellationToken cancellationToken = default) =>
        await context.Customers.AsNoTracking()
            .FirstOrDefaultAsync(customer => customer.CustomerId == customerId && customer.Status != CustomerStatus.Erased, cancellationToken);

    /// <summary>This store's movements only: <c>receivable_movements</c> is behind the store filter (CLAUDE.md §3.3).</summary>
    public async Task<IReadOnlyList<ReceivableMovement>> MovementsAsync(string customerId, CancellationToken cancellationToken = default)
    {
        var movements = await context.ReceivableMovements.AsNoTracking()
            .Where(movement => movement.CustomerId == customerId)
            .ToListAsync(cancellationToken);

        // Ordered here, not in SQL: occurred_at is text through a converter, and the order is the instant's.
        return [.. movements.OrderBy(movement => movement.OccurredAt)];
    }

    public async Task<string?> NoticeInForceAsync(NoticeType type, DateOnly day, CancellationToken cancellationToken = default)
    {
        // effective_from and effective_to are ISO dates, so text order is date order.
        var on = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return await context.NoticeVersions.AsNoTracking()
            .Where(notice => notice.NoticeType == type
                && string.Compare(notice.EffectiveFrom, on) <= 0
                && (notice.EffectiveTo == null || string.Compare(notice.EffectiveTo, on) > 0))
            .OrderByDescending(notice => notice.EffectiveFrom)
            .ThenByDescending(notice => notice.VersionCode)
            .Select(notice => notice.VersionCode)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> StageTabAsync(string customerId, Money? limit, DateTimeOffset? frozenAt, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var customer = await context.Customers.FirstOrDefaultAsync(row => row.CustomerId == customerId, cancellationToken);
        if (customer is null)
        {
            return false;
        }

        // Init-only properties: the change goes through the tracked entry (StoreSettings). Written
        // when the executor commits, with its credit_limit_events row (D-050).
        var entry = context.Entry(customer);
        entry.Property(row => row.CreditLimit).CurrentValue = limit;
        entry.Property(row => row.TabFrozenAt).CurrentValue = frozenAt;
        entry.Property(row => row.UpdatedAt).CurrentValue = at;
        return true;
    }
}
