using Waymark.Application.Commands;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Organisation;

namespace Waymark.Application.Organisation;

/// <summary>
/// How this store's scale labels are read (session B3, D-090): StoreServer's <c>--scale-format=</c>
/// switch until H2's store screen.
/// </summary>
/// <param name="Format">A preset's name, a custom mask (<c>MASK;prefixes=…;price=…</c>), or blank for the default.</param>
public sealed record SetScaleLabelFormat(string? Format) : ICommand<ScaleLabelFormat>;

/// <summary>The format cannot be set, for a reason the person setting it is told. Nothing is written.</summary>
public sealed class ScaleFormatRefusedException(string reason) : Exception(reason);

/// <summary>
/// Checks a format and stages it on the store's row; the executor commits it (D-050). <b>Only a
/// format <see cref="ScaleLabelFormat.Parse"/> reads is ever stored</b>, so the lookup can trust
/// what it finds and throw on anything else.
/// </summary>
public sealed class SetScaleLabelFormatHandler(IStoreSettings settings, TimeProvider clock)
    : ICommandHandler<SetScaleLabelFormat, ScaleLabelFormat>
{
    public async Task<ScaleLabelFormat> HandleAsync(
        SetScaleLabelFormat command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ScaleLabelFormat format;
        try
        {
            format = ScaleLabelFormat.Parse(command.Format);
        }
        catch (FormatException refusal)
        {
            throw new ScaleFormatRefusedException(refusal.Message);
        }

        // The default is stored as null, so a store that never chose follows the default.
        var stored = ReferenceEquals(format, ScaleLabelFormat.Default) ? null : format.Name;
        if (!await settings.StageScaleLabelFormatAsync(stored, clock.GetUtcNow(), cancellationToken))
        {
            throw new ScaleFormatRefusedException("This store has no row in stores; it is not commissioned.");
        }

        return format;
    }
}
