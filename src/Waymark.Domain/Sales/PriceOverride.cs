using Waymark.Domain.Values;

namespace Waymark.Domain.Sales;

/// <summary>What <see cref="PriceOverride.Check"/> says of a price typed at the counter.</summary>
public enum OverrideVerdict
{
    /// <summary>Within the band: it may be charged.</summary>
    Accepted,

    /// <summary>Within the band, and below what the goods cost: it may be charged, and the till says so first.</summary>
    AcceptedBelowCost,

    /// <summary>The same as the price in force: there is nothing to override.</summary>
    Unchanged,

    /// <summary>Zero or less: nothing for free by an override; a free item is a 100 % discount (B4).</summary>
    NotAboveZero,

    /// <summary>More than the band allows above the price in force: the catalogue is corrected instead.</summary>
    AboveBand,
}

/// <summary>The verdict, and the highest price the band allows, for the till to say.</summary>
/// <param name="Ceiling">The most that may be charged: the price in force plus the band, never rounded up.</param>
public sealed record OverrideCheck(OverrideVerdict Verdict, Money Ceiling)
{
    /// <summary>Whether it may be charged.</summary>
    public bool MayCharge => Verdict is OverrideVerdict.Accepted or OverrideVerdict.AcceptedBelowCost;
}

/// <summary>
/// <b>Session B5</b> Whether a unit price typed at the counter may replace the price in
/// force (D-092).
///
/// <para><b>The rules <c>PriceOverrideTests</c> hold you to:</b></para>
/// <list type="number">
///   <item><description><b>Up to <see cref="MaximumRise"/> above the price in force</b>, 20 % today:
///   the <see cref="OverrideCheck.Ceiling"/> is <c>list × (1 + rise)</c> <b>rounded down</b> to the
///   centime, because a limit rounded up would let through a price above the limit. Anything above it
///   is <see cref="OverrideVerdict.AboveBand"/>. Compare exactly: no <c>double</c>, and no rounding in
///   the comparison itself.</description></item>
///   <item><description><b>Down to any price above zero.</b> Zero or less is
///   <see cref="OverrideVerdict.NotAboveZero"/>. The price in force itself is
///   <see cref="OverrideVerdict.Unchanged"/>: an override that changes nothing is not
///   recorded.</description></item>
///   <item><description><b>Below cost</b>: when <paramref name="unitCost"/> is known and the new price
///   is below it, <see cref="OverrideVerdict.AcceptedBelowCost"/>, which the till shows before the owner
///   confirms. At or above cost, or with no cost known, <see cref="OverrideVerdict.Accepted"/>. A cost
///   says nothing about the band: a price above the band is refused whatever it cost.</description></item>
///   <item><description>Prices of different currencies throw, as <see cref="Money"/> always does.</description></item>
/// </list>
/// <para>
/// Pure and in Domain, like <see cref="Discounts"/>: the till checks with it as the price is typed,
/// and the server checks again with it before anything is charged.
/// </para>
/// </summary>
public static class PriceOverride
{
    /// <summary>How far above the price in force an override may go: 20 % (D-092). A store setting at H2.</summary>
    public static BasisPoints MaximumRise { get; } = new(2_000);

    /// <param name="listPrice">The price in force (D-066, D-076): what the till would have charged.</param>
    /// <param name="newPrice">The price typed.</param>
    /// <param name="unitCost">What a unit cost the shop, from the batch it will be sold from; null when unknown.</param>
    public static OverrideCheck Check(Money listPrice, Money newPrice, Money? unitCost)
    {
        // The most that may be charged, rounded DOWN: a limit rounded up would let a centime past it.
        // Not a Rounding policy (it has no truncation, on purpose: policies are for charged amounts);
        // a bound is whole-number arithmetic, list × (10000 + rise) ÷ 10000, the division truncating.
        var Ceiling = Money.FromMinorUnits(
            checked(listPrice.MinorUnits * (BasisPoints.Scale + MaximumRise.Value) / BasisPoints.Scale), listPrice.Currency);
        if (newPrice <= Money.Zero(listPrice.Currency))
        {
            return new OverrideCheck(OverrideVerdict.NotAboveZero, Ceiling);
        }
        if(newPrice == listPrice)
        {
            return new OverrideCheck(OverrideVerdict.Unchanged, Ceiling);
        }
        if(newPrice > Ceiling)
        {
            return new OverrideCheck(OverrideVerdict.AboveBand, Ceiling);
        }
        if(unitCost is not null && newPrice < unitCost)
        {
            return new OverrideCheck(OverrideVerdict.AcceptedBelowCost, Ceiling);
        }
        return new OverrideCheck(OverrideVerdict.Accepted, Ceiling);
    }
}
