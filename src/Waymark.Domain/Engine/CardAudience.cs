namespace Waymark.Domain.Engine;

/// <summary>
/// Who is allowed to act on a recommendation — the Integration Layer's role check (hop 7).
///
/// <para>
/// <see cref="MayDecide"/>.
/// </para>
/// <para>
/// It is a pure function in Domain, like <see cref="NearExpiry"/>, for one reason: a rule
/// about who may see what is the kind that gets quietly duplicated — once in the API, once in
/// the UI, once in a report — and the copies drift. There is one of it, and everything that
/// asks the question calls it.
/// </para>
/// </summary>
public static class CardAudience
{
    /// <summary>
    /// Whether a staff member of <paramref name="staffRank"/> may see and decide a card
    /// addressed to <paramref name="requiredRank"/>.
    ///
    /// <para>
    /// Both numbers are <c>roles.rank</c>, where a higher
    /// number is more senior (<c>cashier</c> 1, <c>manager</c> 2, <c>owner</c> 3 in the
    /// synthetic store). A card written by the expiry evaluator carries the rank of the rung
    /// above the shop floor (D-073), so a manager and an owner both pass and a cashier does
    /// not — which is the rule D-069 settled.
    /// </para>
    /// <para>
    /// <b>The decision inside it</b>, is the
    /// audience "this rank exactly" or "this rank and anything above it"? Waymark's answer is
    /// the second — an owner who could not act on a card a manager can act on would be a shop
    /// where the person in charge is the one locked out.
    /// </para>
    /// <para>
    /// </para>
    /// </summary>
    /// <param name="staffRank">The rank of the person asking, from <c>roles.rank</c>.</param>
    /// <param name="requiredRank">The rank on the card, from <c>recommendations.minimum_required_role</c>.</param>
    public static bool MayDecide(long staffRank, long requiredRank)
    {
        return staffRank >= requiredRank;
    }
}
