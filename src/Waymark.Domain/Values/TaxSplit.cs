namespace Waymark.Domain.Values;

/// <summary>
/// A line total separated into its net and its tax.
///
/// <para>
/// <see cref="Net"/> plus <see cref="Tax"/> equals the gross exactly, always,
/// because one of the two is derived from the other by subtraction rather than
/// rounded on its own (decisions.md D-033). The three figures map to
/// <c>transaction_items</c>: the gross is <c>line_total</c>, the net sums into
/// <c>transactions.subtotal</c>, the tax into <c>tax_amount</c> and
/// <c>transactions.tax_total</c>.
/// </para>
/// </summary>
/// <param name="Net">The amount before tax — HT.</param>
/// <param name="Tax">The tax itself — TVA.</param>
public readonly record struct TaxSplit(Money Net, Money Tax)
{
    /// <summary>The tax-inclusive total — TTC. Exact, by construction.</summary>
    public Money Gross => Net + Tax;
}
