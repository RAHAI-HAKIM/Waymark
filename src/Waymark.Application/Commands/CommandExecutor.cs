using Waymark.Domain.Ids;
using Waymark.Domain.Privacy;
using Waymark.Domain.Work;

namespace Waymark.Application.Commands;

/// <summary>
/// Runs one command, in one transaction. The only thing in the system that
/// commits.
///
/// <para>
/// Every rule this scaffolding exists to make mechanical lives in
/// <see cref="ExecuteAsync{TCommand,TResult}"/>, in the order it runs:
/// </para>
/// <list type="number">
/// <item>
/// The handler runs and writes nothing. It stages rows and mints ids through
/// the <see cref="CommandContext"/>. CLAUDE.md §3.2 — every id for the unit of
/// work minted before anything is written — holds because there is no way for
/// the handler to write.
/// </item>
/// <item>
/// The context is sealed. An id or a log entry produced after this point throws
/// rather than attaching to a closed transaction.
/// </item>
/// <item>
/// One commit. The domain rows, the outbox row and the <c>processing_log</c>
/// rows go together or not at all (§3.6, D-045). A handler that threw commits
/// nothing, including its log entries — an operation that rolled back must not
/// leave evidence claiming it happened.
/// </item>
/// </list>
/// <para>
/// <b>It does not decide what to log.</b> A decorator that wrote a
/// <c>processing_log</c> row for every command would have to guess the purpose,
/// the legal basis and the subject, and would write one for commands that touch
/// no personal data at all. D-045 settled those columns from statute; guessing
/// them is what that decision rules out. The handler names the operation, and
/// what makes that structural rather than remembered is that it cannot name a
/// subject without crossing the pseudonymisation boundary to get a
/// <see cref="Pseudonym"/>.
/// </para>
/// </summary>
public sealed class CommandExecutor(
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IProcessingLog processingLog)
{
    /// <summary>
    /// Runs <paramref name="command"/> through <paramref name="handler"/> and
    /// commits once.
    /// </summary>
    /// <returns>Whatever the handler answered with.</returns>
    public async Task<TResult> ExecuteAsync<TCommand, TResult>(
        ICommandHandler<TCommand, TResult> handler,
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(command);

        var context = new CommandContext(ids, processingLog);

        try
        {
            var result = await handler.HandleAsync(command, context, cancellationToken)
                .ConfigureAwait(false);

            context.Seal();

            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);

            return result;
        }
        catch
        {
            // Not committing is not the same as discarding. The staged rows are
            // still held by the unit of work, and the next command's commit
            // would write them — a failed operation's processing_log entry
            // included. Throw them away before the exception leaves.
            context.Seal();
            unitOfWork.Discard();
            throw;
        }
    }
}
