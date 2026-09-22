using Waymark.Domain.Values;

namespace Waymark.Domain.Catalogue;

/// <summary>Where a line's TVA rate came from, which is not the same question as what it is.</summary>
public enum TvaRateSource
{
    /// <summary>
    /// The product's categories agreed on one rate, and every one of them stated it.
    /// The catalogue answered the question.
    /// </summary>
    FromCategory,

    /// <summary>
    /// D-075's catch-all: the catalogue did not answer, so the standard rate applies by law.
    /// <b>The figure is correct and the catalogue is not.</b> Nothing at the till can act on
    /// this — it is carried so Admin can list every product selling on the fallback (block E).
    /// </summary>
    StandardFallback,
}

/// <summary>The rate a line is taxed at, and how it was arrived at.</summary>
/// <param name="Rate">What TVA is extracted at, from the TTC price (D-033).</param>
/// <param name="Source">
/// Whether the catalogue said so, or the law did. A rate of 19% from
/// <see cref="TvaRateSource.StandardFallback"/> and one from
/// <see cref="TvaRateSource.FromCategory"/> charge the customer the same and mean
/// opposite things about the data.
/// </param>
public readonly record struct TvaRateResolution(BasisPoints Rate, TvaRateSource Source);

/// <summary>
/// <b>Session A1.</b> Which TVA rate a product is sold at, given what its
/// categories say — D-075's catch-all, and the answer to O-24.
///
/// <para>
/// Pure, and in Domain on purpose (the <c>NearExpiry</c> model, D-073): no database, no
/// clock, no store. It can be argued with in a test that opens nothing. Reading the rates
/// out of <c>product_category</c> and <c>categories</c> is Persistence's job, and the
/// standard rate itself is <see cref="BasisPoints.StandardVat"/> — 19%, Algeria's rate under
/// the CTCA, not a configurable number.
/// </para>
///
/// <para>
/// <b>Why this is the dangerous one.</b> Before D-075 a product whose categories disagreed
/// was refused at the till, loudly, and somebody fixed the catalogue. Now it sells. If the
/// fallback condition is written one step too wide — if it fires when the categories *did*
/// agree — every 9% product in the shop (bread, milk, pharmacy) is sold at 19%. Every receipt
/// still recomputes from its own row, every total still adds up, and nothing anywhere
/// complains. That is the silent kind of wrong CLAUDE.md §8 exists for, so the rule is one
/// function with its own tests rather than a condition inside a query.
/// </para>
/// </summary>
public static class TvaRate
{
    /// <summary>
    /// The rate for a product with these category rates, and where it came from.
    ///
    /// <para>
    /// The source is about <i>how</i>, never <i>what</i>: a product whose only category says
    /// 19% resolves to 19% <see cref="TvaRateSource.FromCategory"/>, and a product whose
    /// categories say [19%, null] resolves to the same 19% but
    /// <see cref="TvaRateSource.StandardFallback"/>. Same figure on the receipt, opposite
    /// verdicts on the data. Returning <see cref="TvaRateSource.FromCategory"/> whenever the
    /// rate happens to equal 19% would hide every miscategorised standard-rated product, and
    /// no total would ever look wrong.
    /// </para>
    /// </summary>
    /// <param name="categoryRates">
    /// One entry per category the product is linked to, in no particular order, null where
    /// that category carries no <c>tax_rate</c>. Duplicates are allowed and mean agreement.
    /// An empty list means the product is in no category at all.
    /// </param>
    /// <exception cref="ArgumentNullException">The list is null, which is not the same as empty.</exception>
    public static TvaRateResolution Resolve(IReadOnlyList<BasisPoints?> categoryRates)
    {
        ArgumentNullException.ThrowIfNull(categoryRates);

        if (categoryRates is not [BasisPoints FirstRate, ..])
        {
            return new TvaRateResolution(BasisPoints.StandardVat, TvaRateSource.StandardFallback);
        }

        foreach (BasisPoints? rate in categoryRates)
        {
            if(rate is null || !rate.Equals(FirstRate))
            {
                return new TvaRateResolution(BasisPoints.StandardVat, TvaRateSource.StandardFallback);
            }
        }
        return new TvaRateResolution(FirstRate, TvaRateSource.FromCategory);
    }
}
