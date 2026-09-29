namespace Waymark.Domain.Values;

/// <summary>
/// The one rounding implementation: a fraction rounded to a whole number under a policy.
///
/// <para>
/// <see cref="Money.Times"/> rounds through it, and so does anything that derives a whole number
/// of steps from money, such as a weight worked back from a scale label's price (B3, D-090). One
/// implementation to reason about and to test, never a second one written beside the first.
/// </para>
/// <para>
/// The fraction is taken in <see cref="Int128"/>, so a product of two <c>long</c>s cannot overflow
/// before it is divided back down. Overflow of the <i>result</i> throws.
/// </para>
/// </summary>
public static class RationalRounding
{
    /// <summary><paramref name="numerator"/> ÷ <paramref name="denominator"/>, rounded once.</summary>
    /// <exception cref="DivideByZeroException"><paramref name="denominator"/> is zero (D-037).</exception>
    public static long Divide(Int128 numerator, Int128 denominator, Rounding rounding)
    {
        if (denominator == 0)
        {
            throw new DivideByZeroException(
                "Money cannot be divided by zero. If the caller expects zero to be possible, "
                + "call TryDivideBy and handle the null.");
        }

        // Normalise the sign onto the numerator so the half-way test below only
        // ever compares magnitudes.
        if (denominator < 0)
        {
            denominator = -denominator;
            numerator = -numerator;
        }

        var quotient = numerator / denominator;          // truncates toward zero
        var remainder = numerator - (quotient * denominator);

        if (remainder != 0)
        {
            var twiceRemainder = Int128.Abs(remainder) * 2;

            var awayFromZero = twiceRemainder > denominator
                || (twiceRemainder == denominator && IsHalfRoundedAway(quotient, rounding));

            if (awayFromZero)
            {
                quotient += numerator < 0 ? -1 : 1;
            }
        }

        return checked((long)quotient);
    }

    private static bool IsHalfRoundedAway(Int128 quotient, Rounding rounding) => rounding switch
    {
        Rounding.HalfUp => true,
        // Away from zero makes an odd quotient even, which is the whole point.
        Rounding.HalfEven => (quotient & 1) != 0,
        _ => throw new ArgumentOutOfRangeException(nameof(rounding), rounding, "Unknown rounding policy."),
    };
}
