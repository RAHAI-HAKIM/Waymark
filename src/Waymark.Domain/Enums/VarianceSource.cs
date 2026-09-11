namespace Waymark.Domain.Enums;

/// <summary>
/// What created or destroyed a minor unit, for a row of <c>rounding_variance</c>.
///
/// <para>
/// <b>What this enum leaves out is an assertion.</b> There is no
/// <c>allocation</c> member, because splitting a known total across parts is
/// exact by construction — largest remainder, <c>sum(parts) == total</c>, always
/// (decisions.md D-032). A variance row from an allocation would mean the
/// allocator is broken, and having nowhere to write it is how that stays true.
/// </para>
/// <para>
/// There is no <c>tax_reconciliation</c> member either, and that one was
/// briefly drafted. TVA is extracted per line from the TTC price and the tax is
/// derived by subtraction, so <c>net + tax == line_total</c> holds on every line
/// under either policy, and summing the lines gives
/// <c>subtotal + tax_total == total_amount</c> exactly. Invoice-level tax
/// variance cannot occur, so the source was dropped before it was ever built
/// (D-033). Adding it back would mean the tax arithmetic had changed.
/// </para>
/// </summary>
public enum VarianceSource
{
    /// <summary>
    /// Cash rounded to what can actually change hands — 5,00 DZD. Real money in
    /// or out of the drawer, and the reason this ledger exists at all: left in
    /// <c>cash_sessions.variance</c> it would be noise on the number that
    /// detects theft (D-034).
    /// </summary>
    CashTender,

    /// <summary>
    /// A residual from converting one currency to another, which happens once,
    /// when stock received against a foreign-currency order is valued into the
    /// ledger currency (D-035). Nothing produces these yet.
    /// </summary>
    CurrencyConversion,
}
