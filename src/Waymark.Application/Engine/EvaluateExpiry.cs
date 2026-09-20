using System.Globalization;
using System.Text.Json;
using Waymark.Application.Commands;
using Waymark.Contracts;
using Waymark.Domain;
using Waymark.Domain.Engine;
using Waymark.Domain.Enums;
using Waymark.Domain.Work;
using Wire = Waymark.Contracts.Recommendations;

namespace Waymark.Application.Engine;

/// <summary>Look at every batch on the shelf and say which ones are running out of time (hop 6).</summary>
public sealed record EvaluateExpiry : ICommand<ExpiryEvaluation>;

/// <summary>What the run did. Counts, not cards: the cards are rows, and the board is read from the store.</summary>
/// <param name="BatchesRead">How many batches with stock it looked at.</param>
/// <param name="Flagged">How many cards it wrote.</param>
/// <param name="Superseded">How many earlier cards it replaced.</param>
/// <param name="WindowDays">The window it compared against.</param>
/// <param name="ParameterVersion">Which version of that window.</param>
/// <param name="ComputedAt">When, so the answer can be aged like the cards are.</param>
public sealed record ExpiryEvaluation(
    int BatchesRead, int Flagged, int Superseded, long WindowDays, long ParameterVersion, DateTimeOffset ComputedAt);

/// <summary>
/// The expiry evaluator (hop 6): the first thing in Waymark that offers an opinion.
///
/// <para>
/// <b>It compares and nothing more</b> (CLAUDE.md §5). It reads one parameter the engine owns
/// — the near-expiry window — reads what is on the shelf, subtracts two dates, adds up what
/// the remaining stock cost, and writes a card for each batch inside the window. It fits
/// nothing, aggregates no history and iterates over nothing. The rule itself is
/// <see cref="NearExpiry"/>, in Domain, so it can be argued with away from a database.
/// </para>
/// <para>
/// Every card carries the three things §5 makes non-negotiable: its <b>Because block</b>, at
/// most three factors and each one a figure; the <b>computed-at</b> time, so a stale card is
/// shown as stale rather than as fresh; and its <b>interval</b> — which here is deliberately
/// absent, because days on a calendar and units on a shelf are counts, not estimates, and the
/// envelope reserves a null interval for exactly that (D-044). The day an expiry card carries
/// a forecast — how much of it will sell before the date — it gets a range, and that is the
/// engine's work, not the store's.
/// </para>
/// <para>
/// <b>Nothing decides</b> (CLAUDE.md §4). The card proposes; a human accepts, adjusts or
/// dismisses it, in hop 7. Nothing here applies a markdown or writes off anything.
/// </para>
/// </summary>
public sealed class EvaluateExpiryHandler(
    IExpiryLedger ledger,
    IStaging staging,
    ICurrentStore store,
    IStoreCalendar calendar,
    TimeProvider clock) : ICommandHandler<EvaluateExpiry, ExpiryEvaluation>
{
    /// <summary>
    /// What wrote the card. Free text by design (D-044): the set belongs to whatever emits
    /// recommendations, and this one says plainly that a store-side rule did, not the engine.
    /// </summary>
    public const string Source = "store_rule:near_expiry:v1";

    /// <summary>The reasoning template the UI localises. The sentence lives in the UI, never here.</summary>
    public const string BecauseKey = "near_expiry";

    public async Task<ExpiryEvaluation> HandleAsync(
        EvaluateExpiry command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);

        var storeId = store.StoreId
            ?? throw new InvalidOperationException(
                "The expiry evaluator needs to know which store it is running for: nothing is scoped without it (CLAUDE.md §3.3).");

        // Absence is never zero (D-037). A missing window is not "flag nothing" and not "flag
        // everything": it is a store whose engine parameters were never installed, and the
        // honest answer is to refuse and say which parameter is missing.
        var window = await ledger.NearExpiryWindowAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"No current '{NearExpiry.ParameterCode}' in parameter_registry, so there is no window to compare against. "
                + "Install the cold-start placeholder (D-069) or let the engine fit one.");

        // Who the cards are addressed to. Read from the store's own roles rather than named
        // here (D-073): "manager" is a role code one shop uses and another does not.
        var role = await ledger.DecidingRoleAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "This store has no active role in roles, so there is nobody a card could be addressed to.");

        var now = clock.GetUtcNow();
        var today = calendar.Today;
        var batches = await ledger.BatchesWithStockAsync(cancellationToken);

        // The board as it stands, by subject. A re-run replaces a batch's card rather than
        // adding a second one the shopkeeper would have to reconcile by eye (D-044).
        var live = (await ledger.LiveRecommendationsAsync(NearExpiry.RecommendationType, cancellationToken))
            .ToDictionary(card => card.SubjectId, card => card.RecommendationId, StringComparer.Ordinal);

        var (flagged, superseded) = (0, 0);

        foreach (var batch in batches)
        {
            if (NearExpiry.Evaluate(batch, (int)window.Value, today) is not { } finding)
            {
                continue;
            }

            if (live.TryGetValue(batch.BatchId, out var previous))
            {
                ledger.Supersede(previous);
                superseded++;
            }

            Write(finding, window, storeId, role, now, context);
            flagged++;
        }

        return new ExpiryEvaluation(batches.Count, flagged, superseded, window.Value, window.Version, now);
    }

    /// <summary>Stages the card and the one thing it offers to do about it.</summary>
    private void Write(
        NearExpiryFinding finding,
        EngineParameter window,
        string storeId,
        string role,
        DateTimeOffset now,
        CommandContext context)
    {
        var recommendationId = context.NewId();

        staging.Add(new Recommendation
        {
            RecommendationId = recommendationId,
            StoreId = storeId,
            Department = Department.Inventory,
            RecommendationType = NearExpiry.RecommendationType,
            Urgency = finding.Urgency,
            ActionType = ActionType.Binary,
            SubjectType = RecommendationSubjectType.Batch,
            SubjectId = finding.Batch.BatchId,
            Headline = Headline(finding),
            BecauseJson = JsonSerializer.Serialize(Because(finding)),

            // No interval: see the class comment. A count is not an estimate, and a null here
            // means "this figure has no range", never "no range was computed".
            IntervalLow = null,
            IntervalHigh = null,

            ComputedAt = now,
            ParameterVersion = window.Version,
            Source = Source,
            MinimumRequiredRole = role,
            Status = RecommendationStatus.Pending,
            IssuedAt = now,
        });

        var (label, intent) = finding.DaysToExpiry < 0
            // Past the date, a markdown is not a cheaper sale, it is an illegal one. The only
            // thing left to offer is taking the stock off the shelf.
            ? ("Sortir le lot du stock", "write_off_batch")
            : ("Appliquer une démarque", "apply_markdown");

        staging.Add(new RecommendationOption
        {
            OptionId = context.NewId(),
            RecommendationId = recommendationId,
            Label = label,
            DisplayOrder = 1,

            // An executable intent, not a description (D-044): it names an Application command
            // and its arguments. Nothing in Phase 0.5 carries it out — accepting a card records
            // the decision and stops there (D-069).
            PayloadJson = JsonSerializer.Serialize(new Wire.IntentPayload(
                intent,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["batch_id"] = finding.Batch.BatchId,
                    ["product_id"] = finding.Batch.ProductId,
                })),

            // What choosing it is worth is a projection, and projecting is the engine's job.
            ProjectedValue = null,
        });
    }

    /// <summary>
    /// The reasoning: structured, never a sentence (D-044). Three factors at most, each one a
    /// figure with its unit, as exact decimal text so nothing becomes a double in Python or
    /// TypeScript on the way to a screen.
    /// </summary>
    private static Wire.BecauseBlock Because(NearExpiryFinding finding)
    {
        var factors = new List<Wire.BecauseFactor>
        {
            new(
                "days_to_expiry",
                finding.DaysToExpiry.ToString(CultureInfo.InvariantCulture),
                "days",
                Wire.FactorDirection.Supports),
        };

        if (finding.OnHand is { } onHand)
        {
            factors.Add(new Wire.BecauseFactor(
                "units_on_hand",
                Figures.Quantity(onHand.Thousandths),
                onHand.Unit!,
                Wire.FactorDirection.Supports));
        }

        // What it cost, not what it would sell for: the money already spent is the part that is
        // actually at risk. Context rather than Supports — it says how much the first two
        // factors matter, and on its own it argues for nothing.
        factors.Add(new Wire.BecauseFactor(
            "value_at_cost",
            Figures.Amount(finding.ValueAtCost.MinorUnits),
            finding.ValueAtCost.Currency.Code,
            Wire.FactorDirection.Context));

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["product_name"] = finding.Batch.ProductName,
            ["expires_on"] = finding.Batch.ExpiresOn!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        return new Wire.BecauseBlock(BecauseKey, parameters, factors);
    }

    /// <summary>
    /// The card's sentence, in French, which is the store's language in the slice. <b>A
    /// rendering of the reasoning, never a substitute for it</b>: the UI localises from the
    /// Because block, and this exists so a card is readable before there is a UI.
    ///
    /// <para>
    /// It states a fact and leaves the action to the option (brand voice): "expires in three
    /// days", not "mark this down". There is no language column on <c>stores</c> yet, so the
    /// language is a placeholder like the window is.
    /// </para>
    /// </summary>
    private static string Headline(NearExpiryFinding finding)
    {
        var days = finding.DaysToExpiry;
        var when = days switch
        {
            < -1 => string.Create(CultureInfo.InvariantCulture, $"périmé depuis {-days} jours"),
            -1 => "périmé depuis hier",
            0 => "péremption aujourd'hui",
            1 => "péremption demain",
            _ => string.Create(CultureInfo.InvariantCulture, $"péremption dans {days} jours"),
        };

        var stock = finding.OnHand is { } onHand
            ? string.Create(CultureInfo.InvariantCulture, $", {Figures.Quantity(onHand.Thousandths)} en stock")
            : string.Empty;

        return string.Create(CultureInfo.InvariantCulture, $"{finding.Batch.ProductName} : {when}{stock}");
    }
}
