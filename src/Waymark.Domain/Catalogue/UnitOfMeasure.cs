namespace Waymark.Domain.Catalogue;

/// <summary>
/// A unit a product can be sold in: piece, kilogram, litre.
///
/// <para>
/// A plain class. No attributes, no EF Core, no <c>using</c> that leaves this
/// project — <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1), and
/// the moment an entity carries a <c>[Table]</c> attribute that stops being
/// true. Everything about how this reaches SQLite lives in
/// <c>UnitOfMeasureConfiguration</c>.
/// </para>
/// </summary>
public sealed class UnitOfMeasure
{
    /// <summary>Primary key. A short human code — "kg", "pc" — not a ULID.</summary>
    public required string UnitCode { get; init; }

    public required string NameAr { get; init; }

    public required string NameFr { get; init; }

    public required UnitDimension Dimension { get; init; }

    /// <summary>
    /// The unit this one reduces to, or null when it is itself a base unit.
    /// Self-referencing: grams point at kilograms.
    /// </summary>
    public string? BaseUnitCode { get; init; }

    /// <summary>
    /// Conversion factor to <see cref="BaseUnitCode"/>, scaled by one million.
    /// Grams to kilograms is 1000, stored as 1_000_000_000.
    ///
    /// <para>
    /// <c>long</c>, not <c>int</c>. Scaled integers are exactly where 32 bits
    /// runs out quietly: this one already reaches 1e9 for a commonplace
    /// conversion, against an <c>int</c> ceiling of 2.1e9.
    /// </para>
    /// </summary>
    public long FactorToBase { get; init; } = 1_000_000;

    /// <summary>
    /// How many decimal places to show. Zero to three.
    ///
    /// <para>
    /// <c>int</c> here is right, and the contrast with the field above is the
    /// point: this is a small count with a fixed range, not a scaled quantity.
    /// The rule is not "never int" — it is "never int for anything scaled or
    /// summed".
    /// </para>
    /// </summary>
    public int DecimalPlaces { get; init; }

    public bool IsActive { get; init; } = true;

    public required DateTimeOffset CreatedAt { get; init; }
}
