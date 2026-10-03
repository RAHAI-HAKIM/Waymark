using System.Globalization;
using Waymark.Domain.Organisation;
using Waymark.Domain.Sales;

namespace Waymark.Application.Sales;

/// <summary>
/// <c>{store code}-{year}-{000001}</c>, gapless per store per calendar year, as the synthetic store
/// numbers them. One place, because a sale and a refund (B9) share the sequence: two copies of "the
/// next number" would one day hand out the same one. Read and staged in the command's own
/// transaction, so a command that fails uses no number; StoreServer runs one at a time, and the unique
/// index on (store, invoice number) refuses a duplicate if two ever raced.
/// </summary>
internal static class InvoiceNumbers
{
    public static async Task<string> NextAsync(ISalesLedger ledger, Store store, DateOnly today, CancellationToken cancellationToken)
    {
        var prefix = string.Create(CultureInfo.InvariantCulture, $"{store.StoreCode}-{today.Year}-");
        var last = await ledger.LastInvoiceNumberAsync(prefix, cancellationToken);
        var next = last is null ? 1 : int.Parse(last.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture) + 1;
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}{next:D6}");
    }
}
