using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Sync;

namespace Waymark.Persistence.Sync;

/// <summary>
/// <see cref="IOutboxSequence"/> over the store database. The outbox belongs to the store
/// database rather than to a store, so the counter is the database's (sync-design §2.2).
///
/// <para>
/// Read inside the emitting command's transaction, so the number a failed command would have
/// used is never spent. Two commands racing would read the same last number; StoreServer
/// runs one sale at a time (D-070), and the drain refuses a duplicate sequence.
/// </para>
/// </summary>
public sealed class OutboxSequence(WaymarkDbContext context) : IOutboxSequence
{
    public async Task<long> LastAsync(CancellationToken cancellationToken = default) =>
        await context.Outbox.MaxAsync(message => (long?)message.SequenceNumber, cancellationToken) ?? 0;
}
