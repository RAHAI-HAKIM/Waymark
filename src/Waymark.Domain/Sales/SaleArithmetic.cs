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
/// </summary>
public static class SaleArithmetic
{
    /// <param name="sellPrice">TTC price per selling unit on the day of sale.</param>
    /// <param name="quantity">How much was sold, in the selling unit.</param>
    /// <param name="discount">Taken off the TTC line before TVA is extracted; zero for none.</param>
    /// <param name="vat">The variant's VAT rate.</param>
    /// <param name="policy">The store's rounding policy, as stamped on the transaction.</param>
    public static LineAmounts Line(Money sellPrice, Quantity quantity, Money discount, BasisPoints vat, Rounding policy)
    {
        var gross = sellPrice.Times(quantity.Thousandths, Quantity.Scale, policy);
        var total = gross - discount;
        return new LineAmounts(gross, total, total.SplitTaxInclusive(vat, policy));
    }
}
