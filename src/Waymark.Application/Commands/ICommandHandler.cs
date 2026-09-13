namespace Waymark.Application.Commands;

/// <summary>
/// What carries out one command.
///
/// <para>
/// <b>A handler does not commit.</b> It stages work and returns; the executor
/// commits once, in one transaction. That is CLAUDE.md §3.6 — the domain row
/// and the outbox row go together, both or neither — and it is also what makes
/// §3.2's "mint every id for the whole unit of work before anything is written"
/// true by construction: nothing is written while the handler runs, so every id
/// it minted came first.
/// </para>
/// <para>
/// A handler receives its dependencies through its constructor, like anything
/// else. It receives its <see cref="CommandContext"/> as an argument rather than
/// as a field, because a context belongs to one execution and a field would
/// outlive it — which is exactly how an id gets minted for the wrong unit of
/// work.
/// </para>
/// </summary>
/// <typeparam name="TCommand">The command this handles.</typeparam>
/// <typeparam name="TResult">What it answers with.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>
    /// Carries out the command. Throwing rolls the whole unit of work back,
    /// including the <c>processing_log</c> entries staged through
    /// <paramref name="context"/>.
    /// </summary>
    Task<TResult> HandleAsync(
        TCommand command,
        CommandContext context,
        CancellationToken cancellationToken = default);
}
