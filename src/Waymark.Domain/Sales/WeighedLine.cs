using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Domain.Sales;

/// <summary>A price label's line: the quantity worked back from its price, and what the line comes to.</summary>
/// <param name="Quantity">The derived quantity, on a step the unit allows. What leaves the shelf.</param>
/// <param name="Amounts">The line's figures; <c>LineTotal</c> is the label's price, to the centime.</param>
public readonly record struct DeclaredLine(Quantity Quantity, LineAmounts Amounts);

/// <summary>
/// <b>Session B3.</b> The arithmetic of a line sold by weight (D-090, closes O-26).
///
/// <para><b>The rules <c>WeighedLineTests</c> hold you to:</b></para>
/// <list type="number">
///   <item><description><b><see cref="ByWeight"/></b>: a weight typed or read from a weight label is
///   exact, and the line is <see cref="SaleArithmetic.Line"/> of it, with no discount. Refuse
///   (<see cref="ArgumentOutOfRangeException"/>) a weight that is not above zero or not on a step the
///   unit allows: 0,5555 kg cannot be sold.</description></item>
///   <item><description><b><see cref="ByDeclaredTotal"/></b>: a price label's price is exact, because
///   it is what the customer was shown (Law 04-02). The quantity is worked back from it,
///   <c>declared ÷ sellPrice</c>, and rounded <b>once</b>, by the store's <paramref name="policy"/>,
///   to a whole number of the unit's steps (<see cref="UnitPrecision.StepThousandths"/>). The line
///   total is the declared price, never <c>quantity × price</c>; its TVA is taken out of it by
///   subtraction (<see cref="Money.SplitTaxInclusive"/>, D-033). <c>Gross</c> is the declared price
///   too: nothing was taken off it. Worked example: 100,00 DA at 180,00/kg is 0,5555… kg, so
///   0,556 kg, and the line stays 100,00 (not 100,08).</description></item>
///   <item><description>A declared price worth <b>less than half a step</b> rounds to nothing: return
///   <c>null</c>, the label sells nothing. A declared price that is not above zero is refused
///   (<see cref="ArgumentOutOfRangeException"/>), and a sell price of zero throws
///   <see cref="DivideByZeroException"/>: absence is never zero (D-037).</description></item>
///   <item><description><b><see cref="SplitDeclared"/></b>: a label's line taken from several batches
///   (<c>BatchAllocation.Take</c>) is split over them with <see cref="Money.Allocate(IReadOnlyList{long})"/>,
///   weighted by the thousandths each batch gave, so the parts sum to the label's price exactly.
///   Never round each part on its own (CLAUDE.md §3.1).</description></item>
///   <item><description><b><see cref="Recomputes"/></b>: whether a stored row still agrees with
///   itself (D-053). For <see cref="QuantitySource.LabelPrice"/> the total is exact, so the row holds
///   when its quantity is within <b>one step</b> of <c>(line_total + discount) ÷ sell_price</c> (a split row's
///   share is rounded again by <c>Allocate</c>, which is why it is one step and not half). For every
///   other source the quantity is exact, and the row holds when
///   <c>line_total == round(quantity × sell_price) − discount</c>, exactly. <b>That is a line on one
///   row.</b> A weighed line taken from two batches is priced once and split (D-103), so a row of it
///   may sit a centime off its own product: <see cref="SaleArithmetic.LineRecomputes"/> checks such a
///   line whole.</description></item>
/// </list>
/// <para>
/// <b>What you have to build with:</b> <see cref="RationalRounding.Divide"/> is the one rounding
/// implementation (<see cref="Money.Times"/> rounds through it too), so the quantity is rounded by
/// the same lines as every centime. Quantities are thousandths (<see cref="Quantity.Scale"/>); build
/// the derived one with <see cref="UnitPrecision.Quantity"/>, which refuses a value off the unit's step.
/// </para>
/// </summary>
public static class WeighedLine
{
    /// <param name="sellPrice">TTC price per selling unit (per kilo).</param>
    /// <param name="weight">The weight, in the selling unit.</param>
    /// <param name="unit">The selling unit's precision: which weights exist.</param>
    /// <param name="vat">The variant's TVA rate.</param>
    /// <param name="policy">The store's rounding policy.</param>
    public static LineAmounts ByWeight(Money sellPrice, Quantity weight, UnitPrecision unit, BasisPoints vat, Rounding policy)
    {
        // Zero is no weighing, and 0,5555 kg is a weight the unit cannot be sold in.
        if (!weight.IsPositive || !weight.FitsIn(unit))
        {
            throw new ArgumentOutOfRangeException(nameof(weight), weight.Thousandths, $"Not a weight '{unit.Code}' is sold in.");
        }

        // The weight is exact: the line is weight × price, rounded once, nothing taken off.
        return SaleArithmetic.Line(sellPrice, weight, Money.Zero(sellPrice.Currency), vat, policy);
    }

    /// <param name="sellPrice">TTC price per selling unit (per kilo).</param>
    /// <param name="declared">The price printed on the label: what the customer pays.</param>
    /// <param name="unit">The selling unit's precision: the step the quantity is rounded to.</param>
    /// <param name="vat">The variant's TVA rate.</param>
    /// <param name="policy">The store's rounding policy.</param>
    /// <returns>The line, or null when the price is worth less than half a step.</returns>
    public static DeclaredLine? ByDeclaredTotal(Money sellPrice, Money declared, UnitPrecision unit, BasisPoints vat, Rounding policy)
    {
        if (!declared.IsPositive)
        {
            throw new ArgumentOutOfRangeException(nameof(declared), declared.MinorUnits, "A label's price is above zero.");
        }

        // declared ÷ sellPrice is in selling units; counted in the unit's steps it is
        // declared × 1000 ÷ (sellPrice × step), rounded once. A zero price throws here (D-037).
        var steps = RationalRounding.Divide(
            (Int128)declared.MinorUnits * Quantity.Scale,
            (Int128)sellPrice.MinorUnits * unit.StepThousandths,
            policy);
        if (steps == 0)
        {
            return null;
        }

        // The total is the label's, to the centime: never quantity × price (O-26).
        return new DeclaredLine(
            unit.Quantity(checked(steps * unit.StepThousandths)),
            new LineAmounts(declared, declared, declared.SplitTaxInclusive(vat, policy)));
    }

    /// <param name="declared">The label's price.</param>
    /// <param name="taken">What each batch gave, in the order <c>BatchAllocation.Take</c> gave it.</param>
    /// <returns>One share per batch, in the same order, summing to <paramref name="declared"/>.</returns>
    public static IReadOnlyList<Money> SplitDeclared(Money declared, IReadOnlyList<Quantity> taken) =>
        // Largest remainder, weighted by what each batch gave: the shares sum to the label. Every
        // line is split this way since D-103, so the rule is SaleArithmetic's.
        SaleArithmetic.Split(declared, taken);

    /// <summary>Whether a stored sale row agrees with itself (D-053, D-090).</summary>
    public static bool Recomputes(
        QuantitySource source, Money sellPrice, Quantity quantity, Money discount, Money lineTotal, UnitPrecision unit, Rounding policy)
    {
        if (source == QuantitySource.LabelPrice)
        {
            // The total is exact and the quantity derived: |quantity × price − total| ≤ one step's
            // worth, compared in thousandths × centimes so nothing is rounded to check it.
            // The label's price is the row's total before any discount given at the counter (B4).
            var labelled = lineTotal + discount;
            var gap = ((Int128)quantity.Thousandths * sellPrice.MinorUnits) - ((Int128)labelled.MinorUnits * Quantity.Scale);
            return Int128.Abs(gap) <= (Int128)unit.StepThousandths * sellPrice.MinorUnits;
        }

        // The quantity is exact: the row is quantity × price, rounded once, less its discount.
        return sellPrice.Times(quantity.Thousandths, Quantity.Scale, policy) - discount == lineTotal;
    }
}
