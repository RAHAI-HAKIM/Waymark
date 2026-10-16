using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Enums;
using Waymark.Domain.Sales;

namespace Waymark.Persistence.Sales;

/// <summary><see cref="IReprints"/> over the store database (D3, D-127).</summary>
public sealed class Reprints(WaymarkDbContext context) : IReprints
{
    private static readonly TransactionStatus[] Printed =
        [TransactionStatus.Completed, TransactionStatus.Refunded, TransactionStatus.PartiallyRefunded];

    public async Task<long?> StageReprintAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        var ticket = await context.Transactions.FirstOrDefaultAsync(
            t => t.TransactionId == transactionId && Printed.Contains(t.Status) && t.InvoiceNumber != null, cancellationToken);
        if (ticket is null)
        {
            return null;
        }

        // Init-only properties, so the change goes through the tracked entry, as StoreSettings does.
        // The row's other columns are untouched: a reprint changes nothing the ticket says.
        var count = ticket.ReprintCount + 1;
        context.Entry(ticket).Property(row => row.ReprintCount).CurrentValue = count;
        return count;
    }
}
