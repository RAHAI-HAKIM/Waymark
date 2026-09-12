using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Waymark.Persistence;

/// <summary>
/// Makes the ledger currency part of what identifies a built model.
///
/// <para>
/// EF Core builds the model once per context type and caches it. The money
/// converters are built from <see cref="Waymark.Domain.ILedgerCurrency"/>, so a
/// model built for a DZD store would be handed to a EUR one and read every
/// money column back in the wrong currency — silently, because the integer is
/// the same and only the label differs.
/// </para>
/// <para>
/// One store per process at Basic tier, so this cannot bite today. It is here
/// because the failure it prevents leaves no trace: no exception, no wrong
/// total, just a number labelled with the wrong currency, which is the shape of
/// bug this codebase spends most of its effort making impossible.
/// </para>
/// </summary>
public sealed class WaymarkModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        ArgumentNullException.ThrowIfNull(context);

        var currency = context is WaymarkDbContext waymark
            ? waymark.LedgerCurrencyCode
            : string.Empty;

        return (context.GetType(), currency, designTime);
    }
}
