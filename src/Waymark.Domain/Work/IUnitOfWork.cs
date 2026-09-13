namespace Waymark.Domain.Work;

/// <summary>
/// One transaction, and the only thing that writes.
///
/// <para>
/// <b>Handlers do not save.</b> They stage work and return; the executor
/// commits once. That inversion is what makes CLAUDE.md §3.2's "command
/// handlers mint every id for the whole unit of work before anything is
/// written" true by construction rather than by discipline — nothing is written
/// until the handler has finished, so every id it minted necessarily came
/// first.
/// </para>
/// <para>
/// It is also §3.6: the domain row and the outbox row go in one transaction,
/// both or neither, and the whole sync design rests on it. D-045 puts the
/// <c>processing_log</c> row in the same transaction for the same reason.
/// </para>
/// <para>
/// Declared in Domain and implemented in <c>Waymark.Persistence</c>. Domain
/// states that something must make writes atomic and does not care what does.
/// </para>
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Commits everything staged since the last commit, in one transaction.
    /// </summary>
    /// <returns>How many rows were written.</returns>
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
