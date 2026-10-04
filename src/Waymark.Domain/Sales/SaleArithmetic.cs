using Waymark.Domain.Values;

namespace Waymark.Domain.Sales;

/// <summary>What one sale line comes to.</summary>
/// <param name="Gross">Sell price times quantity, rounded once.</param>
/// <param name="LineTotal">The TTC line total after discount: <c>transaction_items.line_total</c>.</param>
/// <param name="Split">That total separated into HT and TVA; the TVA is <c>tax_amount</c>.</param>
public readonly record struct LineAmounts(Money Gross, Money LineTotal, TaxSplit Split);

/// <summary>
/// The sale arithmetic, written once (D-033, D-034): the till's <c>CompleteSale</c> and the
/// synthetic store generator both call this, so a generated receipt and a real one are
/// computed by the same lines.
///
/// <para>
/// It uses the domain's value objects rather than re-deriving anything: gross by
/// <see cref="Money.Times"/> under the store's policy, the discount taken off the TTC line,
/// then <see cref="Money.SplitTaxInclusive"/>, which derives TVA by subtraction so
/// <c>HT + TVA == line_total</c> on every line. A generated store whose receipts do not
/// recompute would teach the engine a till that cannot exist.
/// </para>
/// <para>
/// <b>A line is priced once, whatever it is taken from</b> (D-103): <see cref="Line"/> for the line,
/// <see cref="Split"/> over its batches, <see cref="Row"/> for each of them.
/// </para>
/// </summary>
public static class SaleArithmetic
{
    /// <param name="sellPrice">TTC price per selling unit on the day of sale.</param>
    /// <param name="quantity">How much was sold, in the selling unit.</param>
    /// <param name="discount">Taken off the TTC line before TVA is extracted; zero for none.</param>
    /// <param name="vat">The variant's VAT rate.</param>
    /// <param name="policy">The store's rounding policy, as stamped on the transaction.</param>
    public static LineAmounts Line(Money sellPrice, Quantity quantity, Money discount, BasisPoints vat, Rounding policy) =>
        Row(sellPrice.Times(quantity.Thousandths, Quantity.Scale, policy), discount, vat, policy);

    /// <summary>
    /// A row whose gross is already known: one batch's share of a line (<see cref="Split"/>). The
    /// discount comes off it and the TVA out of what is left, by subtraction (D-033).
    /// </summary>
    public static LineAmounts Row(Money gross, Money discount, BasisPoints vat, Rounding policy)
    {
        var total = gross - discount;
        return new LineAmounts(gross, total, total.SplitTaxInclusive(vat, policy));
    }

    /// <summary>
    /// A line taken from several batches (D-103). <b>The line is priced once</b>, the figure the
    /// customer was shown, and that figure is split over its batch rows with
    /// <see cref="Money.Allocate(IReadOnlyList{long})"/>, weighted by what each batch gave: the rows
    /// sum to the line, always (CLAUDE.md §3.1). Pricing each row on its own rounded twice, and
    /// 0,300 kg + 0,256 kg at 179,99 came to 100,08 where the scan had said 100,07.
    /// <para>
    /// A counted line's rows come out as count × price each: nothing was rounded, so there is
    /// nothing to spread.
    /// </para>
    /// </summary>
    /// <param name="gross">What the whole line comes to before any discount.</param>
    /// <param name="taken">What each batch gave, in the order <c>BatchAllocation.Take</c> gave it.</param>
    /// <returns>One share per batch, in the same order, summing to <paramref name="gross"/>.</returns>
    public static IReadOnlyList<Money> Split(Money gross, IReadOnlyList<Quantity> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        return gross.Allocate([.. taken.Select(quantity => quantity.Thousandths)]);
    }

    /// <summary>
    /// Whether the rows of one line agree with the line (D-053, D-103): together they come to
    /// <c>round(quantity × price)</c>, the line priced once, and each is its share of that. For a
    /// line on one row this is the second rule of <see cref="WeighedLine.Recomputes"/>. A price
    /// label's line is checked by the first, row by row: its total is the label's, not a product.
    /// </summary>
    /// <param name="sellPrice">The price the line was charged at.</param>
    /// <param name="rows">Each row's quantity, and its total before its discount, in ticket order.</param>
    /// <param name="policy">The policy stamped on the transaction.</param>
    public static bool LineRecomputes(Money sellPrice, IReadOnlyList<(Quantity Quantity, Money Gross)> rows, Rounding policy)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return false;
        }

        var gross = sellPrice.Times(rows.Sum(row => row.Quantity.Thousandths), Quantity.Scale, policy);
        return Split(gross, [.. rows.Select(row => row.Quantity)]).SequenceEqual(rows.Select(row => row.Gross));
    }
}
