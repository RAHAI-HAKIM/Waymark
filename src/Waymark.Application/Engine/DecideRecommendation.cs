using Waymark.Application.Commands;
using Waymark.Domain.Engine;
using Waymark.Domain.Enums;
using Waymark.Domain.Work;

namespace Waymark.Application.Engine;

/// <summary>A human's answer to a card (hop 7).</summary>
/// <param name="RecommendationId">Which card.</param>
/// <param name="StaffId">Who answered. Named on the request; no login until Phase 1 (D-069).</param>
/// <param name="Decision">Accept, or dismiss. Adjust and snooze are Phase 1.</param>
/// <param name="ChosenOptionId">Which option was taken. Required for an accept, forbidden for a dismissal.</param>
public sealed record DecideRecommendation(
    string RecommendationId, string StaffId, Decision Decision, string? ChosenOptionId)
    : ICommand<RecordedDecision>;

/// <summary>What was written.</summary>
/// <param name="DecisionId">The decision's row.</param>
/// <param name="RecommendationId">The card it closed.</param>
/// <param name="Decision">What the person chose.</param>
/// <param name="DecidedBy">Who they are.</param>
/// <param name="DecidedAt">When.</param>
public sealed record RecordedDecision(
    string DecisionId, string RecommendationId, Decision Decision, string DecidedBy, DateTimeOffset DecidedAt);

/// <summary>The decision cannot be recorded, for a reason the person is told. Nothing is written.</summary>
public sealed class DecisionRefusedException(string reason) : Exception(reason);

/// <summary>
/// Records what a human decided about a card (hop 7, the Integration Layer's store half).
///
/// <para>
/// <b>Nothing decides automatically</b> (CLAUDE.md §4). This is the one door a recommendation
/// leaves by, and it needs a person on the other side of it: a staff member the store employs,
/// senior enough for the card, answering a card that is still open.
/// </para>
/// <para>
/// Two rows go in together or neither does (D-050): the <c>recommendation_decisions</c> row,
/// and the card's move to <c>decided</c>. A decision whose card stayed pending would be
/// answered twice; a card marked decided with no decision row would be a suggestion that
/// vanished with no record of who closed it — and the decision table is the audit trail, so
/// losing that half is losing the accountability the whole surface exists for.
/// </para>
/// <para>
/// <b>Accepting does not carry the intent out</b> (D-069): no price is marked down, no stock
/// is written off. The option's payload says what would be done, and Phase 1 does it, setting
/// <c>applied_at</c> and the resulting entity.
/// </para>
/// </summary>
public sealed class DecideRecommendationHandler(
    IRecommendationBoard board,
    IStaging staging,
    TimeProvider clock) : ICommandHandler<DecideRecommendation, RecordedDecision>
{
    public async Task<RecordedDecision> HandleAsync(
        DecideRecommendation command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RecommendationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.StaffId);

        // 0. Is this a decision the slice takes at all? A question about the request, not about
        //    the person, so it is answered before anything is looked up. Adjust needs an
        //    amended payload and snooze a date, and both are CHECKs with nothing to satisfy
        //    them here (D-069).
        if (command.Decision is Decision.Adjust or Decision.Snooze)
        {
            throw new DecisionRefusedException(
                $"{command.Decision} is not offered in Phase 0.5: a card is accepted or dismissed (D-069).");
        }

        // 1. Who is asking. An unknown or departed staff member is refused, never treated as
        //    the most junior person in the shop.
        var staff = await board.StaffAsync(command.StaffId, cancellationToken)
            ?? throw new DecisionRefusedException(
                $"{command.StaffId} is not an active staff member of this store, or their role is not an active role.");

        // 2. Which card.
        var card = await board.CardAsync(command.RecommendationId, cancellationToken)
            ?? throw new DecisionRefusedException($"There is no card {command.RecommendationId} in this store.");

        // 3. May they? The role check, before anything about the request is inspected further
        //    and long before anything is staged (D-074).
        if (!CardAudience.MayDecide(staff.Rank, card.RequiredRank))
        {
            throw new DecisionRefusedException(
                $"This card is for {card.Card.MinimumRequiredRole}; {staff.StaffName} is {staff.RoleCode}.");
        }

        // 4. Is it still open. A card already answered is not answered again: the decision
        //    table is append-only, and a second row would make "what was decided" a question
        //    with two answers.
        if (card.Card.Status is not (RecommendationStatus.Pending or RecommendationStatus.Delivered))
        {
            throw new DecisionRefusedException(
                $"That card is {card.Card.Status.ToString().ToLowerInvariant()}, not waiting for an answer.");
        }

        // 5. Is the option one of this card's. An option id from another card would pass the
        //    foreign key and record a decision about the wrong thing.
        var option = Option(command, card);

        var now = clock.GetUtcNow();
        var decisionId = context.NewId();

        staging.Add(new RecommendationDecision
        {
            DecisionId = decisionId,
            RecommendationId = card.Card.RecommendationId,
            Decision = command.Decision,
            ChosenOptionId = option,

            // Adjust and snooze need a payload and a date respectively, by CHECK, and neither
            // is in the slice (D-069). The wire refuses them before they reach here.
            AdjustedPayloadJson = null,
            SnoozeUntil = null,

            Origin = Origin.Store,
            DecidedBy = staff.StaffId,
            DecidedAt = now,

            // No terminal: this is Local Admin on the shop's LAN, not a till.
            TerminalId = null,

            // Not applied, and nothing produced by it: accepting records the answer and stops
            // there in Phase 0.5.
            AppliedAt = null,
            ResultingEntityType = null,
            ResultingEntityId = null,
        });

        board.MarkDecided(card.Card.RecommendationId);

        return new RecordedDecision(decisionId, card.Card.RecommendationId, command.Decision, staff.StaffId, now);
    }

    /// <summary>The chosen option, checked against the card it is supposed to belong to.</summary>
    private static string? Option(DecideRecommendation command, CardOnTheBoard card)
    {
        if (command.Decision == Decision.Dismiss)
        {
            return command.ChosenOptionId is null
                ? null
                : throw new DecisionRefusedException("A dismissal chooses no option.");
        }

        if (command.ChosenOptionId is not { } chosen)
        {
            throw new DecisionRefusedException("An acceptance has to say which option was taken.");
        }

        return card.Options.Any(available => string.Equals(available.OptionId, chosen, StringComparison.Ordinal))
            ? chosen
            : throw new DecisionRefusedException($"Option {chosen} is not on this card.");
    }
}
