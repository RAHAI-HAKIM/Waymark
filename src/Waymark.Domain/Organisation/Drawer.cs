using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Domain.Organisation;

/// <summary>A <c>rounding_variance</c> row of the session's day, as the rule reads it: what it is about, and how much.</summary>
/// <param name="Reference">What the row points at. Only a ticket's rounding is cash the drawer has not yet been told of.</param>
/// <param name="Amount">Tendered minus exact (<see cref="CashTender.Variance"/>), signed.</param>
public sealed record TenderRounding(VarianceReferenceType Reference, Money Amount);

/// <summary>
/// Everything that moved cash in one cash session, each as a figure of zero or more: the direction is
/// the figure's name, never its sign.
/// </summary>
/// <param name="OpeningFloat">What was counted into the drawer at opening (<c>cash_sessions.opening_float</c>).</param>
/// <param name="CashSales">The cash parts of the session's tickets sold, <b>to the centime</b>, as their payment rows hold them.</param>
/// <param name="CashRefunds">The cash given back by the session's refunds, to the centime, as a figure above zero.</param>
/// <param name="PaidIn">Cash in with no sale (<c>paid_in</c>, <c>float_add</c>), a tab's repayments left out.</param>
/// <param name="TabRepayments">The <c>paid_in</c> rows a tab's repayment points at: <b>already the rounded cash</b> (D-108).</param>
/// <param name="PaidOut">Cash out with no sale (<c>paid_out</c>, <c>float_remove</c>).</param>
/// <param name="Drops">Cash taken to the safe or the bank (<c>drop</c>).</param>
/// <param name="Rounding">Every rounding row of the session, whatever it points at: the rule chooses.</param>
public sealed record DrawerMovements(
    Money OpeningFloat, Money CashSales, Money CashRefunds, Money PaidIn, Money TabRepayments, Money PaidOut, Money Drops,
    IReadOnlyList<TenderRounding> Rounding);

/// <summary>
/// <b>Session C1</b> The drawer (D-034, D-111): what should be in it, what the count says
/// of it, and whether it can give what is asked of it. Pure and in Domain, like <see cref="Customers.Tab"/>:
/// the server asks it when a session closes, and the X and Z reports (C2) ask it again.
///
/// <para><b>The rules <c>DrawerTests</c> hold you to:</b></para>
/// <list type="number">
///   <item><description><b><see cref="Expected"/></b> is the generator's formula (<c>CashDrawer</c>):
///   float + cash sales − cash refunds + paid-in + tab repayments − paid-out − drops + the tender
///   rounding. <b>Only a rounding row about a <see cref="VarianceReferenceType.Transaction"/> is
///   added.</b> A ticket's payment row is exact and the drawer took the rounded tender, so its
///   rounding is cash not yet counted; a tab repayment's <c>paid_in</c> is the rounded cash already
///   (D-108), and adding its rounding row counts those centimes twice. Rows about a purchase order
///   or a batch are not cash at all.</description></item>
///   <item><description>Every figure of <see cref="DrawerMovements"/> but the rounding is zero or
///   more: a negative one throws <see cref="ArgumentOutOfRangeException"/>, because a refund passed
///   as a negative sale would be subtracted twice by whoever fixes it later.</description></item>
///   <item><description><b><see cref="Variance"/> is counted minus expected</b>: a shortage is
///   negative. A count below zero throws. This is the miscount and nothing else: tender rounding
///   never reaches it (D-034).</description></item>
///   <item><description><b><see cref="NeedsNote"/></b>: a variance whose size is <b>more than</b>
///   the tenant's <c>variance_alert_value</c>, short or over, needs a note; exactly the threshold
///   does not. No threshold (null): never. A threshold below zero throws.</description></item>
///   <item><description><b><see cref="Covers"/></b> (F-35): the drawer covers cash going out up to
///   what it should hold, that figure included. An amount of zero or less throws.</description></item>
///   <item><description>Figures in different currencies throw, as <see cref="Money"/> always does.</description></item>
/// </list>
/// </summary>
public static class Drawer
{
    /// <summary>What should physically be in the drawer.</summary>
    public static Money Expected(DrawerMovements movements)
    {
        var z = Money.Zero(movements.OpeningFloat.Currency);
        if (movements.OpeningFloat < z || movements.CashSales < z || movements.CashRefunds < z || movements.Drops < z || movements.PaidIn < z
        || movements.PaidOut < z || movements.TabRepayments < z)
        {
            throw new ArgumentOutOfRangeException(nameof(movements));
        }
        var TotalRounding = Money.Zero(movements.OpeningFloat.Currency);
        foreach (var r in movements.Rounding)
        {
            if(r.Reference == VarianceReferenceType.Transaction) {
                TotalRounding += r.Amount;
            }
        }
        return movements.OpeningFloat + movements.CashSales - movements.CashRefunds + movements.PaidIn - movements.PaidOut + movements.TabRepayments - movements.Drops + TotalRounding;
    }

    /// <param name="expected">As <see cref="Expected"/> says.</param>
    /// <param name="counted">What the person counted, zero or more.</param>
    public static Money Variance(Money expected, Money counted)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(counted, Money.Zero(counted.Currency));
        return counted - expected;
    }

    /// <param name="variance">As <see cref="Variance"/> says.</param>
    /// <param name="threshold">The tenant's <c>variance_alert_value</c>; null: no note is ever asked.</param>
    public static bool NeedsNote(Money variance, Money? threshold)
    {
        if (threshold is null)
        {
            return false;
        }
        if(threshold < Money.Zero(variance.Currency))
        {
            throw new ArgumentOutOfRangeException(nameof(threshold));
        }
        var UnsignedVariance = variance.IsPositive ? variance : -variance;
        if(!(threshold is null) && UnsignedVariance > threshold)
        {
            return true;
        }
        return false;
    }


    /// <param name="expected">What the drawer should hold now.</param>
    /// <param name="goingOut">The cash asked of it, above zero.</param>
    public static bool Covers(Money expected, Money goingOut)
    {
        if(goingOut.IsNegative || goingOut.IsZero)
        {
            throw new ArgumentOutOfRangeException(nameof(goingOut));
        }
        return expected >= goingOut;
    }
}
