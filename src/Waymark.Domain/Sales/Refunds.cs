using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Domain.Sales;

/// <summary>
/// One <c>transaction_items</c> row of the sale being refunded, as the refund rule reads it. A line of
/// the ticket can be several rows, one per batch the sale took stock from (D-070).
/// </summary>
/// <param name="Sold">What the row sold: positive, in the line's selling unit.</param>
/// <param name="Returned">What earlier refunds have already brought back of it (<c>returns.quantity_returned</c>), zero when none.</param>
/// <param name="LineTotal">What the row was paid, TTC, after every discount: <c>line_total</c>.</param>
/// <param name="TaxAmount">The TVA in it: <c>tax_amount</c>.</param>
/// <param name="Weighed">Sold by weight (a typed weight or a scale label), never by a count of units.</param>
public sealed record SoldRow(Quantity Sold, Quantity Returned, Money LineTotal, Money TaxAmount, bool Weighed);

/// <summary>Whether a quantity may be brought back from a line, and if not, why.</summary>
public enum ReturnVerdict
{
    Accepted,

    /// <summary>Zero or less: nothing is brought back.</summary>
    NotAboveZero,

    /// <summary>More than the line has left once earlier refunds are taken off.</summary>
    AboveReturnable,

    /// <summary>A weighed line brought back in part, or a counted line in part of a unit.</summary>
    NotWhole,
}

/// <summary>The verdict, and when accepted, what comes back of each row, in the rows' order.</summary>
/// <param name="PerRow">One quantity per row, zero for a row nothing comes back from; empty unless accepted.</param>
public sealed record ReturnTake(ReturnVerdict Verdict, IReadOnlyList<Quantity> PerRow);

/// <summary>What bringing some of a row back gives back: positive amounts, the refund row is their negation.</summary>
/// <param name="Total">TTC, the refund row's <c>line_total</c> negated.</param>
/// <param name="Tax">The TVA in it, the refund row's <c>tax_amount</c> negated.</param>
public sealed record RefundShare(Money Total, Money Tax);

/// <summary>
/// <b>Session B9</b> A refund linked to its sale (D-098): what may come back, what it gives back, how
/// much of it goes back on the tab, and what the sale is called after. Pure and in Domain, like
/// <see cref="Tender"/>: the server asks it before anything is written, and nothing at the till
/// works a refund out on its own.
///
/// <para><b>The rules <c>RefundsTests</c> hold you to:</b></para>
/// <list type="number">
///   <item><description><b><see cref="Take"/>, in this order:</b> zero or less is
///   <see cref="ReturnVerdict.NotAboveZero"/>; more than the line has left (every row's
///   <c>Sold − Returned</c>, summed) is <see cref="ReturnVerdict.AboveReturnable"/>; a weighed line
///   (any of its rows weighed) brought back in anything but <b>all that is left</b>, or a counted line
///   in anything but whole units (a multiple of <see cref="Quantity.Scale"/>), is
///   <see cref="ReturnVerdict.NotWhole"/>. Accepted, the quantity is taken <b>from the first row
///   first</b>, each row giving what it has left before the next is asked, one entry per row in the
///   rows' order, zero for the rows not reached.</description></item>
///   <item><description><b><see cref="Share"/>: never a centime more than was paid.</b> A counted
///   row's total is split over its units with <see cref="Money.Allocate(int)"/> (equal weights,
///   largest remainder, ties to the first units), and so is its TVA; bringing back <c>n</c> units
///   when <c>a</c> already came back gives the parts <c>a</c> to <c>a + n − 1</c>. So any series of
///   refunds of a row sums <b>exactly</b> to its line total and its TVA: 1 000 over 3 units is 334,
///   333, 333, whatever the order the customer brings them back in. A weighed row comes back whole
///   or not at all: its whole total and TVA. Zero or less, more than the row has left, part of a
///   weighed row, or part of a unit, throws: <see cref="Take"/> was asked first.</description></item>
///   <item><description><b><see cref="ToTab"/>: the tab first</b> (D-055). The share of a refund that
///   goes back on the customer's tab, as a negative charge, is the smallest of: the refund; what the
///   sale put on the tab less what earlier refunds of it already took back off; and what the customer
///   owes now. Nothing owed (zero or a credit) takes nothing: the tab is not a place to keep money,
///   store credit is. A refund of zero or less throws; so does a negative figure for the
///   tab.</description></item>
///   <item><description><b>B9b, <see cref="ToCredit"/>: store credit back as store credit</b> (D-101). Of
///   what is left once the tab took its share, the share given back as store credit is the smaller of
///   that rest and what the sale paid in store credit less what earlier refunds of it already gave back.
///   So a refund never turns store credit into cash. A rest of zero gives back nothing; a rest or a
///   figure below zero throws.</description></item>
///   <item><description><b><see cref="StatusAfter"/>:</b> every row with <c>Returned</c> equal to
///   <c>Sold</c> is <see cref="TransactionStatus.Refunded"/>; anything returned and something left is
///   <see cref="TransactionStatus.PartiallyRefunded"/>; nothing returned is
///   <see cref="TransactionStatus.Completed"/>. The rows are read after this refund.</description></item>
///   <item><description><b><see cref="Restocks"/>:</b> back on the shelf only when the cashier says so
///   and the batch has not expired; the batch's last day is still sellable, as <c>BatchAllocation</c> sells it, and the day after is
///   not. A batch with no expiry date never expires.</description></item>
///   <item><description>Figures in different currencies, or quantities in different units, throw, as
///   <see cref="Money"/> and <see cref="Quantity"/> always do.</description></item>
/// </list>
/// </summary>
public static class Refunds
{
    /// <param name="rows">The line's rows, in the order the sale wrote them.</param>
    /// <param name="wanted">What the customer brings back of the line, in its selling unit.</param>
    public static ReturnTake Take(IReadOnlyList<SoldRow> rows, Quantity wanted)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfZero(rows.Count);

        // What each row has left. Quantity - Quantity is a delta, and comparing quantities in two
        // units throws, as it should: a line is in one unit.
        var unit = rows[0].Sold.Unit!;
        var left = rows.Select(row => (row.Sold - row.Returned).Thousandths).ToList();
        var total = Quantity.FromThousandths(left.Sum(), unit);

        if (!wanted.IsPositive)
        {
            return new ReturnTake(ReturnVerdict.NotAboveZero, []);
        }

        if (wanted > total)
        {
            return new ReturnTake(ReturnVerdict.AboveReturnable, []);
        }

        var whole = rows.Any(row => row.Weighed)
            ? wanted == total
            : wanted.Thousandths % Quantity.Scale == 0;
        if (!whole)
        {
            return new ReturnTake(ReturnVerdict.NotWhole, []);
        }

        // First row first: each gives what it has left before the next is asked.
        var still = wanted.Thousandths;
        var perRow = new List<Quantity>(rows.Count);
        foreach (var available in left)
        {
            var taken = Math.Min(still, available);
            perRow.Add(Quantity.FromThousandths(taken, unit));
            still -= taken;
        }

        return new ReturnTake(ReturnVerdict.Accepted, perRow);
    }

    /// <param name="row">The row, with what earlier refunds brought back of it.</param>
    /// <param name="returning">What this refund brings back of it: one of <see cref="Take"/>'s entries, above zero.</param>
    public static RefundShare Share(SoldRow row, Quantity returning)
    {
        ArgumentNullException.ThrowIfNull(row);
        var left = row.Sold - row.Returned;
        if (!returning.IsPositive || returning.Thousandths > left.Thousandths)
        {
            throw new ArgumentOutOfRangeException(nameof(returning), returning, "A row gives back above nothing and at most what it has left.");
        }

        // A weighed row comes back whole or not at all: its whole total and TVA.
        if (row.Weighed)
        {
            if (!row.Returned.IsZero || returning != row.Sold)
            {
                throw new ArgumentOutOfRangeException(nameof(returning), returning, "A weighed row comes back whole.");
            }

            return new RefundShare(row.LineTotal, row.TaxAmount);
        }

        if (returning.Thousandths % Quantity.Scale != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(returning), returning, "A counted row comes back in whole units.");
        }

        // The row's total and TVA over its units, largest remainder, ties to the first units; this
        // refund takes the parts after those earlier refunds took, so every series sums to the row.
        var units = checked((int)(row.Sold.Thousandths / Quantity.Scale));
        var before = (int)(row.Returned.Thousandths / Quantity.Scale);
        var count = (int)(returning.Thousandths / Quantity.Scale);
        return new RefundShare(Parts(row.LineTotal, units, before, count), Parts(row.TaxAmount, units, before, count));
    }

    /// <param name="refund">What the refund gives back, above zero.</param>
    /// <param name="tabPaid">What the sale put on the tab: its <c>on_account</c> payment, zero when none.</param>
    /// <param name="tabRefunded">What earlier refunds of the sale already took back off the tab, as a positive figure.</param>
    /// <param name="owed">The customer's tab balance now: the sum of every movement (<see cref="Customers.Tab.Age"/>).</param>
    public static Money ToTab(Money refund, Money tabPaid, Money tabRefunded, Money owed)
    {
        if (!refund.IsPositive)
        {
            throw new ArgumentOutOfRangeException(nameof(refund), refund, "A refund gives back above zero.");
        }

        if (tabPaid.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(tabPaid), tabPaid, "What a sale put on the tab is zero or more.");
        }

        if (tabRefunded.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(tabRefunded), tabRefunded, "What refunds took off the tab is zero or more.");
        }

        // The smallest of the three; Money compares only in one currency, and throws otherwise.
        var share = Smaller(refund, Smaller(tabPaid - tabRefunded, owed));
        var zero = Money.Zero(refund.Currency);
        return share.IsNegative ? zero : share;
    }

    /// <param name="rest">What is left of the refund once the tab took its share; zero or more.</param>
    /// <param name="creditPaid">What the sale paid in store credit: its <c>store_credit</c> payment, zero when none.</param>
    /// <param name="creditRefunded">What earlier refunds of the sale already gave back as store credit for it, as a positive figure.</param>
    public static Money ToCredit(Money rest, Money creditPaid, Money creditRefunded)
    {
        if (rest.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(rest), rest, "What is left of a refund is zero or more.");
        }

        if (creditPaid.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(creditPaid), creditPaid, "What a sale paid in store credit is zero or more.");
        }

        if (creditRefunded.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(creditRefunded), creditRefunded, "What refunds gave back as store credit is zero or more.");
        }

        // The smaller of the rest and the credit share still to give back; never below nothing.
        var share = Smaller(rest, creditPaid - creditRefunded);
        return share.IsNegative ? Money.Zero(rest.Currency) : share;
    }

    /// <param name="rows">Every row of the sale, with <c>Returned</c> counting this refund.</param>
    public static TransactionStatus StatusAfter(IReadOnlyList<SoldRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.All(row => row.Returned == row.Sold))
        {
            return TransactionStatus.Refunded;
        }

        return rows.Any(row => row.Returned.IsPositive) ? TransactionStatus.PartiallyRefunded : TransactionStatus.Completed;
    }

    /// <param name="asked">The cashier's "Remis en rayon".</param>
    /// <param name="expires">The batch's last sellable day; null when it has none.</param>
    /// <param name="today">The store's day.</param>
    public static bool Restocks(bool asked, DateOnly? expires, DateOnly today) =>
        asked && !(expires is { } last && last < today);

    /// <summary>The sum of <paramref name="count"/> parts after the first <paramref name="skip"/>, of <paramref name="amount"/> split over <paramref name="units"/>.</summary>
    private static Money Parts(Money amount, int units, int skip, int count) =>
        amount.Allocate(units).Skip(skip).Take(count).Aggregate(Money.Zero(amount.Currency), (sum, part) => sum + part);

    private static Money Smaller(Money left, Money right) => left < right ? left : right;
}
