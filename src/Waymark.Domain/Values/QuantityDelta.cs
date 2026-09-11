namespace Waymark.Domain.Values;

/// <summary>
/// A change in how much of something there is — what a stock movement does.
/// Signed: positive receives, negative issues.
///
/// <para>
/// Maps to <c>stock_movements.quantity_changed</c>, annotated "thousandths,
/// signed". The direction lives in the sign and the table's
/// <c>movement_type</c> says why.
/// </para>
///
/// <para>
/// Being a different type from <see cref="Quantity"/> is the entire point. The
/// algebra that falls out is the stock reconciliation CLAUDE.md §8 puts in its
/// top three, and it is now type-checked rather than conventional:
/// </para>
/// <code>
/// closing_level = opening_level + sum(deltas)
/// </code>
///
/// <para>
/// <b>Zero is a legal value here, though not a legal movement.</b>
/// <c>CHECK (quantity_changed &lt;&gt; 0)</c> stops a pointless row being
/// written; it does not stop a sum of changes coming to nothing, which happens
/// whenever a receipt and a write-off cancel, and it does not stop two equal
/// levels differing by nothing. A type that refused zero could not express
/// either, so the non-zero rule belongs to the column, exactly like the
/// non-negative rules on <c>quantity_ordered</c> (decisions.md D-036).
/// </para>
/// </summary>
public readonly struct QuantityDelta : IEquatable<QuantityDelta>, IComparable<QuantityDelta>
{
    private QuantityDelta(long thousandths, string unit)
    {
        Thousandths = thousandths;
        Unit = unit;
    }

    /// <summary>Signed count of thousandths. Positive adds, negative removes.</summary>
    public long Thousandths { get; }

    /// <summary>The <c>unit_code</c>, or null for <c>default(QuantityDelta)</c>.</summary>
    public string? Unit { get; }

    /// <summary>False only for <c>default(QuantityDelta)</c>.</summary>
    public bool HasUnit => Unit is not null;

    /// <summary>
    /// A change with no precision check — for reading a stored row back. When
    /// the unit's precision is at hand, use <see cref="UnitPrecision.Delta"/>.
    /// </summary>
    public static QuantityDelta FromThousandths(long thousandths, string unitCode)
    {
        if (string.IsNullOrWhiteSpace(unitCode))
        {
            throw new ArgumentException("A change needs a unit. 3 of what?", nameof(unitCode));
        }

        return new QuantityDelta(thousandths, unitCode);
    }

    /// <summary>No change at all — the identity for summing movements.</summary>
    public static QuantityDelta Zero(string unitCode) => FromThousandths(0, unitCode);

    /// <summary>
    /// Stock arriving. Refuses a negative magnitude rather than quietly taking
    /// its absolute value: "increase by -5" is a decrease, and the caller who
    /// wrote it meant something else.
    /// </summary>
    public static QuantityDelta Increase(Quantity magnitude)
    {
        if (magnitude.IsNegative)
        {
            throw new ArgumentOutOfRangeException(
                nameof(magnitude),
                magnitude,
                "An increase of a negative quantity is a decrease. Say QuantityDelta.Decrease.");
        }

        return new QuantityDelta(magnitude.Thousandths, magnitude.RequireUnit());
    }

    /// <summary>Stock leaving. Refuses a negative magnitude, for the same reason.</summary>
    public static QuantityDelta Decrease(Quantity magnitude)
    {
        if (magnitude.IsNegative)
        {
            throw new ArgumentOutOfRangeException(
                nameof(magnitude),
                magnitude,
                "A decrease of a negative quantity is an increase. Say QuantityDelta.Increase.");
        }

        return new QuantityDelta(checked(-magnitude.Thousandths), magnitude.RequireUnit());
    }

    /// <summary>The size of the change, with the direction discarded.</summary>
    public Quantity Magnitude => Quantity.FromThousandths(Math.Abs(Thousandths), RequireUnit());

    public bool IsIncrease => Thousandths > 0;

    public bool IsDecrease => Thousandths < 0;

    /// <summary>True for a change of nothing — legal as a value, not as a movement row.</summary>
    public bool IsZero => Thousandths == 0;

    /// <summary>-1, 0 or 1.</summary>
    public int Sign => Math.Sign(Thousandths);

    /// <summary>Whether this sits on a step the unit actually admits.</summary>
    public bool FitsIn(UnitPrecision precision) =>
        string.Equals(RequireUnit(), precision.Code, StringComparison.Ordinal)
        && precision.Allows(Thousandths);

    // ----------------------------------------------------------- arithmetic

    /// <summary>Changes sum to a change. Exact.</summary>
    public static QuantityDelta Add(QuantityDelta left, QuantityDelta right) =>
        new(checked(left.Thousandths + right.Thousandths), AgreedUnit(left, right, "add"));

    /// <summary>Exact.</summary>
    public static QuantityDelta Subtract(QuantityDelta left, QuantityDelta right) =>
        new(checked(left.Thousandths - right.Thousandths), AgreedUnit(left, right, "subtract"));

    /// <summary>The reversing movement — what undoes this one.</summary>
    public static QuantityDelta Negate(QuantityDelta change) =>
        new(checked(-change.Thousandths), change.RequireUnit());

    /// <summary>Exact. Three cases of the same movement.</summary>
    public static QuantityDelta Multiply(QuantityDelta change, int factor) =>
        new(checked(change.Thousandths * factor), change.RequireUnit());

    public static QuantityDelta operator +(QuantityDelta left, QuantityDelta right) => Add(left, right);

    public static QuantityDelta operator -(QuantityDelta left, QuantityDelta right) => Subtract(left, right);

    public static QuantityDelta operator -(QuantityDelta change) => Negate(change);

    public static QuantityDelta operator *(QuantityDelta change, int factor) => Multiply(change, factor);

    public static QuantityDelta operator *(int factor, QuantityDelta change) => Multiply(change, factor);

    /// <summary>
    /// Totals a run of movements. The right half of
    /// <c>closing = opening + sum(deltas)</c>.
    /// </summary>
    /// <param name="unitCode">The unit the total is in, so an empty run still has one.</param>
    /// <param name="changes">Every one of which must be in that unit.</param>
    public static QuantityDelta Sum(string unitCode, IEnumerable<QuantityDelta> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var total = Zero(unitCode);

        foreach (var change in changes)
        {
            total = Add(total, change);
        }

        return total;
    }

    // ----------------------------------------------------------- comparison

    /// <summary>Equal when both the amount and the unit match. Does not throw.</summary>
    public bool Equals(QuantityDelta other) =>
        Thousandths == other.Thousandths
        && string.Equals(Unit, other.Unit, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is QuantityDelta other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Thousandths, Unit is null ? 0 : StringComparer.Ordinal.GetHashCode(Unit));

    /// <exception cref="InvalidOperationException">The units differ.</exception>
    public int CompareTo(QuantityDelta other)
    {
        _ = AgreedUnit(this, other, "compare");
        return Thousandths.CompareTo(other.Thousandths);
    }

    public static bool operator ==(QuantityDelta left, QuantityDelta right) => left.Equals(right);

    public static bool operator !=(QuantityDelta left, QuantityDelta right) => !left.Equals(right);

    public static bool operator <(QuantityDelta left, QuantityDelta right) => left.CompareTo(right) < 0;

    public static bool operator <=(QuantityDelta left, QuantityDelta right) => left.CompareTo(right) <= 0;

    public static bool operator >(QuantityDelta left, QuantityDelta right) => left.CompareTo(right) > 0;

    public static bool operator >=(QuantityDelta left, QuantityDelta right) => left.CompareTo(right) >= 0;

    // --------------------------------------------------------------- guards

    internal string RequireUnit()
    {
        if (Unit is null)
        {
            throw new InvalidOperationException(
                "This is default(QuantityDelta), which has no unit. Build it with "
                + "QuantityDelta.FromThousandths(value, unitCode) or UnitPrecision.Delta(value).");
        }

        return Unit;
    }

    private static string AgreedUnit(QuantityDelta left, QuantityDelta right, string operation)
    {
        var unit = left.RequireUnit();
        var other = right.RequireUnit();

        if (!string.Equals(unit, other, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot {operation} {left} and {right}: different units. Convert through "
                + "units_of_measure.factor_to_base and say so, rather than adding the numbers "
                + "(CLAUDE.md §3.8).");
        }

        return unit;
    }

    /// <summary>Always signed, because the direction is the point.</summary>
    public override string ToString() => Quantity.Format(Thousandths, Unit, signed: true);
}
