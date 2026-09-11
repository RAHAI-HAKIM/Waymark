namespace Waymark.Domain.Values;

/// <summary>
/// A rate in hundredths of a percent — 1900 is 19,00%.
///
/// <para>
/// The schema stores every percentage this way: <c>categories.tax_rate</c>,
/// <c>promotions.promotion_value</c> and <c>customer_tiers.discount</c> all
/// carry <c>CHECK (… BETWEEN 0 AND 10000)</c>. Wrapping it stops a raw 19 from
/// being passed where 1900 was meant, which is a mistake no test would catch
/// because both are plausible integers.
/// </para>
/// </summary>
public readonly struct BasisPoints : IEquatable<BasisPoints>, IComparable<BasisPoints>
{
    /// <summary>Basis points in 100%. Matches the schema's CHECK bound.</summary>
    public const int Scale = 10_000;

    /// <summary>Zero, which leaves an amount unchanged.</summary>
    public static BasisPoints Zero => default;

    /// <summary>Algeria's standard TVA rate, 19%.</summary>
    public static BasisPoints StandardVat { get; } = new(1_900);

    /// <summary>Algeria's reduced TVA rate, 9%.</summary>
    public static BasisPoints ReducedVat { get; } = new(900);

    /// <param name="value">Hundredths of a percent, 0 to 10000 inclusive.</param>
    public BasisPoints(int value)
    {
        if (value is < 0 or > Scale)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"A rate is 0 to {Scale} basis points (0% to 100%). This is the same bound the "
                + "schema's CHECK constraints carry, so a value outside it could not be stored "
                + "anyway.");
        }

        Value = value;
    }

    /// <summary>Hundredths of a percent. 1900 is 19,00%.</summary>
    public int Value { get; }

    /// <summary>True for a rate of zero.</summary>
    public bool IsZero => Value == 0;

    public int CompareTo(BasisPoints other) => Value.CompareTo(other.Value);

    public bool Equals(BasisPoints other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is BasisPoints other && Equals(other);

    public override int GetHashCode() => Value;

    public static bool operator ==(BasisPoints left, BasisPoints right) => left.Equals(right);

    public static bool operator !=(BasisPoints left, BasisPoints right) => !left.Equals(right);

    public static bool operator <(BasisPoints left, BasisPoints right) => left.Value < right.Value;

    public static bool operator <=(BasisPoints left, BasisPoints right) => left.Value <= right.Value;

    public static bool operator >(BasisPoints left, BasisPoints right) => left.Value > right.Value;

    public static bool operator >=(BasisPoints left, BasisPoints right) => left.Value >= right.Value;

    public override string ToString() =>
        FormattableString.Invariant($"{Value / 100m:0.##}%");
}
