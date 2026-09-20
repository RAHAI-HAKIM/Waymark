using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Waymark.Application.Engine;
using Waymark.Domain.Engine;
using Wire = Waymark.Contracts.Recommendations;

namespace Waymark.StoreServer.Engine;

/// <summary>
/// What the board answers with: who asked, what they may act on, and what was held back.
/// Named in snake_case like everything else on this wire, so one payload does not make a
/// TypeScript reader switch conventions halfway down (D-044).
/// </summary>
public sealed record BoardAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("staff_name")] string? StaffName,
    [property: JsonPropertyName("role_code")] string? RoleCode,
    [property: JsonPropertyName("withheld")] int Withheld,
    [property: JsonPropertyName("cards")] IReadOnlyList<Wire.RecommendationEnvelope> Cards);

/// <summary>What a decision answers with. A refusal is an answer, not an error (D-066, D-070).</summary>
public sealed record DecisionAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("decision_id")] string? DecisionId,
    [property: JsonPropertyName("reason")] string? Reason);

/// <summary>
/// The recommendation board and its decisions, on the wire (hop 7).
///
/// <para>
/// A card crosses as <see cref="Wire.RecommendationEnvelope"/>, which mirrors the
/// <c>recommendations</c> row rather than inventing a shape (D-044). The Because block and the
/// option payloads are stored as JSON text and are <b>re-parsed here</b> rather than passed
/// through as strings: a payload the store cannot read is one the UI cannot either, and it is
/// better to find that out on this side of the wire.
/// </para>
/// </summary>
public static class RecommendationWire
{
    public const string Answered = "answered";
    public const string UnknownStaff = "unknown_staff";
    public const string Recorded = "recorded";
    public const string Refused = "refused";

    public static BoardAnswer ToWire(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);

        return board.Staff is not { } staff
            ? new BoardAnswer(UnknownStaff, null, null, 0, [])
            : new BoardAnswer(
                Answered,
                staff.StaffName,
                staff.RoleCode,
                board.Withheld,
                [.. board.Cards.Select(ToEnvelope)]);
    }

    public static DecisionAnswer FromDecision(RecordedDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        return new DecisionAnswer(Recorded, decision.DecisionId, null);
    }

    public static DecisionAnswer Refusal(string reason) => new(Refused, null, reason);

    private static Wire.RecommendationEnvelope ToEnvelope(CardOnTheBoard card) => new(
        card.Card.RecommendationId,
        card.Card.StoreId,
        card.Card.RecommendationType,
        (Wire.Department)card.Card.Department,
        (Wire.Urgency)card.Card.Urgency,
        (Wire.ActionType)card.Card.ActionType,
        (Wire.RecommendationSubject)card.Card.SubjectType,
        card.Card.SubjectId,
        card.Card.Headline,
        JsonSerializer.Deserialize<Wire.BecauseBlock>(card.Card.BecauseJson)
            ?? throw new InvalidOperationException(
                $"Card {card.Card.RecommendationId} has a Because block that cannot be read. A card that "
                + "cannot say why it exists is not shown (CLAUDE.md §5)."),

        // The interval columns are stored integers and the contract carries exact decimal text.
        // Written as plain counts here because both are null on an expiry card (a count has no
        // range, D-073); the first card that carries an interval decides what scale it is in,
        // and that card does not exist yet.
        card.Card.IntervalLow?.ToString(CultureInfo.InvariantCulture),
        card.Card.IntervalHigh?.ToString(CultureInfo.InvariantCulture),

        card.Card.ComputedAt,
        card.Card.ParameterVersion is { } version ? checked((int)version) : null,
        card.Card.Source,
        card.Card.MinimumRequiredRole,
        (Wire.RecommendationStatus)card.Card.Status,
        card.Card.IssuedAt,
        card.Card.DeliveredAt,
        card.Card.ExpiresAt,
        [.. card.Options.Select(ToWire)]);

    private static Wire.RecommendationOption ToWire(RecommendationOption option) => new(
        option.OptionId,
        option.Label,
        checked((int)option.DisplayOrder),
        JsonSerializer.Deserialize<Wire.IntentPayload>(option.PayloadJson)
            ?? throw new InvalidOperationException(
                $"Option {option.OptionId} has a payload that cannot be read. An option is an executable "
                + "intent, not a label (D-044)."),
        // Projected value is money in the store's ledger currency, when an option has one.
        option.ProjectedValue is { } projected ? Waymark.Contracts.Figures.Amount(projected) : null);
}
