namespace Waymark.Domain.Sync;

/// <summary>
/// The outbox's gapless counter (sync-design §2.2). Read inside the command that emits, so
/// the number is taken and used in one transaction: a command that fails uses none, and a
/// gap would look to the cloud like a message it never received.
/// </summary>
public interface IOutboxSequence
{
    /// <summary>The highest sequence number written so far, or 0 when the outbox is empty.</summary>
    Task<long> LastAsync(CancellationToken cancellationToken = default);
}
