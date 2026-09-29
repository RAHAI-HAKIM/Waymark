using Waymark.Domain.Values;

namespace Waymark.Domain.Sales;

/// <summary>A discount given at the counter (B4, D-091): a percent, or an amount of money.</summary>
public abstract record Discount
{
    private Discount()
    {
    }

    /// <summary>"10 %": a rate in basis points, 1000 is 10 %.</summary>
    public sealed record Percent(BasisPoints Rate) : Discount;

    /// <summary>"50,00 DA" off.</summary>
    public sealed record Amount(Money Value) : Discount;
}

/// <summary>
/// <b>Session B4</b> What a discount given at the counter takes off (D-091).
///
/// <para><b>The rules <c>DiscountsTests</c> hold you to:</b></para>
/// <list type="number">
///   <item><description><b><see cref="OnLine"/></b>: a percent is a derived value, so it is
///   <c>gross.Percent(rate, policy)</c>, rounded <b>once</b> by the store's policy (§3.1). An amount
///   is taken as given. <b>Neither takes more than the line</b>: an amount above the gross is capped
///   at it, and the line comes to zero, never below (<c>ck_transaction_items_discount_amount</c> is
///   <c>&gt;= 0</c>, and a negative line is a refund, which is B9's).</description></item>
///   <item><description><b>Refused</b> (<see cref="ArgumentOutOfRangeException"/>): a percent not
///   above 0 % or above 100 %, and an amount not above zero. A discount of nothing is no discount,
///   and the till must not record one.</description></item>
///   <item><description><b><see cref="OnTicket"/></b>: a ticket discount is worked out <b>once, on
///   the ticket's total</b> (the lines after their own discounts), never line by line: 10 % of three
///   lines of 0,05 is 0,015, rounded once, not three halves of a centime rounded three times. An
///   amount is capped at the total. It is then <b>split over the lines with
///   <see cref="Money.Allocate(IReadOnlyList{long})"/></b>, weighted by each line's total, so the
///   shares sum to the discount exactly and a line at zero takes nothing (§3.1: never round the
///   parts on their own). A ticket that comes to zero takes zero from every line. One share per
///   line, in the lines' order.</description></item>
///   <item><description><b><see cref="Spread"/></b>: a line's discount over the batch rows the sale
///   wrote for it (D-070), weighted by each row's gross, again with <c>Allocate</c>; a discount of
///   zero is zero on every row.</description></item>
/// </list>
/// <para>
/// Pure, like <see cref="SaleArithmetic"/>: the till previews a discount with these same lines and
/// the store's policy, and the server charges it with them, so the two cannot disagree.
/// </para>
/// </summary>
public static class Discounts
{
    /// <param name="gross">The line before any discount: price × quantity, rounded once.</param>
    /// <param name="discount">The discount given on it.</param>
    /// <param name="policy">The store's rounding policy.</param>
    /// <returns>What comes off the line, from zero to <paramref name="gross"/>.</returns>
    public static Money OnLine(Money gross, Discount discount, Rounding policy)
    {
        ArgumentNullException.ThrowIfNull(discount);

        // A discount of nothing is no discount: refused, never recorded. BasisPoints already refuses
        // a negative rate and one above 100 %.
        var off = discount switch
        {
            Discount.Percent { Rate.Value: > 0 } p => gross.Percent(p.Rate, policy),
            Discount.Amount { Value.IsPositive: true } a => a.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(discount), discount, "A discount takes more than nothing off."),
        };

        // Never more than the line: it comes to zero, never below.
        return off > gross ? gross : off;
    }

    /// <param name="lineTotals">Each line's total after its own discount, in the ticket's order.</param>
    /// <param name="discount">The discount given on the whole ticket.</param>
    /// <param name="policy">The store's rounding policy.</param>
    /// <returns>Each line's share of it, in the same order, summing to the ticket's discount.</returns>
    public static IReadOnlyList<Money> OnTicket(IReadOnlyList<Money> lineTotals, Discount discount, Rounding policy)
    {
        ArgumentNullException.ThrowIfNull(lineTotals);
        if (lineTotals.Count == 0)
        {
            return [];
        }

        // Worked out once, on the total: the same rule as a line, so the same refusals and cap.
        var ticketTotal = lineTotals.Aggregate(Money.Add);
        var ticketDiscount = OnLine(ticketTotal, discount, policy);

        // A ticket at zero, or a percent that rounds to nothing: every line takes zero. Allocate
        // cannot weigh by totals that are all zero.
        return ticketDiscount.IsZero
            ? [.. lineTotals.Select(_ => Money.Zero(ticketTotal.Currency))]
            : ticketDiscount.Allocate([.. lineTotals.Select(line => line.MinorUnits)]);
    }

    /// <param name="discount">One line's discount, its own and its share of the ticket's.</param>
    /// <param name="rowGross">The gross of each batch row the sale wrote for the line.</param>
    /// <returns>Each row's part, in the same order, summing to <paramref name="discount"/>.</returns>
    public static IReadOnlyList<Money> Spread(Money discount, IReadOnlyList<Money> rowGross)
    {
        ArgumentNullException.ThrowIfNull(rowGross);

        // Nothing off is zero on every row, and never asks Allocate to weigh anything.
        return discount.IsZero
            ? [.. rowGross.Select(_ => Money.Zero(discount.Currency))]
            : discount.Allocate([.. rowGross.Select(row => row.MinorUnits)]);
    }
}
