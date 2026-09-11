using System.Globalization;

namespace Waymark.Domain.Values;

/// <summary>
/// How much of something there is: a level, or a magnitude. What you order,
/// receive, count, return, or have on the shelf.
///
/// <para>
/// Stored in thousandths, matching the schema — "INTEGER, in thousandths
/// (scale 1000). Supports kg to the gram."
/// </para>
///
/// <para>
/// <b><see cref="Quantity"/> plus <see cref="Quantity"/> deliberately does not
/// exist.</b> A level and a change are different things, and the archetypal
/// stock bug is passing one where the other was meant — both are plausible
/// integers, so no test catches it, because whoever writes the test makes the
/// same mistake. The operator set is the specification (decisions.md D-036):
/// </para>
/// <code>
/// Quantity      + QuantityDelta  ->  Quantity        a level plus a change is a level
/// Quantity      - QuantityDelta  ->  Quantity        and so is a level less a change
/// Quantity      - Quantity       ->  QuantityDelta   two levels differ by a change
/// QuantityDelta + QuantityDelta  ->  QuantityDelta   changes sum to a change
/// </code>
/// <para>
/// Totalling magnitudes — how many units across the lines of an order — is
/// legitimate and goes through <see cref="Sum(string, IEnumerable{Quantity})"/>,
/// which has to be named. The friction is at exactly the point where somebody
/// should ask whether they are adding levels.
/// </para>
/// <para>
/// <b>A negative level is not a bug.</b> <c>inventories.quantity</c> is
/// annotated "may be negative" because a sale can outrun its receipt, and that
/// happens constantly in a real shop. Where the schema requires a positive
/// value — <c>quantity_ordered</c>, <c>quantity_received</c>,
/// <c>quantity_returned</c> — that is a per-column CHECK, not a property of
/// this type.
/// </para>
/// </summary>
public readonly struct Quantity : IEquatable<Quantity>, IComparable<Quantity>
{
    /// <summary>Thousandths per whole unit. Matches the schema's scale.</summary>
    public const int Scale = 1_000;

    private Quantity(long thousandths, string unit)
    {
        Thousandths = thousandths;
        Unit = unit;
    }

    /// <summary>Signed count of thousandths of the unit.</summary>
    public long Thousandths { get; }

    /// <summary>The <c>unit_code</c>, or null for <c>default(Quantity)</c>.</summary>
    public string? Unit { get; }

    /// <summary>False only for <c>default(Quantity)</c>.</summary>
    public bool HasUnit => Unit is not null;

    /// <summary>
    /// A quantity with no precision check — for reading a stored row back,
    /// where the value was already checked on the way in and throwing now would
    /// only make existing data unreadable.
    ///
    /// <para>
    /// When the unit's precision is at hand, use
    /// <see cref="UnitPrecision.Quantity"/> instead, which refuses 1,5 of
    /// something sold whole.
    /// </para>
    /// </summary>
    public static Quantity FromThousandths(long thousandths, string unitCode)
    {
        if (string.IsNullOrWhiteSpace(unitCode))
        {
            throw new ArgumentException(
                "A quantity needs a unit. 3 of what?", nameof(unitCode));
        }

        return new Quantity(thousandths, unitCode);
    }

    /// <summary>None of something, in a stated unit. There is no unit-less zero.</summary>
    public static Quantity Zero(string unitCode) => FromThousandths(0, unitCode);

    public bool IsZero => Thousandths == 0;

    public bool IsNegative => Thousandths < 0;

    public bool IsPositive => Thousandths > 0;

    /// <summary>-1, 0 or 1.</summary>
    public int Sign => Math.Sign(Thousandths);

    /// <summary>The same amount without its sign.</summary>
    public Quantity Abs() => new(Math.Abs(Thousandths), RequireUnit());

    /// <summary>Whether this sits on a step the unit actually admits.</summary>
    public bool FitsIn(UnitPrecision precision) =>
        string.Equals(RequireUnit(), precision.Code, StringComparison.Ordinal)
        && precision.Allows(Thousandths);

    // ----------------------------------------------------------- arithmetic

    /// <summary>A level plus a change is a level. Exact.</summary>
    public static Quantity Add(Quantity level, QuantityDelta change) =>
        new(checked(level.Thousandths + change.Thousandths), AgreedUnit(level, change, "add"));

    /// <summary>A level less a change is a level. Exact.</summary>
    public static Quantity Subtract(Quantity level, QuantityDelta change) =>
        new(checked(level.Thousandths - change.Thousandths), AgreedUnit(level, change, "subtract"));

    /// <summary>Two levels differ by a change. Exact.</summary>
    public static QuantityDelta Difference(Quantity from, Quantity to) =>
        QuantityDelta.FromThousandths(
            checked(to.Thousandths - from.Thousandths), AgreedUnit(from, to, "compare"));

    /// <summary>A whole number of a magnitude is still a magnitude. Exact.</summary>
    public static Quantity Multiply(Quantity magnitude, int factor) =>
        new(checked(magnitude.Thousandths * factor), magnitude.RequireUnit());

    public static Quantity operator +(Quantity level, QuantityDelta change) => Add(level, change);

    public static Quantity operator +(QuantityDelta change, Quantity level) => Add(level, change);

    public static Quantity operator -(Quantity level, QuantityDelta change) => Subtract(level, change);

    /// <summary>Two levels differ by a change — note the return type.</summary>
    public static QuantityDelta operator -(Quantity later, Quantity earlier) =>
        Difference(earlier, later);

    public static Quantity operator *(Quantity magnitude, int factor) => Multiply(magnitude, factor);

    public static Quantity operator *(int factor, Quantity magnitude) => Multiply(magnitude, factor);

    /// <summary>
    /// Totals magnitudes — units across the lines of an order, say.
    ///
    /// <para>
    /// A named method rather than <c>operator +</c>, because adding two *levels*
    /// is meaningless and the type should not make it easy. Naming it is the
    /// whole safeguard: it reads as a deliberate act at the call site.
    /// </para>
    /// </summary>
    /// <param name="unitCode">The unit the total is in. Stated, so an empty sequence still has one.</param>
    /// <param name="magnitudes">Every one of which must be in that unit.</param>
    public static Quantity Sum(string unitCode, IEnumerable<Quantity> magnitudes)
    {
        ArgumentNullException.ThrowIfNull(magnitudes);

        var total = Zero(unitCode);

        foreach (var magnitude in magnitudes)
        {
            _ = AgreedUnit(total, magnitude, "total");
            total = new Quantity(checked(total.Thousandths + magnitude.Thousandths), total.Unit!);
        }

        return total;
    }

    // ----------------------------------------------------------- comparison

    /// <summary>
    /// Equal when both the amount and the unit match. Does not throw on a unit
    /// mismatch — 1 kg and 1 piece are simply not the same value — which keeps
    /// <see cref="Quantity"/> usable as a dictionary key.
    /// </summary>
    public bool Equals(Quantity other) =>
        Thousandths == other.Thousandths
        && string.Equals(Unit, other.Unit, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Quantity other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Thousandths, Unit is null ? 0 : StringComparer.Ordinal.GetHashCode(Unit));

    /// <exception cref="InvalidOperationException">The units differ.</exception>
    public int CompareTo(Quantity other)
    {
        _ = AgreedUnit(this, other, "compare");
        return Thousandths.CompareTo(other.Thousandths);
    }

    public static bool operator ==(Quantity left, Quantity right) => left.Equals(right);

    public static bool operator !=(Quantity left, Quantity right) => !left.Equals(right);

    public static bool operator <(Quantity left, Quantity right) => left.CompareTo(right) < 0;

    public static bool operator <=(Quantity left, Quantity right) => left.CompareTo(right) <= 0;

    public static bool operator >(Quantity left, Quantity right) => left.CompareTo(right) > 0;

    public static bool operator >=(Quantity left, Quantity right) => left.CompareTo(right) >= 0;

    // --------------------------------------------------------------- guards

    internal string RequireUnit()
    {
        if (Unit is null)
        {
            throw new InvalidOperationException(
                "This is default(Quantity), which has no unit. Build it with "
                + "Quantity.FromThousandths(value, unitCode) or UnitPrecision.Quantity(value).");
        }

        return Unit;
    }

    private static string AgreedUnit(Quantity left, Quantity right, string operation)
    {
        var unit = left.RequireUnit();
        var other = right.RequireUnit();

        if (!string.Equals(unit, other, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot {operation} {left} and {right}: different units. Convert through "
                + "units_of_measure.factor_to_base and say so, rather than adding the numbers.");
        }

        return unit;
    }

    private static string AgreedUnit(Quantity level, QuantityDelta change, string operation)
    {
        var unit = level.RequireUnit();
        var other = change.RequireUnit();

        if (!string.Equals(unit, other, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot {operation} {change} and {level}: different units. Convert through "
                + "units_of_measure.factor_to_base and say so, rather than adding the numbers.");
        }

        return unit;
    }

    public override string ToString() => Format(Thousandths, Unit, signed: false);

    internal static string Format(long thousandths, string? unit, bool signed)
    {
        var whole = thousandths / Scale;
        var fraction = Math.Abs(thousandths % Scale);

        var sign = thousandths switch
        {
            < 0 when whole == 0 => "-",
            > 0 when signed => "+",
            _ => string.Empty,
        };

        var digits = fraction == 0
            ? string.Empty
            : "." + fraction.ToString("D3", CultureInfo.InvariantCulture).TrimEnd('0');

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{whole}{digits} {unit ?? "(no unit)"}");
    }
}
