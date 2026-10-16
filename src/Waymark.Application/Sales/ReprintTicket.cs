using Waymark.Application.Commands;
using Waymark.Domain.Sales;

namespace Waymark.Application.Sales;

/// <summary>A reprint refused: the store has no such numbered ticket. Nothing is written.</summary>
public sealed class ReprintRefusedException(string reason) : Exception(reason);

/// <summary>
/// One more copy of a ticket (D3, D-127): its count goes up by one, nothing else of it changes. Who
/// may is the host's to ask, the rule that opens the ticket (D-088, D-109).
/// </summary>
public sealed record ReprintTicket(string TransactionId) : ICommand<long>;

public sealed class ReprintTicketHandler(IReprints reprints) : ICommandHandler<ReprintTicket, long>
{
    public async Task<long> HandleAsync(ReprintTicket command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await reprints.StageReprintAsync(command.TransactionId, cancellationToken)
            ?? throw new ReprintRefusedException("This store has no such ticket.");
    }
}
