using Waymark.Application.Commands;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;

namespace Waymark.Application.Organisation;

/// <summary>The store's tax regime could not be set; nothing was changed.</summary>
public sealed class TaxRegimeRefusedException(string reason) : Exception(reason);

/// <summary>
/// Says whether this store charges TVA (D-122). Asked when a store is commissioned; the maintenance
/// door until H2's store screen is <c>--tax-regime=</c>. It changes what is sold from now on: a
/// ticket already written keeps the tax its own rows recorded.
/// </summary>
public sealed record SetTaxRegime(TaxRegime Regime) : ICommand<TaxRegime>;

public sealed class SetTaxRegimeHandler(IStoreSettings settings, TimeProvider clock) : ICommandHandler<SetTaxRegime, TaxRegime>
{
    public async Task<TaxRegime> HandleAsync(SetTaxRegime command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Enum.IsDefined(command.Regime))
        {
            throw new TaxRegimeRefusedException("A store is subject to TVA or it is not.");
        }

        return await settings.StageTaxRegimeAsync(command.Regime, clock.GetUtcNow(), cancellationToken)
            ? command.Regime
            : throw new TaxRegimeRefusedException("This store has no row in stores; it is not commissioned.");
    }
}
