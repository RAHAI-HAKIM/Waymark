using Waymark.Contracts;
using Waymark.Contracts.Sync;
using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Application.Sync;

/// <summary>One sold line, as the basket record sees it: a product, how much, and what it came to.</summary>
public readonly record struct SoldLine(string ProductId, Quantity Quantity, Money LineTotal);

/// <summary>
/// The anonymous basket a sale emits (D-043, D-064, hop 3). <b>The coarsening happens here,
/// at emit</b>, because after an erasure it is no longer known which rows would need fixing.
///
/// <para>
/// What the record must not carry is the point of it: no customer (there is no column), and
/// none of the sale's own identifiers — not the transaction, the invoice, the staff member,
/// the terminal or the cash session. The basket id is fresh and opaque, so nothing joins the
/// record back to the till's own row. The time is the store's hour, never the second: an
/// exact second with a basket's contents identifies whoever was in the shop then.
/// </para>
/// <para>
/// Lines are at <b>product</b> grain, one level coarser than the variant sold, and the two
/// figures cross as exact decimal text (D-044). A sale that took a product from two batches
/// is one line here: the batches are the store's business, not the cloud's.
/// </para>
/// </summary>
public static class AnonymousBasket
{
    /// <summary>What the outbox row calls this message.</summary>
    public const string MessageType = "anonymous_basket";

    /// <summary>The payment classes of D-043: a method, never an instrument.</summary>
    public const string Cash = "cash";

    /// <summary>A ticket paid in more than one way.</summary>
    public const string Mixed = "mixed";

    /// <summary>A ticket that took no payment: everything on it was given away.</summary>
    public const string None = "none";

    /// <summary>
    /// The class of a sale's payment rows: its method when there is one, <see cref="Mixed"/> for
    /// several, <see cref="None"/> for a ticket that took no payment. <c>on_account</c> is a method,
    /// not a debt (D-043). The till wrote <see cref="Cash"/> on every basket, whatever was paid
    /// (block B review); the generator and the till now ask this one function.
    /// </summary>
    public static string PaymentClassOf(IEnumerable<PaymentMethod> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        var distinct = methods.Distinct().ToList();
        return distinct.Count switch
        {
            0 => None,
            1 => distinct[0] switch
            {
                PaymentMethod.Cash => Cash,
                PaymentMethod.Card => "card",
                PaymentMethod.MobileWallet => "mobile_wallet",
                PaymentMethod.StoreCredit => "store_credit",
                PaymentMethod.OnAccount => "on_account",
                _ => throw new ArgumentOutOfRangeException(nameof(methods), distinct[0], "A payment method with no class."),
            },
            _ => Mixed,
        };
    }

    public static AnonymousBasketRecord From(
        string basketId,
        string storeId,
        DateOnly date,
        int hourOfDay,
        IEnumerable<SoldLine> lines,
        string paymentClass,
        bool hasDiscount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentClass);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegative(hourOfDay);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hourOfDay, 23);

        var products = lines
            .GroupBy(line => line.ProductId, StringComparer.Ordinal)
            .Select(group => new BasketLine(
                group.Key,
                Figures.Quantity(group.Sum(line => line.Quantity.Thousandths)),
                Figures.Amount(group.Sum(line => line.LineTotal.MinorUnits))))
            .ToList();

        if (products.Count == 0)
        {
            throw new ArgumentException("A basket with no line is not a basket.", nameof(lines));
        }

        return new AnonymousBasketRecord(
            basketId,
            storeId,
            date,
            hourOfDay,
            (int)date.DayOfWeek,
            products,
            paymentClass,
            hasDiscount);
    }
}
