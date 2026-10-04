using Waymark.Domain.Values;

namespace Waymark.Domain.Customers;

/// <summary>A movement on the tab as the rule reads it: when, and how much. Positive means the customer owes more (D-055).</summary>
public sealed record TabMovement(DateTimeOffset At, Money Amount);

/// <summary>What the tab comes to, and how old the oldest unpaid charge is.</summary>
/// <param name="Balance">The sum of every movement: what is owed; negative when the customer paid more than they owed.</param>
/// <param name="OldestUnpaid">When the oldest charge not yet paid off was made; null when nothing is owed.</param>
public sealed record TabAge(Money Balance, DateTimeOffset? OldestUnpaid);

/// <summary>What <see cref="Tab.Check"/> says of a new charge.</summary>
public enum TabVerdict
{
    /// <summary>It fits: charge it.</summary>
    Accepted,

    /// <summary>The customer has no limit: never credit by omission (D-055).</summary>
    NoTab,

    /// <summary>The tab is frozen: no new charge, whatever the limit.</summary>
    Frozen,

    /// <summary>The oldest unpaid charge is older than the store allows: something is repaid first.</summary>
    Overdue,

    /// <summary>It would take the balance past the limit. The one refusal an owner may override (D-096).</summary>
    AboveLimit,
}

/// <param name="Available">What may still be charged, the limit less the balance, negative when already past it; null with no tab.</param>
public sealed record TabCheck(TabVerdict Verdict, Money? Available)
{
    public bool MayCharge => Verdict == TabVerdict.Accepted;

    /// <summary>Only a charge past the limit may be let through, by a person with <c>ManageCredit</c>.</summary>
    public bool MayOverride => Verdict == TabVerdict.AboveLimit;
}

/// <summary>What <see cref="Tab.MayLimit"/> says of a limit to set.</summary>
public enum LimitVerdict
{
    Accepted,

    /// <summary>Below zero.</summary>
    Negative,

    /// <summary>Above the tenant's ceiling (<c>tenant_configuration.max_credit_limit</c>).</summary>
    AboveCeiling,
}

/// <summary>What <see cref="Tab.MayRepay"/> says of a repayment.</summary>
public enum RepaymentVerdict
{
    Accepted,

    /// <summary>Zero or less.</summary>
    NotAboveZero,

    /// <summary>More than is owed: the tab is not a place to keep money (store credit is B9's).</summary>
    AboveBalance,

    /// <summary>A part of what is owed that is not a multiple of the cash step: no coin pays it (D-108).</summary>
    NotOnCashStep,
}

/// <summary>A repayment in cash as it is recorded (D-108).</summary>
/// <param name="Cleared">What comes off the tab, to the centime.</param>
/// <param name="Cash">What the drawer takes: the same, or the whole due rounded to the cash step.</param>
public readonly record struct TabRepayment(RepaymentVerdict Verdict, Money Cleared, Money Cash)
{
    public bool Accepted => Verdict == RepaymentVerdict.Accepted;

    /// <summary>What the cash step added or took off: <c>rounding_variance</c>'s, never the drawer's (D-034).</summary>
    public Money Variance => Cash - Cleared;
}

/// <summary>
/// <b>Session B7</b> The tab, le carnet (D-055, D-096): what a customer owes, how old it is, and
/// whether a new charge, a limit or a repayment may go on it. Pure and in Domain, like
/// <see cref="Sales.Tender"/>: the server asks it before anything is written, and the till asks it
/// to say why before the cashier presses anything.
///
/// <para><b>The rules <c>TabTests</c> hold you to:</b></para>
/// <list type="number">
///   <item><description><b><see cref="Age"/>: the balance is the sum of every movement</b>, never a
///   stored figure. <b>Repayments pay the oldest charges first</b>: walk the movements in time order
///   (ties in the order given); a positive movement is owed from its date; a negative one pays off
///   what is oldest. <see cref="TabAge.OldestUnpaid"/> is the date of the oldest charge left unpaid,
///   null when nothing is. Paying more than was owed leaves a credit that pays off the next charges
///   as they come. No movement: a balance of zero in <paramref name="currency"/>.</description></item>
///   <item><description><b><see cref="Check"/>, in this order:</b> no limit is
///   <see cref="TabVerdict.NoTab"/>; frozen is <see cref="TabVerdict.Frozen"/>; with
///   <c>overdueDays</c> set, an oldest unpaid charge <b>more than</b> that many days (of 24 hours)
///   before <c>now</c> is <see cref="TabVerdict.Overdue"/>, exactly that many is not; a balance plus
///   the charge <b>above</b> the limit is <see cref="TabVerdict.AboveLimit"/>, reaching it exactly
///   is fine; else <see cref="TabVerdict.Accepted"/>. <see cref="TabCheck.Available"/> is the limit
///   less the balance, whatever the verdict, and null with no limit. A charge of zero or less
///   throws: a refund is B9's.</description></item>
///   <item><description><b><see cref="MayLimit"/>:</b> no limit (null, the tab closed) is always
///   accepted; below zero is <see cref="LimitVerdict.Negative"/>; above a ceiling, when there is one,
///   is <see cref="LimitVerdict.AboveCeiling"/>, the ceiling itself is fine. A limit below what is
///   already owed is accepted: it only stops new charges.</description></item>
///   <item><description><b><see cref="MayRepay"/>:</b> zero or less is
///   <see cref="RepaymentVerdict.NotAboveZero"/>; more than the balance is
///   <see cref="RepaymentVerdict.AboveBalance"/>; the whole balance is fine.</description></item>
///   <item><description><b><see cref="Repay"/> (D-108):</b> cash moves in steps of
///   <see cref="Currency.CashRoundingStep"/>, a tab is owed to the centime. <b>Paying the whole due
///   clears the tab exactly</b>, and the drawer takes the due rounded to the step, the difference a
///   rounding variance as for a cash sale (D-034): 286,00 owed is cleared by 285,00 in cash. The whole
///   due is named by its exact figure or by its rounded one, so 285,00 typed on 286,00 clears it too,
///   and 290,00 on 288,00. <b>A part of the due is a multiple of the step</b>, cleared and taken as
///   typed; anything else is <see cref="RepaymentVerdict.NotOnCashStep"/>. Zero or less, and more than
///   is owed, as <see cref="MayRepay"/>.</description></item>
///   <item><description>Figures in different currencies throw, as <see cref="Money"/> always does.</description></item>
/// </list>
/// </summary>
public static class Tab
{
    /// <param name="currency">The ledger's currency, for the balance of a tab with no movement.</param>
    /// <param name="movements">Every <c>receivable_movements</c> row of the customer, as a movement.</param>
    public static TabAge Age(Currency currency, IReadOnlyList<TabMovement> movements)
    {
        ArgumentNullException.ThrowIfNull(movements);

        // Nothing is edited: the movements are read, in time order, and the matching happens in
        // this list, which is thrown away after. Each entry is a charge (or a positive adjustment)
        // with what is still unpaid of it, oldest first.
        var unpaid = new List<(DateTimeOffset At, Money Left)>();
        var balance = Money.Zero(currency);

        // What was paid beyond everything owed at the time: it pays the next charges as they come.
        var credit = Money.Zero(currency);

        // Time order; OrderBy is stable, so movements at the same instant keep the order given.
        foreach (var movement in movements.OrderBy(movement => movement.At))
        {
            balance += movement.Amount;

            if (movement.Amount.IsPositive)
            {
                // A charge: first eaten by any credit, and what is left of it is owed from its date.
                var left = movement.Amount;
                var used = Smaller(credit, left);
                credit -= used;
                left -= used;
                if (left.IsPositive)
                {
                    unpaid.Add((movement.At, left));
                }
            }
            else
            {
                // A repayment (or a write-off, a negative adjustment): it pays the oldest charge
                // first, then the next, until it is spent; what is left over becomes credit.
                var pay = -movement.Amount;
                while (pay.IsPositive && unpaid.Count > 0)
                {
                    var (at, left) = unpaid[0];
                    var used = Smaller(pay, left);
                    pay -= used;
                    left -= used;
                    if (left.IsPositive)
                    {
                        unpaid[0] = (at, left);
                    }
                    else
                    {
                        unpaid.RemoveAt(0);
                    }
                }

                credit += pay;
            }
        }

        return new TabAge(balance, unpaid.Count > 0 ? unpaid[0].At : null);
    }

    /// <param name="age">The tab as <see cref="Age"/> reads it.</param>
    /// <param name="limit">The customer's limit; null: no tab.</param>
    /// <param name="frozen">Whether the tab is frozen.</param>
    /// <param name="charge">The new charge, above zero.</param>
    /// <param name="now">When it is charged.</param>
    /// <param name="overdueDays">The tenant's overdue days; null: the rule is off.</param>
    public static TabCheck Check(TabAge age, Money? limit, bool frozen, Money charge, DateTimeOffset now, int? overdueDays)
    {
        ArgumentNullException.ThrowIfNull(age);
        if (!charge.IsPositive)
        {
            throw new ArgumentOutOfRangeException(nameof(charge), charge, "A charge is above zero; a refund is B9's.");
        }

        if (limit is not { } ceiling)
        {
            return new TabCheck(TabVerdict.NoTab, null);
        }

        var available = ceiling - age.Balance;
        if (frozen)
        {
            return new TabCheck(TabVerdict.Frozen, available);
        }

        // More than that many days of 24 hours: exactly that many is not yet overdue.
        if (overdueDays is { } days && age.OldestUnpaid is { } oldest && now - oldest > TimeSpan.FromDays(days))
        {
            return new TabCheck(TabVerdict.Overdue, available);
        }

        // Reaching the limit exactly is fine; a centime past it is not. Money throws on a second currency.
        return new TabCheck(age.Balance + charge > ceiling ? TabVerdict.AboveLimit : TabVerdict.Accepted, available);
    }

    /// <param name="limit">The limit to set; null: no tab.</param>
    /// <param name="ceiling">The tenant's ceiling; null: none.</param>
    public static LimitVerdict MayLimit(Money? limit, Money? ceiling)
    {
        if (limit is not { } set)
        {
            return LimitVerdict.Accepted;
        }

        if (set.IsNegative)
        {
            return LimitVerdict.Negative;
        }

        return ceiling is { } most && set > most ? LimitVerdict.AboveCeiling : LimitVerdict.Accepted;
    }

    public static RepaymentVerdict MayRepay(Money balance, Money amount)
    {
        if (!amount.IsPositive)
        {
            return RepaymentVerdict.NotAboveZero;
        }

        return amount > balance ? RepaymentVerdict.AboveBalance : RepaymentVerdict.Accepted;
    }

    /// <param name="balance">What the customer owes now.</param>
    /// <param name="amount">What is paid, as typed: the exact due, the due as cash rounds it, or a part of it.</param>
    public static TabRepayment Repay(Money balance, Money amount)
    {
        var zero = Money.Zero(amount.Currency);
        if (!amount.IsPositive)
        {
            return new TabRepayment(RepaymentVerdict.NotAboveZero, zero, zero);
        }

        // The whole due, by either of its names: the tab is cleared to the centime, and the cash
        // rounds once. Money throws on a second currency.
        var whole = balance.ToCashTender().Tendered;
        if (balance.IsPositive && (amount == balance || amount == whole))
        {
            return new TabRepayment(RepaymentVerdict.Accepted, balance, whole);
        }

        if (amount > balance)
        {
            return new TabRepayment(RepaymentVerdict.AboveBalance, zero, zero);
        }

        var step = amount.Currency.CashRoundingStep;
        return step > 1 && amount.MinorUnits % step != 0
            ? new TabRepayment(RepaymentVerdict.NotOnCashStep, zero, zero)
            : new TabRepayment(RepaymentVerdict.Accepted, amount, amount);
    }

    private static Money Smaller(Money a, Money b) => a < b ? a : b;
}
