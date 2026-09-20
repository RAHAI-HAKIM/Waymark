using Waymark.Domain.Engine;

namespace Waymark.Application.Engine;

/// <summary>
/// What one staff member is shown, and why they were not shown the rest (hop 7).
/// </summary>
/// <param name="Staff">Who asked. Null when the store has no such active person.</param>
/// <param name="Cards">The cards they may act on, most urgent first.</param>
/// <param name="Withheld">
/// How many live cards were kept back because they are addressed above this person's rank.
/// <b>A count, never the cards themselves</b> — and it is reported rather than hidden, because
/// a cashier seeing "3 cards are for a manager" is the truth, while an empty board that looks
/// like a quiet shop is not.
/// </param>
public sealed record Board(StaffOnDuty? Staff, IReadOnlyList<CardOnTheBoard> Cards, int Withheld);

/// <summary>
/// The recommendation board for one staff member (hop 7): a read, not a command, so it stages
/// nothing and the executor never sees it.
///
/// <para>
/// The filtering is <see cref="CardAudience"/>'s — <b>the same rule the decision goes
/// through</b>. A screen that filtered by its own copy of the rule is how a card gets shown to
/// somebody who is then refused when they press the button, and worse, how one gets shown that
/// should not have been.
/// </para>
/// </summary>
public sealed class PendingCards(IRecommendationBoard board)
{
    public async Task<Board> ForAsync(string staffId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(staffId);

        if (await board.StaffAsync(staffId, cancellationToken) is not { } staff)
        {
            return new Board(null, [], 0);
        }

        var live = await board.LiveCardsAsync(cancellationToken);
        var mine = live.Where(card => CardAudience.MayDecide(staff.Rank, card.RequiredRank)).ToList();

        return new Board(staff, mine, live.Count - mine.Count);
    }
}
