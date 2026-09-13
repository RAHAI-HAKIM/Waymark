using System.Globalization;

namespace Waymark.Domain.Values;

/// <summary>
/// An amount of money: an exact integer count of minor units, and the currency
/// it is counted in.
///
/// <para>
/// Maps to the schema's <c>INTEGER</c> money columns with no change to any of
/// them — <c>schema_v7_1.sql</c> already fixes the storage as "INTEGER, in
/// centimes (scale 100). Never REAL, never TEXT."
/// </para>
///
/// <para>
/// <b>No arithmetic that can round exists as an operator.</b> Addition,
/// subtraction, negation, comparison and multiplication by a whole number are
/// exact, and are operators. Multiplication by a rate or a quantity, and
/// division, exist only as methods that take an explicit <see cref="Rounding"/>.
/// There is deliberately no <c>operator *(Money, decimal)</c>, and
/// <c>MoneyApiShapeTests</c> fails if one is ever added.
/// </para>
/// <para>
/// The point is not ceremony. Grepping for <see cref="Times"/>,
/// <see cref="Percent"/> and <see cref="Allocate(IReadOnlyList{long})"/> returns
/// every site in the codebase where a centime can be created or destroyed, and
/// that list is what CLAUDE.md §7 asks for. An implicit multiply operator
/// would scatter those sites into ordinary-looking arithmetic, and there would
/// be no way to enumerate them again (decisions.md D-031).
/// </para>
/// <para>
/// <c>default(Money)</c> has no currency. It compares equal to itself and to
/// nothing else, and any arithmetic on it throws rather than silently adopting
/// the other operand's currency.
/// </para>
/// </summary>
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    // Qualified so it does not read as the Currency *property* below.
    private const int Scale = Values.Currency.StorageScale;

    /// <param name="minorUnits">
    /// Signed count of Waymark minor units — centimes for DZD. See
    /// <see cref="Currency.StorageScale"/>.
    /// </param>
    /// <param name="currency">The currency this amount is counted in.</param>
    public Money(long minorUnits, Currency currency)
    {
        if (!currency.IsDefined)
        {
            throw new ArgumentException(
                "Money needs a currency. Use Money.Zero(Currency.Dzd) rather than default(Money).",
                nameof(currency));
        }

        MinorUnits = minorUnits;
        Currency = currency;
    }

    /// <summary>Signed count of minor units. Centimes, for DZD.</summary>
    public long MinorUnits { get; }

    /// <summary>The currency. <see cref="Values.Currency.IsDefined"/> is false only for <c>default(Money)</c>.</summary>
    public Currency Currency { get; }

    /// <summary>Nothing, in a stated currency. There is no currency-less zero, deliberately.</summary>
    public static Money Zero(Currency currency) => new(0, currency);

    /// <summary>Reads a stored column value back into a <see cref="Money"/>.</summary>
    public static Money FromMinorUnits(long minorUnits, Currency currency) => new(minorUnits, currency);

    public bool IsZero => MinorUnits == 0;

    public bool IsNegative => MinorUnits < 0;

    public bool IsPositive => MinorUnits > 0;

    /// <summary>-1, 0 or 1.</summary>
    public int Sign => Math.Sign(MinorUnits);

    /// <summary>The same amount without its sign.</summary>
    public Money Abs() => new(Math.Abs(MinorUnits), Currency);

    // ---------------------------------------------------------------- exact

    /// <summary>Exact. Throws if the currencies differ.</summary>
    public static Money Add(Money left, Money right) =>
        new(checked(left.MinorUnits + right.MinorUnits), Agreed(left, right, "add"));

    /// <summary>Exact. Throws if the currencies differ.</summary>
    public static Money Subtract(Money left, Money right) =>
        new(checked(left.MinorUnits - right.MinorUnits), Agreed(left, right, "subtract"));

    /// <summary>Exact. A whole number of an amount is still a whole number of minor units.</summary>
    public static Money Multiply(Money amount, int factor) =>
        new(checked(amount.MinorUnits * factor), amount.RequireCurrency());

    /// <summary>Exact.</summary>
    public static Money Negate(Money amount) =>
        new(checked(-amount.MinorUnits), amount.RequireCurrency());

    public static Money operator +(Money left, Money right) => Add(left, right);

    public static Money operator -(Money left, Money right) => Subtract(left, right);

    public static Money operator -(Money amount) => Negate(amount);

    public static Money operator *(Money amount, int factor) => Multiply(amount, factor);

    public static Money operator *(int factor, Money amount) => Multiply(amount, factor);

    // ------------------------------------------------------------- rounding

    /// <summary>
    /// Multiplies by the rational <paramref name="numerator"/>/<paramref name="denominator"/>
    /// and rounds <b>once</b>, at the end.
    ///
    /// <para>
    /// This is the one primitive that can create or destroy a minor unit.
    /// <see cref="Percent"/>, <see cref="DivideBy"/> and
    /// <see cref="SplitTaxInclusive"/> are all expressed in terms of it, so
    /// there is exactly one rounding implementation to reason about and to test.
    /// A rational rather than a <c>decimal</c> factor because a quantity in
    /// thousandths and a rate in basis points are both exact rationals, and
    /// converting them to a decimal first would round twice.
    /// </para>
    /// <para>
    /// The intermediate product is computed in <see cref="Int128"/>, so an
    /// amount times a large quantity cannot overflow before it is divided back
    /// down. Overflow of the <i>result</i> throws.
    /// </para>
    /// </summary>
    /// <exception cref="DivideByZeroException"><paramref name="denominator"/> is zero.</exception>
    public Money Times(long numerator, long denominator, Rounding rounding)
    {
        var currency = RequireCurrency();

        if (denominator == 0)
        {
            throw new DivideByZeroException(
                "Money cannot be divided by zero. If the caller expects zero to be possible, "
                + "call TryDivideBy and handle the null.");
        }

        var product = (Int128)MinorUnits * numerator;
        Int128 divisor = denominator;

        // Normalise the sign onto the product so the half-way test below only
        // ever compares magnitudes.
        if (divisor < 0)
        {
            divisor = -divisor;
            product = -product;
        }

        var quotient = product / divisor;          // truncates toward zero
        var remainder = product - (quotient * divisor);

        if (remainder != 0)
        {
            var twiceRemainder = Int128.Abs(remainder) * 2;

            var awayFromZero = twiceRemainder > divisor
                || (twiceRemainder == divisor && IsHalfRoundedAway(quotient, rounding));

            if (awayFromZero)
            {
                quotient += product < 0 ? -1 : 1;
            }
        }

        return new Money(checked((long)quotient), currency);
    }

    private static bool IsHalfRoundedAway(Int128 quotient, Rounding rounding) => rounding switch
    {
        Rounding.HalfUp => true,
        // Away from zero makes an odd quotient even, which is the whole point.
        Rounding.HalfEven => (quotient & 1) != 0,
        _ => throw new ArgumentOutOfRangeException(nameof(rounding), rounding, "Unknown rounding policy."),
    };

    /// <summary>A percentage of this amount, rounded once.</summary>
    public Money Percent(BasisPoints rate, Rounding rounding) =>
        Times(rate.Value, BasisPoints.Scale, rounding);

    /// <summary>
    /// Divides, rounding once.
    /// </summary>
    /// <exception cref="DivideByZeroException">
    /// Always, for a zero divisor. Absence is never represented as zero (D-037).
    /// </exception>
    public Money DivideBy(long divisor, Rounding rounding) => Times(1, divisor, rounding);

    /// <summary>
    /// Divides, or returns null for a zero divisor — for the caller who has a
    /// real reason to expect one and must then say what to show instead.
    /// </summary>
    public Money? TryDivideBy(long divisor, Rounding rounding) =>
        divisor == 0 ? null : Times(1, divisor, rounding);

    // ----------------------------------------------------------- allocation

    /// <summary>
    /// Splits this amount into <paramref name="parts"/> equal shares that sum
    /// back to it exactly.
    /// </summary>
    public IReadOnlyList<Money> Allocate(int parts)
    {
        if (parts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(parts), parts, "An amount splits into at least one part.");
        }

        var weights = new long[parts];
        Array.Fill(weights, 1L);
        return Allocate(weights);
    }

    /// <summary>
    /// Splits this amount in proportion to <paramref name="weights"/>, so that
    /// the parts sum back to it <b>exactly</b>.
    ///
    /// <para>
    /// Each part takes its floor, then the leftover minor units are handed out
    /// one at a time, largest remainder first, ties broken by position. The
    /// result is exact by construction and deterministic for a given input —
    /// which is why a basket discount split across lines produces no variance
    /// at all, and why <c>rounding_variance</c> has no allocation source
    /// (decisions.md D-032, D-034).
    /// </para>
    /// <para>
    /// Rounding each part independently instead would guarantee the opposite.
    /// </para>
    /// </summary>
    /// <param name="weights">Non-negative, and not all zero. Line totals, quantities, whatever the split is by.</param>
    public IReadOnlyList<Money> Allocate(IReadOnlyList<long> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        var currency = RequireCurrency();

        if (weights.Count == 0)
        {
            throw new ArgumentException("An amount splits into at least one part.", nameof(weights));
        }

        Int128 totalWeight = 0;
        for (var i = 0; i < weights.Count; i++)
        {
            if (weights[i] < 0)
            {
                throw new ArgumentException(
                    $"Weight at index {i} is negative. A share of an amount cannot be negative; "
                    + "if the intent is a refund line, negate the amount rather than the weight.",
                    nameof(weights));
            }

            totalWeight += weights[i];
        }

        if (totalWeight == 0)
        {
            throw new ArgumentException(
                "The weights sum to zero, so there is no proportion to split by.", nameof(weights));
        }

        var shares = new Int128[weights.Count];
        var remainders = new Int128[weights.Count];
        Int128 assigned = 0;

        for (var i = 0; i < weights.Count; i++)
        {
            var product = (Int128)MinorUnits * weights[i];
            var share = product / totalWeight;
            shares[i] = share;
            remainders[i] = Int128.Abs(product - (share * totalWeight));
            assigned += share;
        }

        var leftover = (Int128)MinorUnits - assigned;
        var step = leftover < 0 ? -1 : 1;
        var outstanding = Int128.Abs(leftover);

        // Provable, and asserted because the proof is not visible at the call
        // site: sum(remainders) == totalWeight * leftover and each remainder is
        // below totalWeight, so at least |leftover| entries have a non-zero
        // remainder and sort ahead of every zero-weight entry. A zero-weight
        // line therefore never receives a unit.
        if (outstanding >= weights.Count)
        {
            throw new InvalidOperationException(
                $"Allocation left {outstanding} units to distribute across {weights.Count} parts, "
                + "which cannot happen. The largest-remainder step is wrong.");
        }

        var units = (int)outstanding;

        var order = new int[weights.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (left, right) =>
        {
            var byRemainder = remainders[right].CompareTo(remainders[left]);
            return byRemainder != 0 ? byRemainder : left.CompareTo(right);
        });

        for (var i = 0; i < units; i++)
        {
            shares[order[i]] += step;
        }

        var result = new Money[weights.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new Money(checked((long)shares[i]), currency);
        }

        return result;
    }

    // ------------------------------------------------------------------ tax

    /// <summary>
    /// Treats this amount as a TTC (tax-inclusive) line total and splits it.
    ///
    /// <para>
    /// The net is rounded and <b>the tax is derived by subtraction</b>. That is
    /// what makes <c>Net + Tax == this</c> true by construction, on every line,
    /// under either policy, with no residual — and therefore what makes
    /// <c>subtotal + tax_total == total_amount</c> exact once the lines are
    /// summed. Rounding the tax independently instead would leave a residual
    /// and force an invoice-level reconciliation that does not otherwise exist
    /// (decisions.md D-033).
    /// </para>
    /// <para>
    /// Displayed retail prices in Algeria are TTC under Law No. 04-02, and TVA
    /// is computed per line item with the discount applied first — so this runs
    /// on the discounted line total, not the gross one.
    /// </para>
    /// </summary>
    public TaxSplit SplitTaxInclusive(BasisPoints rate, Rounding rounding)
    {
        var net = Times(BasisPoints.Scale, BasisPoints.Scale + rate.Value, rounding);
        return new TaxSplit(net, this - net);
    }

    /// <summary>
    /// Treats this amount as an HT (tax-exclusive) figure and adds the tax —
    /// the <c>prices.is_tax_inclusive = 0</c> case, and how supplier prices work.
    /// </summary>
    public TaxSplit AddTaxExclusive(BasisPoints rate, Rounding rounding) =>
        new(this, Percent(rate, rounding));

    // ----------------------------------------------------------------- cash

    /// <summary>
    /// Rounds to what can actually change hands in cash, and reports the
    /// difference.
    ///
    /// <para>
    /// <b>The tender rounds; the invoice does not.</b> An invoice is a fiscal
    /// document, it must be printable before the customer chooses how to pay,
    /// and it must not change because they reached for cash instead of a card.
    /// So this is applied to the cash portion of a tender and nowhere else, and
    /// <see cref="CashTender.Variance"/> is a real movement of money that goes
    /// to <c>rounding_variance</c> — never into <c>cash_sessions.variance</c>,
    /// which exists to detect theft and must not be filled with noise
    /// (decisions.md D-034).
    /// </para>
    /// <para>
    /// Nearest, ties away from zero. Always-toward-the-store was rejected: it
    /// takes up to 4,99 DZD from every cash customer on every sale,
    /// systematically, and nearest is also what a shopkeeper does by hand.
    /// </para>
    /// </summary>
    public CashTender ToCashTender()
    {
        var currency = RequireCurrency();
        var step = currency.CashRoundingStep;

        if (step <= 1)
        {
            return new CashTender(this, Zero(currency));
        }

        var steps = MinorUnits / step;
        var remainder = MinorUnits - (steps * step);

        if (remainder != 0 && Math.Abs(remainder) * 2 >= step)
        {
            steps += MinorUnits < 0 ? -1 : 1;
        }

        var tendered = new Money(checked(steps * step), currency);
        return new CashTender(tendered, tendered - this);
    }

    // ----------------------------------------------------------- comparison

    /// <summary>
    /// Two amounts are equal when both the count and the currency match.
    ///
    /// <para>
    /// Unlike <see cref="CompareTo"/> this does not throw on a currency
    /// mismatch, and the difference is deliberate: "are these the same value"
    /// has an answer for 100 DZD and 100 EUR — no — while "which is larger"
    /// does not. Equality that threw would also make <see cref="Money"/>
    /// unusable as a dictionary key.
    /// </para>
    /// </summary>
    public bool Equals(Money other) =>
        MinorUnits == other.MinorUnits && Currency == other.Currency;

    public override bool Equals(object? obj) => obj is Money other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(MinorUnits, Currency);

    /// <exception cref="InvalidOperationException">The currencies differ.</exception>
    public int CompareTo(Money other)
    {
        _ = Agreed(this, other, "compare");
        return MinorUnits.CompareTo(other.MinorUnits);
    }

    public static bool operator ==(Money left, Money right) => left.Equals(right);

    public static bool operator !=(Money left, Money right) => !left.Equals(right);

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    // ---------------------------------------------------------------- guards

    private Currency RequireCurrency()
    {
        if (!Currency.IsDefined)
        {
            throw new InvalidOperationException(
                "This is default(Money), which has no currency. Build it with "
                + "Money.Zero(currency) or new Money(minorUnits, currency).");
        }

        return Currency;
    }

    private static Currency Agreed(Money left, Money right, string operation)
    {
        var currency = left.RequireCurrency();
        _ = right.RequireCurrency();

        if (currency != right.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} {left} and {right}: they are different currencies. Waymark "
                + "never converts implicitly.");
        }

        return currency;
    }

    public override string ToString()
    {
        var units = MinorUnits / Scale;
        var fraction = Math.Abs(MinorUnits % Scale);
        var sign = MinorUnits < 0 && units == 0 ? "-" : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{units}.{fraction.ToString("D2", CultureInfo.InvariantCulture)} {Currency.Code}");
    }
}
