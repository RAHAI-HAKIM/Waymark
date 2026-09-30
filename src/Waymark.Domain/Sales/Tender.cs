using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Domain.Sales;

/// <summary>A part of the ticket paid other than in cash (B6): a card, or BaridiMob.</summary>
/// <param name="Reference">What the terminal or the phone showed: an authorisation number, a transfer's id. Optional, and never a card number.</param>
public sealed record TenderPart(PaymentMethod Method, Money Amount, string? Reference = null);

/// <summary>What <see cref="Tender.Settle"/> says of the parts given.</summary>
public enum TenderVerdict
{
    /// <summary>The parts fit the total: the rest, if any, is paid in cash.</summary>
    Settled,

    /// <summary>A part is not a card or a wallet: cash is never a part (it is the rest), and store credit and on-account are not B6's.</summary>
    NotAPart,

    /// <summary>A part of zero or less.</summary>
    NotAboveZero,

    /// <summary>The parts come to more than the total: a card gives no change and no cash back.</summary>
    AboveTotal,
}

/// <summary>How the ticket is paid.</summary>
/// <param name="Payments">
/// The <c>transaction_payments</c> rows, in order: the parts as given, then one cash row for the
/// exact rest when there is one. Empty when refused.
/// </param>
/// <param name="Cash">
/// The cash to collect, rounded to the cash step, and the difference, which goes to
/// <c>rounding_variance</c> (D-034). Zero when there is no cash, and when refused.
/// </param>
public sealed record Settlement(TenderVerdict Verdict, IReadOnlyList<TenderPart> Payments, CashTender Cash)
{
    /// <summary>Whether the sale may be written.</summary>
    public bool Settled => Verdict == TenderVerdict.Settled;
}

/// <summary>
/// <b>Session B6</b> How a ticket is paid: card and BaridiMob parts, and the rest in cash (D-095).
///
/// <para><b>The rules <c>TenderTests</c> hold you to:</b></para>
/// <list type="number">
///   <item><description><b>A part is a card or a wallet</b> (<see cref="PaymentMethod.Card"/>,
///   <see cref="PaymentMethod.MobileWallet"/>). Any other method is <see cref="TenderVerdict.NotAPart"/>:
///   cash is never typed, it is what is left.</description></item>
///   <item><description><b>Each part is above zero</b>, else <see cref="TenderVerdict.NotAboveZero"/>.
///   Each part is checked before the sum is.</description></item>
///   <item><description><b>The parts never come to more than the total</b>, else
///   <see cref="TenderVerdict.AboveTotal"/>: a card pays exact, gives no change and no cash back.
///   Equal is fine: then there is no cash at all.</description></item>
///   <item><description><b>The rest is cash</b>: one cash row of the exact rest, after the parts, in
///   the order given. Only that rest rounds to the cash step (<see cref="Money.ToCashTender"/>), once,
///   whatever order the parts were typed in; the parts are never rounded. A rest of zero writes no
///   cash row, and its <see cref="Settlement.Cash"/> is zero with no variance.</description></item>
///   <item><description>A refusal has no payments and a zero <see cref="Settlement.Cash"/>.</description></item>
///   <item><description>A total below zero throws (a refund is B9's); parts in another currency throw,
///   as <see cref="Money"/> always does.</description></item>
/// </list>
/// <para>
/// Pure and in Domain, like <see cref="PriceOverride"/>: the till asks it as parts are added, and the
/// server asks it again on its own total before anything is written.
/// </para>
/// </summary>
public static class Tender
{
    /// <param name="total">The ticket's exact TTC total, as the server works it out.</param>
    /// <param name="parts">The card and wallet parts, in the order they were added. Empty: all cash.</param>
    public static Settlement Settle(Money total, IReadOnlyList<TenderPart> parts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(total, Money.Zero(total.Currency));
        var PartsTotal = Money.Zero(total.Currency);
        if(parts.Count == 0)
        {
            CashTender OnlyCash = total.ToCashTender();
            IReadOnlyList<TenderPart> Payments = total.IsZero
            ? Array.Empty<TenderPart>()
            : [new TenderPart(PaymentMethod.Cash, total)];

        return new Settlement(TenderVerdict.Settled, Payments, OnlyCash);
        }
        foreach(TenderPart part in parts)
        {
            if (part.Method != PaymentMethod.Card && part.Method != PaymentMethod.MobileWallet)
            {
                return new Settlement(TenderVerdict.NotAPart, Array.Empty<TenderPart>(), new CashTender(Money.Zero(total.Currency), Money.Zero(total.Currency)));
            }
            if (part.Amount <= Money.Zero(part.Amount.Currency))
            {
                return new Settlement(TenderVerdict.NotAboveZero, Array.Empty<TenderPart>(), new CashTender(Money.Zero(total.Currency), Money.Zero(total.Currency)));
            }
            PartsTotal += part.Amount;
        }
        if(PartsTotal > total)
        {
                return new Settlement(TenderVerdict.AboveTotal, Array.Empty<TenderPart>(), new CashTender(Money.Zero(total.Currency), Money.Zero(total.Currency)));
        }
        var rest = total - PartsTotal;
    var cashTender = rest.ToCashTender();

    // Keep the entered card/wallet order; cash, if any, goes last.
    var payments = new List<TenderPart>(parts);
    if (!rest.IsZero)
    {
        payments.Add(new TenderPart(PaymentMethod.Cash, rest));
    }

    return new Settlement(TenderVerdict.Settled, payments, cashTender);
    }
}
