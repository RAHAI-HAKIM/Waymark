using Waymark.Domain.Statistics;

namespace Waymark.Application.Statistics;

/// <summary>
/// Tier 2's stub for Phase 0.5 (D-065): it takes a sale and keeps nothing.
///
/// <para>
/// It exists so the hop is wired and the real writer has a shape to replace, and so nobody
/// mistakes "tier 2 is not written yet" for "tier 2 is not part of the path". Phase 2
/// replaces it with the DuckDB writer, encrypted (O-23).
/// </para>
/// </summary>
public sealed class NullTier2Writer : ITier2Writer
{
    public void Record(Tier2Sale sale) => ArgumentNullException.ThrowIfNull(sale);
}
