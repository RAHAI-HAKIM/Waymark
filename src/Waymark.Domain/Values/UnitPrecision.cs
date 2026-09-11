namespace Waymark.Domain.Values;

/// <summary>
/// How finely a unit of measure may be divided, and the only way to build a
/// quantity that is checked against it.
///
/// <para>
/// <c>units_of_measure.decimal_places</c> carries <c>CHECK (… BETWEEN 0 AND 3)</c>,
/// so a unit sold by the piece admits no fraction at all while one sold by
/// weight goes to the gram. That is a real invariant and it is cheap to enforce
/// at construction rather than discovering it at <c>SaveChanges</c>
/// (decisions.md D-036).
/// </para>
/// <para>
/// It is a separate type from <see cref="Waymark.Domain.Reference.UnitOfMeasure"/>
/// on purpose. That is a row in a table, mutable and loaded from the database;
/// this is the one fact about it that the arithmetic needs, in a shape a value
/// object can hold. Build it at the boundary:
/// <c>UnitPrecision.For(unit.UnitCode, (int)unit.DecimalPlaces)</c>.
/// </para>
/// </summary>
public readonly struct UnitPrecision : IEquatable<UnitPrecision>
{
    /// <summary>The finest a unit may be divided, matching the schema's CHECK.</summary>
    public const int MaxDecimalPlaces = 3;

    private readonly string? _code;
    private readonly int _decimalPlaces;

    private UnitPrecision(string code, int decimalPlaces)
    {
        _code = code;
        _decimalPlaces = decimalPlaces;
    }

    /// <param name="unitCode">The <c>unit_code</c>, exactly as it is stored.</param>
    /// <param name="decimalPlaces">0 to 3. From <c>units_of_measure.decimal_places</c>.</param>
    public static UnitPrecision For(string unitCode, int decimalPlaces)
    {
        if (string.IsNullOrWhiteSpace(unitCode))
        {
            throw new ArgumentException("A unit code is required.", nameof(unitCode));
        }

        if (decimalPlaces is < 0 or > MaxDecimalPlaces)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decimalPlaces),
                decimalPlaces,
                $"A unit is divided to at most {MaxDecimalPlaces} places, because quantities are "
                + $"stored in thousandths. This is the bound units_of_measure already carries.");
        }

        return new UnitPrecision(unitCode, decimalPlaces);
    }

    /// <summary>The unit code, or <c>(none)</c> for <c>default(UnitPrecision)</c>.</summary>
    public string Code => _code ?? "(none)";

    /// <summary>False only for <c>default(UnitPrecision)</c>.</summary>
    public bool IsDefined => _code is not null;

    /// <summary>0 for a unit sold whole, 3 for one measured to the gram.</summary>
    public int DecimalPlaces => _decimalPlaces;

    /// <summary>
    /// The smallest step this unit admits, in thousandths: 1000 for a whole
    /// unit, 1 for a gram.
    /// </summary>
    public long StepThousandths => _decimalPlaces switch
    {
        0 => 1_000,
        1 => 100,
        2 => 10,
        _ => 1,
    };

    /// <summary>Whether this many thousandths is a value the unit can actually take.</summary>
    public bool Allows(long thousandths) => thousandths % StepThousandths == 0;

    /// <summary>A quantity in this unit, refused if the unit cannot be divided that finely.</summary>
    public Quantity Quantity(long thousandths) =>
        Values.Quantity.FromThousandths(Require(thousandths), Code);

    /// <summary>
    /// A whole number of this unit — 3 pieces, 2 kilograms. Always admissible,
    /// whatever the precision, but still routed through the guard so a
    /// <c>default(UnitPrecision)</c> cannot quietly mint quantities in a unit
    /// called "(none)".
    /// </summary>
    public Quantity Whole(long units) => Quantity(checked(units * Values.Quantity.Scale));

    /// <summary>A change in this unit, refused if the unit cannot be divided that finely.</summary>
    public QuantityDelta Delta(long thousandths) =>
        QuantityDelta.FromThousandths(Require(thousandths), Code);

    private long Require(long thousandths)
    {
        if (!IsDefined)
        {
            throw new InvalidOperationException(
                "This is default(UnitPrecision). Build it with UnitPrecision.For(code, decimalPlaces).");
        }

        if (!Allows(thousandths))
        {
            throw new ArgumentOutOfRangeException(
                nameof(thousandths),
                thousandths,
                $"'{Code}' is sold to {DecimalPlaces} decimal places, so it cannot take this value. "
                + "A till that accepts 1,5 of something sold whole prints a receipt nobody can fill.");
        }

        return thousandths;
    }

    public bool Equals(UnitPrecision other) =>
        _decimalPlaces == other._decimalPlaces
        && string.Equals(_code, other._code, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is UnitPrecision other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(_code is null ? 0 : StringComparer.Ordinal.GetHashCode(_code), _decimalPlaces);

    public static bool operator ==(UnitPrecision left, UnitPrecision right) => left.Equals(right);

    public static bool operator !=(UnitPrecision left, UnitPrecision right) => !left.Equals(right);

    public override string ToString() => Code;
}
