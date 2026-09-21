using Waymark.Domain.Values;

namespace Waymark.Domain.Statistics;

/// <summary>One sold line as tier 2 keeps it: transaction grain, the variant sold (D-043).</summary>
public sealed record Tier2Line(string VariantId, Quantity Quantity, Money LineTotal);

/// <summary>A completed sale, as tier 2 keeps it. No subject: sales in the skeleton have no customer (D-069).</summary>
public sealed record Tier2Sale(string TransactionId, DateOnly Date, int HourOfDay, IReadOnlyList<Tier2Line> Lines);

/// <summary>
/// Statistics tier 2: full fidelity, transaction grain, <b>local</b> and never sent (D-043).
///
/// <para>
/// <b>Stubbed in Phase 0.5</b> (D-065): the port exists so the sale has somewhere to hand
/// its figures, and the implementation stores nothing. Phase 2 writes the real DuckDB one,
/// encrypted (O-23), decides whether the transform runs per sale or nightly, and adds the
/// pseudonym for sales that have a customer.
/// </para>
/// </summary>
public interface ITier2Writer
{
    /// <summary>Takes a completed sale. Called inside the sale's command, after its rows are staged.</summary>
    void Record(Tier2Sale sale);
}
