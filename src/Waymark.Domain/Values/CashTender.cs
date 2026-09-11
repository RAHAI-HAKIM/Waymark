namespace Waymark.Domain.Values;

/// <summary>
/// What is actually taken in cash, and the difference from what was owed.
///
/// <para>
/// <see cref="Variance"/> is signed and is <see cref="Tendered"/> minus the
/// exact amount, so it is negative when the customer hands over less than the
/// invoice says. It is a real movement of money, not a presentation detail, and
/// it belongs in <c>rounding_variance</c> with source <c>cash_tender</c>.
/// </para>
/// <para>
/// It must not be allowed to reach <c>cash_sessions.variance</c>. That column
/// exists to detect theft and miscounting; a few dinars of tender rounding
/// every day would teach the shopkeeper to ignore the number, and the control
/// would be dead (decisions.md D-034).
/// </para>
/// </summary>
/// <param name="Tendered">The amount that can actually change hands.</param>
/// <param name="Variance">Tendered minus the exact amount. Zero for a currency that does not round cash.</param>
public readonly record struct CashTender(Money Tendered, Money Variance)
{
    /// <summary>True when rounding moved the amount at all.</summary>
    public bool HasVariance => !Variance.IsZero;
}
