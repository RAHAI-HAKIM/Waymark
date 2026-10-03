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

    /// <summary>A part is not a card, a wallet, the tab or store credit: cash is never a part (it is the rest).</summary>
    NotAPart,

    /// <summary>A part of zero or less.</summary>
    NotAboveZero,

    /// <summary>The parts come to more than the total: a card gives no change and no cash back.</summary>
    AboveTotal,

    /// <summary>Two tab parts: a ticket goes on one customer's tab once (B7).</summary>
    TabTwice,

    /// <summary>The tab may not be a part here (<c>tenant_configuration.tab_as_part</c> off): it takes the whole ticket, alone, or nothing (B7).</summary>
    TabNotWhole,

    /// <summary>Two store credit parts: a ticket spends a customer's credit once (B9b).</summary>
    CreditTwice,
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
///   <item><description><b>B7: the tab is a part too</b> (<see cref="PaymentMethod.OnAccount"/>), exact
///   like a card and never rounded. <b>At most one tab part</b>, else <see cref="TenderVerdict.TabTwice"/>.
///   With <c>tabMayBePart</c> false the tab part must be the only part and the whole total, else
///   <see cref="TenderVerdict.TabNotWhole"/>. In order: each part (method, above zero), then
///   <see cref="TenderVerdict.TabTwice"/>, then <see cref="TenderVerdict.AboveTotal"/>, then
///   <see cref="TenderVerdict.TabNotWhole"/>. Whether the customer's tab takes the charge is
///   <see cref="Customers.Tab.Check"/>'s, not this rule's.</description></item>
///   <item><description><b>B9b: store credit is a part too</b> (<see cref="PaymentMethod.StoreCredit"/>), exact
///   like a card. <b>At most one</b>, else <see cref="TenderVerdict.CreditTwice"/>, asked right after
///   <see cref="TenderVerdict.TabTwice"/>. It is a part like the others for <c>tabMayBePart</c>: with the
///   tab whole, credit beside it is <see cref="TenderVerdict.TabNotWhole"/>. Whether the customer has that
///   much credit is <see cref="Customers.StoreCredit.MayRedeem"/>'s, not this rule's.</description></item>
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
    /// <param name="parts">The card, wallet and tab parts, in the order they were added. Empty: all cash.</param>
    /// <param name="tabMayBePart">The tenant's <c>tab_as_part</c> (B7): false, the tab takes a ticket whole or not at all.</param>
    public static Settlement Settle(Money total, IReadOnlyList<TenderPart> parts, bool tabMayBePart = true)
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
            // B7: the tab is a part too, exact like a card. B9b: so is store credit.
            if (part.Method != PaymentMethod.Card && part.Method != PaymentMethod.MobileWallet && part.Method != PaymentMethod.OnAccount
                && part.Method != PaymentMethod.StoreCredit)
            {
                return new Settlement(TenderVerdict.NotAPart, Array.Empty<TenderPart>(), new CashTender(Money.Zero(total.Currency), Money.Zero(total.Currency)));
            }
            if (part.Amount <= Money.Zero(part.Amount.Currency))
            {
                return new Settlement(TenderVerdict.NotAboveZero, Array.Empty<TenderPart>(), new CashTender(Money.Zero(total.Currency), Money.Zero(total.Currency)));
            }
            PartsTotal += part.Amount;
        }
        // B7: a ticket goes on the tab once.
        if (parts.Count(part => part.Method == PaymentMethod.OnAccount) > 1)
        {
            return new Settlement(TenderVerdict.TabTwice, Array.Empty<TenderPart>(), new CashTender(Money.Zero(total.Currency), Money.Zero(total.Currency)));
        }

        // B9b: a ticket spends a customer's store credit once.
        if (parts.Count(part => part.Method == PaymentMethod.StoreCredit) > 1)
        {
            return new Settlement(TenderVerdict.CreditTwice, Array.Empty<TenderPart>(), new CashTender(Money.Zero(total.Currency), Money.Zero(total.Currency)));
        }

        if(PartsTotal > total)
        {
                return new Settlement(TenderVerdict.AboveTotal, Array.Empty<TenderPart>(), new CashTender(Money.Zero(total.Currency), Money.Zero(total.Currency)));
        }
        // B7: with the tab not a part, it takes the whole ticket alone, or nothing.
        if (!tabMayBePart && parts.Any(part => part.Method == PaymentMethod.OnAccount) && (parts.Count != 1 || parts[0].Amount != total))
        {
            return new Settlement(TenderVerdict.TabNotWhole, Array.Empty<TenderPart>(), new CashTender(Money.Zero(total.Currency), Money.Zero(total.Currency)));
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
