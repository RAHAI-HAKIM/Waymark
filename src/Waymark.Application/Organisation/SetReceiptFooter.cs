using Waymark.Application.Commands;
using Waymark.Domain.Organisation;

namespace Waymark.Application.Organisation;

/// <summary>The store's receipt text could not be set; nothing was changed.</summary>
public sealed class ReceiptFooterRefusedException(string reason) : Exception(reason);

/// <summary>
/// The store's own text at the foot of its receipts (D-125): a thank you, opening hours. Above
/// Waymark's signature, which the store does not write. Blank clears it. The maintenance door until
/// H2's store screen is <c>--receipt-footer=</c>.
/// </summary>
public sealed record SetReceiptFooter(string? Footer) : ICommand<string?>;

public sealed class SetReceiptFooterHandler(IStoreSettings settings, TimeProvider clock) : ICommandHandler<SetReceiptFooter, string?>
{
    public async Task<string?> HandleAsync(SetReceiptFooter command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var footer = string.IsNullOrWhiteSpace(command.Footer) ? null : command.Footer.Trim();
        if (footer is { Length: > Store.ReceiptFooterMaxLength })
        {
            throw new ReceiptFooterRefusedException($"A receipt's text is {Store.ReceiptFooterMaxLength} characters at most.");
        }

        // Text from the store's keyboard reaches the printer: a control character would be a
        // command to it. The encoder prints one as '?', and this refuses it before it is kept.
        if (footer is not null && footer.Any(c => char.IsControl(c) && c != '\n'))
        {
            throw new ReceiptFooterRefusedException("A receipt's text is letters, figures and punctuation; a line break at most.");
        }

        return await settings.StageReceiptFooterAsync(footer, clock.GetUtcNow(), cancellationToken)
            ? footer
            : throw new ReceiptFooterRefusedException("This store has no row in stores; it is not commissioned.");
    }
}
