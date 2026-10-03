using Waymark.Domain.Values;

namespace Waymark.Domain.Customers;

/// <summary>A movement of a customer's store credit, as the rule reads it: issued above zero; spent, expired or reversed below.</summary>
public sealed record CreditLine(DateTimeOffset At, Money Amount);

/// <summary>A customer's store credit now.</summary>
/// <param name="Balance">The sum of every movement: what <c>customers.credit</c> holds.</param>
/// <param name="Available">What may be spent: the balance less what has expired and is not yet written off.</param>
/// <param name="Expired">What was issued more than the tenant's days ago and is still unspent: written off as an <c>expire</c> movement when it is next spent.</param>
public sealed record CreditAge(Money Balance, Money Available, Money Expired);

/// <summary>Whether an amount of store credit may be spent, and if not, why.</summary>
public enum RedeemVerdict
{
    Accepted,

    /// <summary>Zero or less: nothing is spent.</summary>
    NotAboveZero,

    /// <summary>More than the credit available: "PLUS QUE L'AVOIR".</summary>
    AboveAvailable,
}

/// <summary>
/// <b>Session B9b</b> A customer's store credit (D-101): what is available, what has expired, and
/// whether an amount may be spent. Pure and in Domain, like <see cref="Tab"/>: the server asks it
/// before a sale spends credit, and the till asks it to say why before the cashier presses anything.
///
/// <para><b>The rules <c>StoreCreditTests</c> hold you to:</b></para>
/// <list type="number">
///   <item><description><b><see cref="Age"/>: the balance is the sum of every movement</b>, never a stored
///   figure. <b>Spending uses the oldest credit first</b>, as repayments pay the oldest charges on the tab
///   (<see cref="Tab.Age"/>): walk the movements in time order (ties in the order given); a positive one
///   is credit issued on its date; a negative one takes from what is oldest. With <c>expiryDays</c> set,
///   credit issued <b>more than</b> that many days (of 24 hours) before <c>now</c> and still unspent is
///   <see cref="CreditAge.Expired"/>; exactly that many is not. <see cref="CreditAge.Available"/> is the
///   balance less what has expired. With no expiry (null), nothing expires. No movement: zero in
///   <paramref name="currency"/>, all three.</description></item>
///   <item><description><b><see cref="MayRedeem"/>:</b> zero or less is
///   <see cref="RedeemVerdict.NotAboveZero"/>; more than what is available is
///   <see cref="RedeemVerdict.AboveAvailable"/>; all of it is fine.</description></item>
///   <item><description>An expiry of zero days or less throws; figures in different currencies throw,
///   as <see cref="Money"/> always does.</description></item>
/// </list>
/// </summary>
public static class StoreCredit
{
    /// <param name="currency">The ledger's currency, for a customer with no movement.</param>
    /// <param name="movements">Every <c>credit_movements</c> row of the customer, at any store of the tenant.</param>
    /// <param name="expiryDays">The tenant's <c>credit_expiry_days</c>; null when credit never expires.</param>
    public static CreditAge Age(Currency currency, IReadOnlyList<CreditLine> movements, DateTimeOffset now, int? expiryDays)
    {
        if (expiryDays is not null && expiryDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expiryDays));
        }
        Money Balance = Money.Zero(currency);
        var queue = new List<(DateTimeOffset At, Money Left)>();
        foreach(var m in movements.OrderBy(m => m.At))
        {
            Balance += m.Amount;

            if (m.Amount.IsPositive)
            {
                queue.Add((m.At, m.Amount));
                continue;
            }
            var ToSpend = -m.Amount;
            for (var i = 0; i < queue.Count && ToSpend.IsPositive; i++)
            {
                var (at, left) = queue[i];
                var used = Smaller(ToSpend, left);

                ToSpend -= used;
                left -= used;

                if (left.IsPositive)
                {
                    queue[i] = (at, left);
                }
                else
                {
                    queue.RemoveAt(i);
                    i--;
                }
            }
        }
        var Available = Money.Zero(currency);
        var Expired = Money.Zero(currency);

        if (expiryDays is not null)
        {
            foreach (var (at, left) in queue)
            {
                if (now > at.AddDays(expiryDays.Value))
                {
                    Expired += left;
                }
                else
                {
                    Available += left;
                }
            }
        }
        else
        {
            foreach (var (_, left) in queue)
            {
                Available += left;
            }
        }
        return new CreditAge(Balance, Available, Expired);
    }

    /// <param name="available">What may be spent: <see cref="CreditAge.Available"/>.</param>
    /// <param name="amount">What the ticket would spend.</param>
    public static RedeemVerdict MayRedeem(Money available, Money amount)
    {
        if (amount <= Money.Zero(amount.Currency))
        {
            return RedeemVerdict.NotAboveZero;
        }

        if (amount > available)
        {
            return RedeemVerdict.AboveAvailable;
        }

        return RedeemVerdict.Accepted;
    }

    private static Money Smaller(Money a, Money b) => a < b ? a : b;

}
