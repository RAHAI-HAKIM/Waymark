using System.Globalization;
using Waymark.Contracts.Pos;
using Waymark.Contracts.Recommendations;
using Waymark.Domain.Values;
using Waymark.Pos.Checkout;

namespace Waymark.Pos.Screen;

/// <summary>
/// The tone of a label. <b>There is no positive tone</b> (CLAUDE.md §6, G1 kit §9): violet
/// already confirms, and a shelf that is fine gets no card. Leaving it out of the type means no
/// view can ask for one.
/// </summary>
public enum Tone
{
    Neutral,
    Warning,
    Critical,
}

/// <summary>What the till knows, gathered for one frame. Nothing here is computed; see <see cref="TillScreen.Build"/>.</summary>
/// <param name="Text">The till's language.</param>
/// <param name="Zone">The till's time zone, for every clock on screen.</param>
/// <param name="Now">This moment.</param>
/// <param name="Context">The store, the till and the person selling; null until StoreServer has said.</param>
/// <param name="SelectedVariantId">The line the cashier touched, if any.</param>
/// <param name="Board">The signed-in person's Almanac board; null until fetched, or when there is none to show.</param>
/// <param name="CardIndex">Which of the board's cards is on screen.</param>
/// <param name="Unconfirmed">A sale with no usable answer, until the cashier acknowledges it (D-085).</param>
public sealed record ScreenState(
    TillText Text,
    TimeZoneInfo Zone,
    DateTimeOffset Now,
    Cart Cart,
    TillNotice? Notice,
    PaidTicket? Paid,
    SaleOutcome? LastSale,
    DateTimeOffset? LastSaleAt,
    ServerState Server,
    TillContext? Context,
    string? SelectedVariantId,
    BoardAnswer? Board,
    int CardIndex,
    UnconfirmedSale? Unconfirmed = null);

/// <summary>
/// Everything the till shows, decided (session A4, G1). The window draws this and decides
/// nothing: every rule a cashier would notice being wrong lives here, where it is tested
/// without a window.
/// </summary>
public sealed record TillScreen(
    bool RightToLeft,
    TopBar Top,
    NoticeLine Notice,
    CartView Cart,
    Rail Rail,
    BottomBar Bottom)
{
    /// <summary>The key for "Encaisser". Every action has its F key (G1 kit §9).</summary>
    public const string CollectKey = "F12";

    /// <summary>The key for "Retirer la ligne".</summary>
    public const string RemoveLineKey = "F8";

    public static TillScreen Build(ScreenState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var text = state.Text;

        return new TillScreen(
            text.RightToLeft,
            TopBarOf(state),
            NoticeOf(state),
            CartOf(state),
            RailOf(state),
            BottomOf(state));
    }

    /// <summary>
    /// Which regions of <paramref name="next"/> differ from the frame the window drew last; all of
    /// them when it has drawn none. The window redraws only those, so a key is not replaced under
    /// the cashier's finger, nor a focus lost, by a redraw that changed something else — the clock
    /// changes the top bar once a minute, and nothing more.
    ///
    /// <para>
    /// Records compare by value but their lists by reference, so each list is compared by its
    /// items here, and the rest of its record by the record. A list a later session adds, and does
    /// not add here, compares by reference: its region redraws on every frame, which costs a redraw
    /// and never leaves a stale one.
    /// </para>
    /// </summary>
    public static FrameChanges Compare(TillScreen? drawn, TillScreen next)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (drawn is null || drawn.RightToLeft != next.RightToLeft)
        {
            return FrameChanges.All;
        }

        return new FrameChanges(
            Top: drawn.Top != next.Top,
            Notice: drawn.Notice != next.Notice,
            Cart: !Same(drawn.Cart, next.Cart),
            Rail: !Same(drawn.Rail, next.Rail),
            Bottom: !Same(drawn.Bottom, next.Bottom));
    }

    private static bool Same(CartView drawn, CartView next) =>
        drawn.Columns.SequenceEqual(next.Columns)
        && drawn.Lines.Count == next.Lines.Count
        && drawn.Lines.Zip(next.Lines).All(pair =>
            pair.First.Chips.SequenceEqual(pair.Second.Chips) && pair.First with { Chips = pair.Second.Chips } == pair.Second)
        && drawn with { Columns = next.Columns, Lines = next.Lines } == next;

    private static bool Same(Rail drawn, Rail next) => (drawn, next) switch
    {
        (Rail.Paid a, Rail.Paid b) => a.Figures.SequenceEqual(b.Figures) && a with { Figures = b.Figures } == b,
        _ => drawn == next,
    };

    private static bool Same(BottomBar drawn, BottomBar next) =>
        drawn.Summary.SequenceEqual(next.Summary) && drawn with { Summary = next.Summary } == next;

    // ============================================================= top bar

    private static TopBar TopBarOf(ScreenState state)
    {
        var text = state.Text;
        var context = state.Context is { Outcome: TillContextOutcome.Found } found ? found : null;

        var tab = state.Paid is { } paid
            ? new TicketTab(text.TicketNumber(paid.Outcome.InvoiceNumber ?? "?"), text.Paid)
            : state.Cart.Lines.Count > 0
                ? new TicketTab(text.CurrentTicket, text.Lines(state.Cart.ActiveLines.Count))
                : new TicketTab(text.NewTicket, text.Empty);

        var connection = state.Server.IsReachable
            ? new Connection(text.Online, Tone.Neutral)
            : new Connection(text.Offline, Tone.Critical);

        var staff = context?.StaffName is { } name
            ? new StaffChip(name, RoleLabel(context, text))
            : null;

        return new TopBar(
            context is null ? null : $"{context.StoreName} · {context.TerminalName}",
            tab,
            connection,
            staff,
            DisplayFigures.Clock(Local(state, state.Now)));
    }

    private static string RoleLabel(TillContext context, TillText text) => text.Language == TillLanguage.Arabic
        ? context.RoleLabelAr ?? string.Empty
        // Invariant casing: the till runs with InvariantGlobalization (D-067), so no named culture
        // exists, and invariant upper-casing still takes "é" to "É".
        : (context.RoleLabelFr ?? string.Empty).ToUpperInvariant();

    // ======================================================== notice slot

    /// <summary>
    /// The one line under the search field (G1, "Avis"): fixed height, never above the cart, so
    /// a notice moves no line. What went wrong comes first, then what just happened.
    /// </summary>
    private static NoticeLine NoticeOf(ScreenState state)
    {
        var text = state.Text;

        switch (state.Notice)
        {
            case { Kind: TillNoticeKind.UnknownCode } unknown:
                return new NoticeLine(Tone.Warning, text.UnknownCode, text.UnknownCodeDetail(unknown.Code));

            case { Kind: TillNoticeKind.NotSellable } refused:
                return new NoticeLine(Tone.Critical, text.NotSellable, $"{refused.Code} — {text.NotSellableReason(refused.Detail)}");

            case { Kind: TillNoticeKind.ServerUnavailable }:
                return Offline(state);

            case { Kind: TillNoticeKind.SaleRefused } saleRefused:
                return new NoticeLine(Tone.Critical, text.SaleRefused, saleRefused.Detail);

            case { Kind: TillNoticeKind.SwitchRefused }:
                return new NoticeLine(Tone.Warning, text.TicketInProgress, text.FinishBeforeSwitching);

            case { Kind: TillNoticeKind.NotSignedIn }:
                return new NoticeLine(Tone.Warning, text.SessionEnded, text.SessionEndedDetail);

            // An unconfirmed sale takes the rail (RailOf), where its instructions fit; the slot
            // goes on saying what it would otherwise say.
        }

        if (!state.Server.IsReachable)
        {
            return Offline(state);
        }

        if (state.Paid is { } paid)
        {
            return new NoticeLine(Tone.Neutral, text.SaleRecorded, text.SaleRecordedLine(paid.Outcome.InvoiceNumber ?? "?"));
        }

        if (state.Cart.LastAdded is { } last)
        {
            return new NoticeLine(Tone.Neutral, text.LastArticle, $"{Article(last)} · {DisplayFigures.Amount(last.UnitPrice)}");
        }

        if (state.Cart.Lines.Count == 0 && state.LastSale is { } sale && state.LastSaleAt is { } at)
        {
            return new NoticeLine(
                Tone.Neutral,
                text.Ready,
                text.LastSaleLine(sale.InvoiceNumber ?? "?", DisplayFigures.Clock(Local(state, at)), Figure(sale.TotalTtc, sale.Currency)));
        }

        return new NoticeLine(Tone.Neutral, text.Ready, string.Empty);
    }

    private static NoticeLine Offline(ScreenState state) => new(
        Tone.Critical,
        state.Text.Offline,
        state.Text.OfflineSince(DisplayFigures.Clock(Local(state, state.Server.UnreachableSince ?? state.Now))));

    // =============================================================== cart

    private static CartView CartOf(ScreenState state)
    {
        var text = state.Text;
        var paid = state.Paid is not null;
        var lines = state.Paid?.Lines ?? state.Cart.Lines;

        var rows = lines.Select(line =>
        {
            var selected = !paid && !line.IsRemoved && line.VariantId == state.SelectedVariantId;
            return new LineRow(
                line.VariantId,
                DisplayFigures.Count(line.Count),
                Article(line),
                ChipsOf(line, text, state),
                DisplayFigures.Amount(line.UnitPrice),
                DisplayFigures.Amount(line.LineTotal),
                line.IsRemoved,
                selected);
        }).ToList();

        var empty = rows.Count == 0 ? new EmptyState(text.EmptyTitle, text.EmptyHint) : null;

        return new CartView(
            [text.ColumnQuantity, text.ColumnArticle, text.ColumnUnitPrice, text.ColumnTotal],
            rows,
            empty,
            rows.Any(row => row.Selected) ? new LineActions(text.RemoveLine, RemoveLineKey) : null);
    }

    /// <summary>
    /// Labels on a line, word first (G1 kit §5, "le libellé d'abord"). A removed line says only
    /// that it was removed: the stock or the promotion no longer apply to it.
    /// </summary>
    private static List<Chip> ChipsOf(CartLine line, TillText text, ScreenState state)
    {
        if (line.IsRemoved)
        {
            return [new Chip(Tone.Neutral, text.Removed)];
        }

        var chips = new List<Chip>();
        if (line.ExceedsStockOnHand)
        {
            chips.Add(new Chip(Tone.Warning, text.BeyondRecordedStock));
        }

        if (line.IsPromotionalPrice)
        {
            chips.Add(new Chip(Tone.Neutral, text.PromotionalPrice));
        }

        return chips;
    }

    private static string Article(CartLine line) => $"{line.ProductName} {line.VariantName}";

    // =============================================================== rail

    private static Rail RailOf(ScreenState state)
    {
        var text = state.Text;

        if (state.Unconfirmed is { } unknown)
        {
            // No "Réessayer": nothing makes a second request harmless yet, and a sale the server
            // did record would be recorded twice (O-27). The cashier checks, then says so (D-085).
            return new Rail.Unconfirmed(
                text.SaleNotConfirmed, text.SaleNotConfirmedTitle, text.SaleNotConfirmedBody, unknown.Why, text.AcknowledgeUnconfirmed);
        }

        if (state.Paid is { } paid)
        {
            var outcome = paid.Outcome;
            var total = Money(outcome.TotalTtc, outcome.Currency);
            var cash = Money(outcome.CashToCollect, outcome.Currency);
            var staff = state.Context?.StaffName;
            var when = DisplayFigures.Clock(Local(state, paid.At));

            return new Rail.Paid(
                text.SaleRecorded,
                text.TicketNumber(outcome.InvoiceNumber ?? "?"),
                staff is null ? when : $"{when} · {staff}",
                [
                    new Figure(text.TicketTotal, total is { } t ? DisplayFigures.Amount(t) : "?"),
                    new Figure(text.CashRounding, total is { } a && cash is { } b ? DisplayFigures.Amount(b - a) : "?"),
                    new Figure(text.CashDue, cash is { } c ? DisplayFigures.Amount(c) : "?"),
                ],
                text.NextScanOpensTicket);
        }

        return new Rail.Rest(AlmanacOf(state));
    }

    /// <summary>
    /// The one Almanac slot at the till (G1 kit §7): the board of whoever is signed in, never a
    /// figure recomputed from this ticket (CLAUDE.md §5). Cards above their rank are counted,
    /// never shown (D-074). No board, no slot.
    /// </summary>
    private static AlmanacSlot? AlmanacOf(ScreenState state)
    {
        if (state.Board is not { Outcome: BoardOutcome.Answered } board)
        {
            return null;
        }

        var text = state.Text;
        var awaiting = board.Withheld > 0 ? text.CardsAwaitingManager(board.Withheld) : null;

        if (board.Cards.Count == 0)
        {
            return new AlmanacSlot.Quiet(text.NoCardsForYou, awaiting);
        }

        var index = ((state.CardIndex % board.Cards.Count) + board.Cards.Count) % board.Cards.Count;
        var card = board.Cards[index];
        var option = card.Options.OrderBy(o => o.DisplayOrder).FirstOrDefault();
        var (claim, detail) = Claim(card, text);

        return new AlmanacSlot.Card(
            card.RecommendationId,
            text.AlmanacKind(card.RecommendationType),
            $"{index + 1} / {board.Cards.Count}",
            claim,
            detail,
            option is null ? null : new CardAction(option.OptionId, text.IntentAction(option.Payload.Command, option.Label)),
            // D-074 records accept and dismiss only. Adjust is on the card because the brand says
            // a suggestion has three answers, and it is unavailable until a decision says how an
            // adjusted payload is recorded.
            new CardAction(null, text.Adjust),
            new CardAction(null, text.Dismiss),
            awaiting);
    }

    /// <summary>
    /// A card's sentence, written from its Because block in the till's language (D-044): the
    /// headline is the server's French rendering and stands in only for a card this till does
    /// not know how to say.
    /// </summary>
    private static (string Claim, string? Detail) Claim(RecommendationEnvelope card, TillText text)
    {
        var because = card.Because;
        if (because.Key != "near_expiry"
            || !because.Params.TryGetValue("product_name", out var product)
            || !because.Params.TryGetValue("expires_on", out var expires)
            || Factor(because, "days_to_expiry") is not { } days
            || !int.TryParse(days.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var daysToExpiry)
            || Factor(because, "value_at_cost") is not { } value
            || Money(value.Value, value.Unit) is not { } cost
            || !DateOnly.TryParseExact(expires, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresOn))
        {
            return (card.Headline, null);
        }

        var units = Factor(because, "units_on_hand") is { } onHand ? Decimal(onHand.Value) : null;

        return (
            text.NearExpiryClaim(product, daysToExpiry),
            text.NearExpiryDetail(units, DisplayFigures.AmountWithCurrency(cost, text), DisplayFigures.DayAndMonth(expiresOn)));
    }

    private static BecauseFactor? Factor(BecauseBlock because, string key) =>
        because.Factors.FirstOrDefault(factor => factor.LabelKey == key);

    // ========================================================= bottom bar

    private static BottomBar BottomOf(ScreenState state)
    {
        var text = state.Text;

        if (state.Paid is { } paid)
        {
            var outcome = paid.Outcome;
            var total = Money(outcome.TotalTtc, outcome.Currency);
            var cash = Money(outcome.CashToCollect, outcome.Currency);

            return new BottomBar(
                [
                    new Figure(text.TicketTotal, total is { } t ? DisplayFigures.Amount(t) : "?"),
                    new Figure(text.CashRounding, total is { } a && cash is { } b ? DisplayFigures.Amount(b - a) : "?"),
                ],
                text.CashDue,
                cash is { } c ? DisplayFigures.AmountWithCurrency(c, text) : "?",
                new PrimaryKey(text.NewSale, null, text.EnterKey, Enabled: true));
        }

        var currency = CurrencyOf(state);
        var totalDue = state.Cart.Total ?? (currency is { } known ? Domain.Values.Money.Zero(known) : (Money?)null);
        // Not while a sale is unconfirmed: Encaisser would send it again (D-085).
        var canCollect = state.Cart.ActiveLines.Count > 0 && state.Unconfirmed is null;

        // What the drawer takes: the total rounded to the cash step, by the same function the
        // server uses (Money.ToCashTender, D-034), so the button and the receipt agree.
        var cashDetail = totalDue is { } due
            ? text.CashAmount(DisplayFigures.AmountWithCurrency(due.ToCashTender().Tendered, text))
            : null;

        return new BottomBar(
            [
                new Figure(text.Subtotal, totalDue is { } s ? DisplayFigures.Amount(s) : "—"),
                new Figure(text.Discounts, totalDue is { } z ? DisplayFigures.Amount(Domain.Values.Money.Zero(z.Currency)) : "—"),
            ],
            text.TotalToPay,
            totalDue is { } shown ? DisplayFigures.AmountWithCurrency(shown, text) : "—",
            new PrimaryKey(text.Collect, cashDetail, CollectKey, canCollect));
    }

    private static Currency? CurrencyOf(ScreenState state)
    {
        if (state.Cart.Lines.Count > 0)
        {
            return state.Cart.Lines[0].UnitPrice.Currency;
        }

        return state.Context?.Currency is { } code && Currency.TryFromCode(code, out var currency) ? currency : null;
    }

    // ============================================================ helpers

    private static DateTimeOffset Local(ScreenState state, DateTimeOffset moment) =>
        TimeZoneInfo.ConvertTime(moment, state.Zone);

    private static Money? Money(string? text, string? currency)
    {
        if (text is null || currency is null)
        {
            return null;
        }

        try
        {
            return WireFigures.Money(text, currency);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static string Figure(string? amount, string? currency) =>
        Money(amount, currency) is { } money ? DisplayFigures.Amount(money) : "?";

    /// <summary>A decimal from the wire, with the display's comma and grouping.</summary>
    private static string? Decimal(string wire)
    {
        if (!decimal.TryParse(wire, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var whole = decimal.Truncate(value);
        var fraction = wire.Contains('.', StringComparison.Ordinal) ? wire[(wire.IndexOf('.', StringComparison.Ordinal) + 1)..] : string.Empty;
        var head = DisplayFigures.Count((long)whole);
        return fraction.Length == 0 ? head : head + DisplayFigures.DecimalSeparator + fraction;
    }
}

/// <summary>Which regions a frame changed (<see cref="TillScreen.Compare"/>): the ones the window redraws.</summary>
public sealed record FrameChanges(bool Top, bool Notice, bool Cart, bool Rail, bool Bottom)
{
    public static FrameChanges All { get; } = new(true, true, true, true, true);
}

/// <param name="Tab">The open ticket; null on the sign-in screen, where there is none.</param>
public sealed record TopBar(string? Place, TicketTab? Tab, Connection Connection, StaffChip? Staff, string Clock);

/// <summary>"Ticket en cours · 14 lignes". No number until the sale is recorded (D-070).</summary>
public sealed record TicketTab(string Title, string Detail);

public sealed record Connection(string Label, Tone Tone);

public sealed record StaffChip(string Name, string Role);

/// <summary>The notice slot: a label, its tone, and a sentence. Never a tone without a label.</summary>
public sealed record NoticeLine(Tone Tone, string Label, string Text);

public sealed record CartView(
    IReadOnlyList<string> Columns,
    IReadOnlyList<LineRow> Lines,
    EmptyState? Empty,
    LineActions? Actions);

/// <param name="Struck">Taken out before payment: drawn struck through, never counted.</param>
/// <param name="Selected">Touched by the cashier; its actions open beneath it.</param>
public sealed record LineRow(
    string VariantId,
    string Quantity,
    string Article,
    IReadOnlyList<Chip> Chips,
    string UnitPrice,
    string Total,
    bool Struck,
    bool Selected);

public sealed record Chip(Tone Tone, string Label);

public sealed record EmptyState(string Title, string Hint);

/// <summary>What the selected line offers. A4 has "Retirer la ligne"; B2, B4, B5 add theirs.</summary>
public sealed record LineActions(string Remove, string RemoveKey);

public sealed record Figure(string Label, string Value);

/// <summary>Encaisser, or Nouvelle vente once a sale is recorded.</summary>
public sealed record PrimaryKey(string Title, string? Detail, string Key, bool Enabled);

public sealed record BottomBar(IReadOnlyList<Figure> Summary, string BigLabel, string BigFigure, PrimaryKey Primary);

/// <summary>The right-hand column, the one place on screen that changes shape (G1 kit §9).</summary>
public abstract record Rail
{
    private Rail()
    {
    }

    /// <summary>At rest: the space the B-block parts will fill, and the Almanac slot at its foot.</summary>
    public sealed record Rest(AlmanacSlot? Almanac) : Rail;

    /// <summary>A sale just recorded: what it came to and what the drawer takes.</summary>
    public sealed record Paid(string Label, string Title, string Subtitle, IReadOnlyList<Figure> Figures, string Footer) : Rail;

    /// <summary>No answer to a sale. Critical, and in the rail because it needs room to say what to do.</summary>
    /// <param name="Acknowledge">The one way on: the cashier has checked (D-085).</param>
    public sealed record Unconfirmed(string Label, string Title, string Body, string Detail, string Acknowledge) : Rail;
}

public abstract record AlmanacSlot
{
    private AlmanacSlot()
    {
    }

    /// <summary>A card: the board's own claim, and three answers (G1 kit §7).</summary>
    /// <param name="Awaiting">"2 cartes attendent le responsable", when cards are held back.</param>
    public sealed record Card(
        string RecommendationId,
        string Kind,
        string Position,
        string Claim,
        string? Detail,
        CardAction? Primary,
        CardAction Adjust,
        CardAction Dismiss,
        string? Awaiting) : AlmanacSlot;

    /// <summary>Nothing for this person: the quiet weight, no action (G1 kit §7).</summary>
    public sealed record Quiet(string Text, string? Awaiting) : AlmanacSlot;
}

/// <param name="OptionId">The option an accept records; null for an action that records none.</param>
public sealed record CardAction(string? OptionId, string Label);
