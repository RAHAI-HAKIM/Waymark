using System.Globalization;
using Waymark.Contracts.Pos;
using Waymark.Contracts.Recommendations;
using Waymark.Contracts.Reference;
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
/// <param name="SelectedLineId">The line the cashier touched, if any.</param>
/// <param name="Board">The signed-in person's Almanac board; null until fetched, or when there is none to show.</param>
/// <param name="CardIndex">Which of the board's cards is on screen.</param>
/// <param name="Unconfirmed">A sale with no usable answer, until the cashier acknowledges it (D-085).</param>
/// <param name="Parked">Tickets on hold, oldest first (D-087).</param>
/// <param name="Drafts">Tickets cancelled today, newest first (D-087).</param>
/// <param name="DraftsOpen">Whether the rail shows the drafts list.</param>
/// <param name="Search">The name search under way, if any (B1, D-088).</param>
/// <param name="NextCount">What the next scan or touch adds: the "QTÉ × n" chip (B1).</param>
/// <param name="Viewing">A past ticket open read-only in the ticket view (B1).</param>
/// <param name="Tickets">The "Tickets" list, when the rail shows it (B1).</param>
/// <param name="Weighing">A product sold by weight waiting for its weight (B3).</param>
/// <param name="Discounting">A discount being given at the counter, on a line or the ticket (B4).</param>
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
    string? SelectedLineId,
    BoardAnswer? Board,
    int CardIndex,
    UnconfirmedSale? Unconfirmed = null,
    IReadOnlyList<HeldTicket>? Parked = null,
    IReadOnlyList<HeldTicket>? Drafts = null,
    bool DraftsOpen = false,
    SearchState? Search = null,
    int NextCount = 1,
    PastTicketDetail? Viewing = null,
    TicketsState? Tickets = null,
    PendingWeight? Weighing = null,
    DiscountState? Discounting = null);

/// <summary>
/// A discount being given at the counter (B4, D-091): on which line, or the ticket when
/// <paramref name="LineId"/> is null; percent or amount; the text typed in the field; the shop's
/// reasons (null while asked); the one chosen; then the manager step, when the seller may not give it
/// alone.
/// </summary>
/// <param name="Form">One of <c>DiscountForms</c>.</param>
/// <param name="Staff">Who may be asked to authorise: the sign-in list.</param>
/// <param name="PinLength">How many digits of the manager's PIN are typed: the dots, never the digits.</param>
/// <param name="Kind">A discount (B4), or a price typed in place of the price in force (B5).</param>
/// <param name="Note">What was typed for a reason that asks for a note (F-28).</param>
/// <param name="Noting">The field now holds the note, not the value.</param>
public sealed record DiscountState(
    string? LineId,
    string Form,
    string Typed,
    ReasonCodeList? Reasons,
    bool Offline,
    string? ReasonCode,
    bool Authorising = false,
    TillStaff? Staff = null,
    string? ManagerId = null,
    int PinLength = 0,
    DiscountProblem? Problem = null,
    int? AttemptsLeft = null,
    DateTimeOffset? LockedUntil = null,
    CounterKind Kind = CounterKind.Discount,
    string Note = "",
    bool Noting = false);

/// <summary>What the counter panel gives: a discount (B4), or a new unit price (B5).</summary>
public enum CounterKind
{
    Discount,
    PriceOverride,
}

/// <summary>What the discount panel has to say is wrong.</summary>
public enum DiscountProblem
{
    ValueInvalid,
    NoReason,
    Offline,
    WrongPin,
    Locked,
    NotAllowed,
    NoPin,

    /// <summary>A price above the band (B5).</summary>
    AboveBand,

    /// <summary>A price of zero or less (B5).</summary>
    NotAboveZero,

    /// <summary>The price in force itself (B5).</summary>
    Unchanged,

    /// <summary>A reason that asks for a note, and none written (F-28).</summary>
    NoteMissing,
}

/// <summary>A name search: what was typed, the server's answer (null while asked, or when it could not say), which row Entrée takes.</summary>
/// <param name="Offline">The server could not answer.</param>
public sealed record SearchState(string Query, ProductSearchAnswer? Answer, bool Offline, int Highlighted);

/// <summary>The "Tickets" list: which day, which tills, and the server's answer (null while asked, or when it could not say).</summary>
public sealed record TicketsState(DateOnly Day, bool AllTills, TicketList? List, bool Offline);

/// <summary>
/// Everything the till shows, decided (session A4, G1). The window draws this and decides
/// nothing: every rule a cashier would notice being wrong lives here, where it is tested
/// without a window.
/// </summary>
/// <param name="Field">The chip in the search field: "QTÉ × 1", or the n typed before a scan (B1).</param>
/// <param name="Results">The name search's results, floating over the ticket; null when there is no search (B1).</param>
/// <param name="Weigh">The weight card, floating where the results float, while a weight is awaited (B3).</param>
public sealed record TillScreen(
    bool RightToLeft,
    TopBar Top,
    NoticeLine Notice,
    CartView Cart,
    Rail Rail,
    BottomBar Bottom,
    FieldChip Field,
    ResultsView? Results,
    WeighCard? Weigh = null)
{
    /// <summary>The key for "Encaisser". Every action has its F key (G1 kit §9).</summary>
    public const string CollectKey = "F12";

    /// <summary>The key for "Retirer la ligne".</summary>
    public const string RemoveLineKey = "F8";

    /// <summary>The key for "Attente" (G1 board).</summary>
    public const string ParkKey = "F3";

    /// <summary>The key for "Remise" under a line (G1 board).</summary>
    public const string DiscountLineKey = "F4";

    /// <summary>The key for "Remise ticket" in the rail (G1 board).</summary>
    public const string TicketDiscountKey = "F6";

    /// <summary>What closes a past ticket, as the keyboard says it.</summary>
    public const string CloseKey = "Échap";

    public static TillScreen Build(ScreenState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var text = state.Text;

        if (state.Viewing is { } past)
        {
            return PastOf(state, past);
        }

        return new TillScreen(
            text.RightToLeft,
            TopBarOf(state),
            NoticeOf(state),
            CartOf(state),
            RailOf(state),
            BottomOf(state),
            state.Weighing is { } weighing
                ? new FieldChip(text.WeightChip(weighing.UnitCode), Active: true)
                : state.Discounting is { } discounting
                    ? new FieldChip(
                        discounting.Noting ? text.NoteChip
                        : discounting.Kind == CounterKind.PriceOverride ? $"{text.PriceKey.ToUpperInvariant()} · {text.CurrencySymbol(CurrencyOf(state) ?? Currency.Dzd)}"
                        : text.DiscountChip(discounting.Form == DiscountForms.Percent ? "%" : text.CurrencySymbol(CurrencyOf(state) ?? Currency.Dzd)),
                        Active: true)
                    : new FieldChip(text.NextCount(DisplayFigures.Count(state.NextCount)), state.NextCount > 1),
            state.Weighing is null && state.Discounting is null ? ResultsOf(state) : null,
            WeighOf(state));
    }

    /// <summary>
    /// The weight card (B3, D-090): the product and its price per unit, what was typed, and the
    /// server's total for it, or why the weight cannot be sold. The till never works a total out
    /// itself: the figure shown is the one the sale will charge.
    /// </summary>
    private static WeighCard? WeighOf(ScreenState state)
    {
        if (state.Weighing is not { } weighing)
        {
            return null;
        }

        var text = state.Text;
        var typed = weighing.Typed.Trim();
        var message = weighing.Invalid ? text.WeighInvalid(weighing.Decimals)
            : weighing.Refusal is { } refusal ? text.NotSellableReason(refusal)
            : typed.Length == 0 ? text.WeighPrompt(weighing.UnitCode)
            : null;
        var total = weighing.Preview is { } preview && message is null
            ? $"{DisplayFigures.Weight(preview.Quantity, weighing.Decimals)} {weighing.UnitCode} = {DisplayFigures.AmountWithCurrency(preview.LineTotal, text)}"
            : null;

        return new WeighCard(
            text.WeighTitle,
            $"{weighing.ProductName} {weighing.VariantName}",
            text.PerUnit(DisplayFigures.AmountWithCurrency(weighing.UnitPrice, text), weighing.UnitCode),
            total,
            message,
            weighing.Invalid || weighing.Refusal is not null,
            text.WeighKeys);
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
            Top: !Same(drawn.Top, next.Top),
            Notice: drawn.Notice != next.Notice,
            Cart: !Same(drawn.Cart, next.Cart),
            Rail: !Same(drawn.Rail, next.Rail),
            Bottom: !Same(drawn.Bottom, next.Bottom),
            Field: drawn.Field != next.Field,
            Results: !Same(drawn.Results, next.Results) || drawn.Weigh != next.Weigh);
    }

    private static bool Same(ResultsView? drawn, ResultsView? next) => (drawn, next) switch
    {
        (null, null) => true,
        ({ } a, { } b) => a.Rows.SequenceEqual(b.Rows) && a with { Rows = b.Rows } == b,
        _ => false,
    };

    private static bool Same(CartView drawn, CartView next) =>
        drawn.TicketDiscount == next.TicketDiscount
        && drawn.Columns.SequenceEqual(next.Columns)
        && drawn.Lines.Count == next.Lines.Count
        && drawn.Lines.Zip(next.Lines).All(pair =>
            pair.First.Chips.SequenceEqual(pair.Second.Chips) && pair.First with { Chips = pair.Second.Chips } == pair.Second)
        && drawn with { Columns = next.Columns, Lines = next.Lines } == next;

    private static bool Same(TopBar drawn, TopBar next) =>
        drawn.Parked.SequenceEqual(next.Parked) && drawn with { Parked = next.Parked } == next;

    private static bool Same(Rail drawn, Rail next) => (drawn, next) switch
    {
        (Rail.Paid a, Rail.Paid b) => a.Figures.SequenceEqual(b.Figures) && a with { Figures = b.Figures } == b,
        (Rail.Rest a, Rail.Rest b) => a.Operations.SequenceEqual(b.Operations) && a with { Operations = b.Operations } == b,
        (Rail.Drafts a, Rail.Drafts b) => a.Rows.SequenceEqual(b.Rows) && a with { Rows = b.Rows } == b,
        (Rail.Tickets a, Rail.Tickets b) => a.Rows.SequenceEqual(b.Rows) && a with { Rows = b.Rows } == b,
        (Rail.Past a, Rail.Past b) => a.Figures.SequenceEqual(b.Figures) && a with { Figures = b.Figures } == b,
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

        // The tickets on hold, as tabs after the one on screen (G1, "Attente 14:05 · 3 lignes").
        var parked = (state.Parked ?? []).Select(held => new ParkedTab(
            held.Id,
            text.ParkedAt(DisplayFigures.Clock(Local(state, held.At))),
            text.Lines(held.Cart.ActiveLines.Count))).ToList();

        return new TopBar(
            context is null ? null : $"{context.StoreName} · {context.TerminalName}",
            tab,
            connection,
            staff,
            DisplayFigures.Clock(Local(state, state.Now)),
            parked);
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

            case { Kind: TillNoticeKind.TicketUnknown } unknownTicket:
                return new NoticeLine(Tone.Warning, text.TicketUnknown, text.TicketUnknownDetail(unknownTicket.Code));

            case { Kind: TillNoticeKind.TicketNotAllowed }:
                return new NoticeLine(Tone.Warning, text.ManagerOnly, text.TicketsNotAllowed);

            case { Kind: TillNoticeKind.WeighedTakesNoCount }:
                return new NoticeLine(Tone.Warning, text.CountIgnored, text.CountIgnoredDetail);

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
            var selected = !paid && !line.IsRemoved && line.LineId == state.SelectedLineId;
            return new LineRow(
                line.LineId,
                line.Weight is { } weight
                    ? $"{DisplayFigures.Weight(weight.Quantity, weight.UnitDecimals)} {line.UnitCode}"
                    : DisplayFigures.Count(line.Count),
                Article(line),
                ChipsOf(line, text, state),
                // A weighed line's price is per unit of weight, and says so: "180,00 /kg" (G1 board).
                line.IsWeighed ? $"{DisplayFigures.Amount(line.UnitPrice)} /{line.UnitCode}" : DisplayFigures.Amount(line.ChargedPrice),
                DisplayFigures.Amount(line.LineTotal),
                line.IsRemoved,
                selected,
                DiscountOf(state, line));
        }).ToList();

        var empty = rows.Count == 0 ? new EmptyState(text.EmptyTitle, text.EmptyHint) : null;

        return new CartView(
            [text.ColumnQuantity, text.ColumnArticle, text.ColumnUnitPrice, text.ColumnTotal],
            rows,
            empty,
            ActionsOf(state, paid),
            paid ? null : TicketDiscountOf(state));
    }

    /// <summary>
    /// A line's own discount, as the board draws it under the line: "REMISE 10 % · GESTE COMMERCIAL",
    /// then what it takes off, worked out with the store's policy as the sale will (B4).
    /// </summary>
    private static DiscountLine? DiscountOf(ScreenState state, CartLine line)
    {
        if (line.Discount is not { } discount || line.IsRemoved || state.Paid is not null)
        {
            return null;
        }

        return new DiscountLine(
            $"{state.Text.DiscountLabel} {Given(state, discount)} · {Reason(state, discount)}",
            Minus(Off(() => state.Cart.LineDiscountOf(line))));
    }

    /// <summary>The ticket's discount, as a row at the end of the ticket.</summary>
    private static DiscountLine? TicketDiscountOf(ScreenState state)
    {
        if (state.Cart.TicketDiscount is not { } discount)
        {
            return null;
        }

        var shares = Off(() => state.Cart.TicketShares().Aggregate((sum, share) => sum + share));
        return new DiscountLine($"{state.Text.TicketDiscountLabel} {Given(state, discount)} · {Reason(state, discount)}", Minus(shares));
    }

    /// <summary>"10 %" or "50,00 DA".</summary>
    private static string Given(ScreenState state, CounterDiscount discount) => discount.IsPercent
        ? $"{Hundredths(discount.Hundredths)} %"
        : DisplayFigures.AmountWithCurrency(Domain.Values.Money.FromMinorUnits(discount.Hundredths, CurrencyOf(state) ?? Currency.Dzd), state.Text);

    private static string Reason(ScreenState state, CounterDiscount discount) =>
        (state.Text.RightToLeft ? discount.ReasonAr : discount.ReasonFr).ToUpperInvariant();

    /// <summary>"10", "12,5": a percent without the zeros it does not need.</summary>
    private static string Hundredths(long value)
    {
        var cents = value % 100;
        var tail = cents % 10 == 0 ? (cents / 10).ToString(CultureInfo.InvariantCulture) : cents.ToString("00", CultureInfo.InvariantCulture);
        return cents == 0 ? DisplayFigures.Count(value / 100) : $"{DisplayFigures.Count(value / 100)}{DisplayFigures.DecimalSeparator}{tail}";
    }

    /// <summary>A total that needs the discount rules, or none while they are not written (see <see cref="Off{T}"/>).</summary>
    private static Money? Try(Func<Money?> work)
    {
        try
        {
            return work();
        }
        catch (NotImplementedException)
        {
            return null;
        }
    }

    private static string Minus(Money? off) => off is { } amount ? DisplayFigures.Amount(-amount) : "—";

    /// <summary>
    /// A discount's figure, or none: the rules are Hakim's piece (<c>Discounts</c>), and a till whose
    /// piece is not written yet shows the discount without its figure rather than falling over.
    /// </summary>
    private static T? Off<T>(Func<T> work)
        where T : struct
    {
        try
        {
            return work();
        }
        catch (NotImplementedException)
        {
            return null;
        }
    }

    /// <summary>
    /// What the selected line offers: the − / + stepper and "Retirer la ligne" (G1 board). The −
    /// stops at one; the last unit goes with "Retirer la ligne", which leaves the line struck.
    /// </summary>
    private static LineActions? ActionsOf(ScreenState state, bool paid)
    {
        if (paid || state.Cart.ActiveLines.FirstOrDefault(line => line.LineId == state.SelectedLineId) is not { } line)
        {
            return null;
        }

        // A weighed line has no stepper: it is one weighing. A weight typed by hand can be typed
        // again ("Poids"); a label's cannot, and a wrong label is a line removed (B3).
        if (line.Weight is { } weight)
        {
            return new LineActions(
                line.LineId, 1, $"{DisplayFigures.Weight(weight.Quantity, weight.UnitDecimals)} {line.UnitCode}", false,
                state.Text.RemoveLine, RemoveLineKey, weight.IsTyped ? state.Text.Reweigh : null, IsWeighed: true,
                Discount: state.Text.DiscountKey, DiscountKey: DiscountLineKey);
        }

        return new LineActions(
            line.LineId, line.Count, DisplayFigures.Count(line.Count), line.Count > 1, state.Text.RemoveLine, RemoveLineKey,
            Discount: state.Text.DiscountKey, DiscountKey: DiscountLineKey, Price: state.Text.PriceKey);
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

        // A price typed at the counter, said on the line (B5): whoever reviews the drawer sees it.
        if (line.Override is not null)
        {
            chips.Add(new Chip(Tone.Neutral, text.PriceOverrideLabel));
        }

        // Where a weight came from, said on the line (D-090): a typed weight is the one nothing
        // vouches for, and whoever reviews the drawer should see which lines were typed.
        if (line.Weight is { } weight)
        {
            chips.Add(new Chip(Tone.Neutral, weight.Source switch
            {
                QuantitySources.TypedWeight => text.TypedWeight,
                QuantitySources.LabelPrice => text.LabelPrice,
                _ => text.LabelWeight,
            }));
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

        if (state.Discounting is { } discounting)
        {
            return discounting.Authorising ? AuthoriseOf(state, discounting)
                : discounting.Kind == CounterKind.PriceOverride ? OverridePanelOf(state, discounting)
                : DiscountPanelOf(state, discounting);
        }

        if (state.DraftsOpen)
        {
            return DraftsOf(state);
        }

        if (state.Tickets is { } tickets)
        {
            return TicketsOf(state, tickets);
        }

        return new Rail.Rest(OperationsOf(state), AlmanacOf(state));
    }

    /// <summary>
    /// The rail's operation keys (G1 board). B2 has three: "Attente" and "Annuler ticket" for a
    /// ticket with lines, and "Brouillons", available once something was cancelled today. The
    /// board's other keys arrive with their sessions.
    /// </summary>
    private static List<OperationKey> OperationsOf(ScreenState state)
    {
        var text = state.Text;
        var mayPutAside = state.Cart.ActiveLines.Count > 0 && state.Paid is null && state.Unconfirmed is null;
        var drafts = state.Drafts?.Count ?? 0;

        return
        [
            new OperationKey(Operation.Park, text.Park, ParkKey, mayPutAside),
            new OperationKey(Operation.TicketDiscount, text.TicketDiscountKey, TicketDiscountKey, mayPutAside && state.Weighing is null),
            new OperationKey(Operation.CancelTicket, text.CancelTicket, null, mayPutAside),
            new OperationKey(Operation.Drafts, text.DraftsKey(drafts), null, drafts > 0),
            new OperationKey(Operation.Tickets, text.TicketsKey, null, true),
        ];
    }

    // ============================================================ B4: a discount given at the counter

    /// <summary>
    /// The discount panel (B4, D-091), built from the kit where the rail's panels go: what it is on,
    /// percent or amount, what the typed value takes off, the shop's reasons, then Continuer. The
    /// value is typed in the one field (D-088).
    /// </summary>
    private static Rail.Discount DiscountPanelOf(ScreenState state, DiscountState discounting)
    {
        var text = state.Text;
        var percent = discounting.Form == DiscountForms.Percent;
        var currency = CurrencyOf(state) ?? Currency.Dzd;
        var line = discounting.LineId is { } id ? state.Cart.ActiveLines.FirstOrDefault(l => l.LineId == id) : null;
        var gross = line?.LineTotal ?? state.Cart.Subtotal ?? Domain.Values.Money.Zero(currency);
        var valid = DiscountEntry.TryParse(discounting.Typed, discounting.Form, out var hundredths);

        Money? preview = null;
        if (valid)
        {
            var asked = new CounterDiscount(discounting.Form, hundredths, string.Empty, string.Empty, string.Empty, string.Empty);
            preview = line is not null
                ? Off(() => Domain.Sales.Discounts.OnLine(gross, asked.AsDomain(currency), state.Cart.Policy))
                : Off(() => Domain.Sales.Discounts.OnTicket(
                    [.. state.Cart.ActiveLines.Select(l => l.LineTotal - state.Cart.LineDiscountOf(l))], asked.AsDomain(currency), state.Cart.Policy)
                    .Aggregate((sum, share) => sum + share));
        }

        var reasons = discounting.Reasons?.ReasonCodes ?? [];
        var chosen = reasons.FirstOrDefault(reason => reason.Code == discounting.ReasonCode);
        if (discounting.Noting && chosen is not null)
        {
            // The note step (F-28): the field holds the note; the panel says what it is for.
            return NotePanelOf(state, discounting, line, gross, text.RightToLeft ? chosen.LabelAr : chosen.LabelFr);
        }

        var message = discounting.Offline ? text.DiscountOffline
            : discounting.Reasons is not null && reasons.Count == 0 ? text.NoDiscountReasons
            : discounting.Problem == DiscountProblem.ValueInvalid || (discounting.Typed.Trim().Length > 0 && !valid) ? text.DiscountInvalid(percent)
            : discounting.Problem == DiscountProblem.NoReason ? text.ChooseReason
            : discounting.Typed.Trim().Length == 0 ? text.DiscountPrompt(percent)
            : null;
        var existing = line is not null ? line.Discount : state.Cart.TicketDiscount;

        return new Rail.Discount(
            line is null ? text.TicketDiscountLabel : text.DiscountLabel,
            line is null ? text.CurrentTicket : Article(line),
            DisplayFigures.AmountWithCurrency(gross, text),
            [
                new DiscountFormChoice(DiscountForms.Percent, "%", percent),
                new DiscountFormChoice(DiscountForms.Amount, text.CurrencySymbol(currency), !percent),
            ],
            preview is { } off ? DisplayFigures.AmountWithCurrency(-off, text) : null,
            text.ReasonTitle,
            [.. reasons.Select(reason => new ReasonRow(
                reason.Code, text.RightToLeft ? reason.LabelAr : reason.LabelFr, reason.Code == discounting.ReasonCode))],
            message,
            message is not null && (discounting.Problem is not null || discounting.Offline || (discounting.Typed.Trim().Length > 0 && !valid)
                || (discounting.Reasons is not null && reasons.Count == 0)),
            valid && discounting.ReasonCode is not null && !discounting.Offline,
            text.ContinueKey,
            text.BackToTicket,
            existing is null ? null : text.RemoveDiscount);
    }

    /// <summary>
    /// The note step (F-28, D-092): a reason that asks for a note is not given without one. The
    /// panel keeps its place; the note typed shows where the preview was.
    /// </summary>
    private static Rail.Discount NotePanelOf(ScreenState state, DiscountState discounting, CartLine? line, Money gross, string reason)
    {
        var text = state.Text;
        var typed = discounting.Note.Trim();
        return new Rail.Discount(
            line is null ? text.TicketDiscountLabel : text.DiscountLabel,
            line is null ? text.CurrentTicket : Article(line),
            DisplayFigures.AmountWithCurrency(gross, text),
            [],
            typed.Length == 0 ? null : $"« {typed} »",
            text.ReasonTitle,
            [new ReasonRow(discounting.ReasonCode ?? string.Empty, reason, true)],
            text.NotePrompt(reason),
            discounting.Problem == DiscountProblem.NoteMissing,
            typed.Length > 0,
            text.ContinueKey,
            text.BackToTicket,
            null);
    }

    /// <summary>
    /// The price panel (B5, D-092), the discount panel's shape without % and DA: the price in force,
    /// the price typed in the field, what the line then comes to, the band (<see cref="Domain.Sales.PriceOverride"/>,
    /// the same rule the server checks), a warning below cost, the reasons, then Continuer.
    /// </summary>
    private static Rail.Discount OverridePanelOf(ScreenState state, DiscountState discounting)
    {
        var text = state.Text;
        var currency = CurrencyOf(state) ?? Currency.Dzd;
        var line = discounting.LineId is { } id ? state.Cart.ActiveLines.FirstOrDefault(l => l.LineId == id) : null;
        if (line is null)
        {
            return new Rail.Discount(text.PriceOverrideLabel, string.Empty, string.Empty, [], null, text.ReasonTitle, [], null, false, false,
                text.ContinueKey, text.BackToTicket, null);
        }

        var valid = DiscountEntry.TryParse(discounting.Typed, DiscountForms.Amount, out var centimes);
        var newPrice = Domain.Values.Money.FromMinorUnits(centimes, currency);
        var check = valid ? OffCheck(() => Domain.Sales.PriceOverride.Check(line.UnitPrice, newPrice, line.UnitCost)) : null;
        var reasons = discounting.Reasons?.ReasonCodes ?? [];

        var (message, refused) = discounting.Offline ? (text.DiscountOffline, true)
            : discounting.Reasons is not null && reasons.Count == 0 ? (text.NoOverrideReasons, true)
            : discounting.Typed.Trim().Length == 0 ? (text.PricePrompt, false)
            : !valid ? (text.PriceInvalid, true)
            : check?.Verdict switch
            {
                Domain.Sales.OverrideVerdict.AboveBand => (text.PriceAboveBand(DisplayFigures.AmountWithCurrency(check.Ceiling, text)), true),
                Domain.Sales.OverrideVerdict.NotAboveZero => (text.PriceNotAboveZero, true),
                Domain.Sales.OverrideVerdict.Unchanged => (text.PriceUnchanged, true),
                Domain.Sales.OverrideVerdict.AcceptedBelowCost => (text.PriceBelowCost, false),
                _ => discounting.Problem == DiscountProblem.NoReason ? (text.ChooseReason, true) : ((string?)null, false),
            };

        return new Rail.Discount(
            text.PriceOverrideLabel,
            Article(line),
            text.PriceInForce(DisplayFigures.AmountWithCurrency(line.UnitPrice, text)),
            [],
            check is { MayCharge: true } ? text.NewTotal(DisplayFigures.AmountWithCurrency(newPrice * line.Count, text)) : null,
            text.ReasonTitle,
            [.. reasons.Select(reason => new ReasonRow(
                reason.Code, text.RightToLeft ? reason.LabelAr : reason.LabelFr, reason.Code == discounting.ReasonCode))],
            message,
            refused,
            check is { MayCharge: true } && discounting.ReasonCode is not null && !discounting.Offline,
            text.ContinueKey,
            text.BackToTicket,
            line.Override is null ? null : text.BackToListPrice);
    }

    /// <summary>The band's verdict, or none while the rule is not written (Hakim's piece, as <see cref="Off{T}"/>).</summary>
    private static Domain.Sales.OverrideCheck? OffCheck(Func<Domain.Sales.OverrideCheck> work)
    {
        try
        {
            return work();
        }
        catch (NotImplementedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The manager step (B4), as the board draws "Annuler le ticket" (09-pin): what is asked, who
    /// authorises it, their PIN as dots, and the pad. The PIN is checked by StoreServer only (§3.10).
    /// </summary>
    private static Rail.Authorise AuthoriseOf(ScreenState state, DiscountState discounting)
    {
        var text = state.Text;
        var overriding = discounting.Kind == CounterKind.PriceOverride;
        var form = overriding ? DiscountForms.Amount : discounting.Form;
        var given = DiscountEntry.TryParse(discounting.Typed, form, out var hundredths)
            ? overriding
                ? DisplayFigures.AmountWithCurrency(Domain.Values.Money.FromMinorUnits(hundredths, CurrencyOf(state) ?? Currency.Dzd), state.Text)
                : Given(state, new CounterDiscount(form, hundredths, string.Empty, string.Empty, string.Empty, string.Empty))
            : discounting.Typed;
        var reason = discounting.Reasons?.ReasonCodes.FirstOrDefault(r => r.Code == discounting.ReasonCode);
        var line = discounting.LineId is { } id ? state.Cart.ActiveLines.FirstOrDefault(l => l.LineId == id) : null;

        var message = discounting.Problem switch
        {
            DiscountProblem.WrongPin => text.WrongManagerPin(discounting.AttemptsLeft ?? 0),
            DiscountProblem.Locked => text.ManagerLocked(DisplayFigures.Clock(Local(state, discounting.LockedUntil ?? state.Now))),
            DiscountProblem.NotAllowed => text.NotAManager,
            DiscountProblem.NoPin => text.ManagerHasNoPin,
            DiscountProblem.Offline => text.DiscountOffline,
            _ => null,
        };

        return new Rail.Authorise(
            text.ManagerApproval,
            $"{(overriding ? text.PriceOverrideLabel : line is null ? text.TicketDiscountLabel : text.DiscountLabel)} {given}",
            $"{(line is null ? text.CurrentTicket : Article(line))} · {(reason is null ? string.Empty : text.RightToLeft ? reason.LabelAr : reason.LabelFr)}",
            text.WhoApproves,
            [.. (discounting.Staff?.Staff ?? []).Select(person => new ApproverRow(
                person.StaffId, person.StaffName, text.RightToLeft ? person.RoleLabelAr : person.RoleLabelFr,
                person.StaffId == discounting.ManagerId, person.HasPin))],
            text.ManagerPin,
            discounting.PinLength,
            message,
            discounting.ManagerId is not null && discounting.PinLength >= 4,
            overriding ? text.ApproveOverride : text.ApproveDiscount,
            text.BackToTicket);
    }

    // ============================================================ B1: search, tickets, a past ticket

    /// <summary>
    /// The name search's results, floating over the ticket (D-088): name, price and stock, which
    /// makes them the stock lookup; an unsellable product listed with its reason, and not touchable.
    /// Entrée takes the highlighted row, if it can be sold.
    /// </summary>
    private static ResultsView? ResultsOf(ScreenState state)
    {
        if (state.Search is not { } search)
        {
            return null;
        }

        var text = state.Text;
        if (search.Offline)
        {
            return new ResultsView(search.Query, [], text.SearchOffline);
        }

        if (search.Answer is not { } answer)
        {
            return new ResultsView(search.Query, [], text.Searching);
        }

        var rows = answer.Results.Select((result, index) => ResultRowOf(result, index == search.Highlighted, text)).ToList();
        return new ResultsView(search.Query, rows, rows.Count == 0 ? text.NoResults(search.Query) : null);
    }

    private static ResultRow ResultRowOf(ProductSearchResult result, bool highlighted, TillText text)
    {
        var title = $"{result.ProductName} {result.VariantName}";
        if (result is { Outcome: ProductLookupOutcome.Found, Product: { } product, Code: not null }
            && Money(product.PriceTtc, product.Currency) is { } price)
        {
            var stock = Decimal(product.StockOnHand) ?? product.StockOnHand;
            return new ResultRow(result.VariantId, title, $"{DisplayFigures.AmountWithCurrency(price, text)} · {text.Stock(stock)}", null, true, highlighted);
        }

        return new ResultRow(result.VariantId, title, text.NotSellableReason(result.Reason), new Chip(Tone.Warning, text.NotSellable), false, highlighted);
    }

    /// <summary>
    /// The "Tickets" list (D-088): one store day, this till or every till, newest first. Today at
    /// this till is anyone's; the server refuses the rest below rank 2, and the list says so.
    /// </summary>
    private static Rail.Tickets TicketsOf(ScreenState state, TicketsState tickets)
    {
        var text = state.Text;
        var today = DateOnly.FromDateTime(Local(state, state.Now).DateTime);
        var rows = new List<TicketRow>();
        string? message = null;

        if (tickets.Offline)
        {
            message = text.SearchOffline;
        }
        else if (tickets.List is not { } list)
        {
            message = text.Searching;
        }
        else if (list.Outcome == TicketOutcomes.NotAllowed)
        {
            message = text.TicketsNotAllowed;
        }
        else if (list.Outcome == TicketOutcomes.NotSignedIn)
        {
            message = text.SessionEndedDetail;
        }
        else
        {
            rows.AddRange(list.Tickets.Select(ticket => new TicketRow(
                ticket.TransactionId,
                $"{DisplayFigures.Clock(Local(state, ticket.OccurredAt))} · {text.TicketNumber(ticket.InvoiceNumber ?? "?")}",
                $"{text.Lines(ticket.LineCount)} · {Figure(ticket.Total, ticket.Currency)}",
                StatusChip(ticket.Status, text))));
            message = rows.Count == 0 ? text.NoTickets : null;
        }

        var dayLabel = tickets.Day == today
            ? text.TodayLabel(DisplayFigures.DayAndMonth(tickets.Day))
            : DisplayFigures.DayAndMonth(tickets.Day);

        return new Rail.Tickets(
            text.TicketsTitle,
            dayLabel,
            tickets.AllTills ? text.AllTills : text.ThisTill,
            text.OtherScope(tickets.AllTills),
            rows,
            message,
            MayGoForward: tickets.Day < today,
            text.Close);
    }

    private static Chip? StatusChip(string status, TillText text) => status switch
    {
        "voided" => new Chip(Tone.Warning, text.StatusVoided),
        "refunded" => new Chip(Tone.Warning, text.StatusRefunded),
        "partially_refunded" => new Chip(Tone.Warning, text.StatusPartlyRefunded),
        _ => null,
    };

    /// <summary>
    /// A past ticket, read-only, in the ticket view (D-088): the same regions, drawn from the sale as
    /// it ended; no selection, no action on a line, and one key, "Fermer", which gives the ticket on
    /// screen back. No customer: showing one is a consultation to log, and that is B7's.
    /// </summary>
    private static TillScreen PastOf(ScreenState state, PastTicketDetail past)
    {
        var text = state.Text;
        var when = Local(state, past.OccurredAt);
        var clock = DisplayFigures.Clock(when);
        var date = DisplayFigures.DayAndMonth(DateOnly.FromDateTime(when.DateTime));
        var number = text.TicketNumber(past.InvoiceNumber ?? "?");
        var status = StatusChip(past.Status, text);

        var top = TopBarOf(state) with { Tab = new TicketTab(number, $"{date} {clock}") };

        var notice = new NoticeLine(
            status is null ? Tone.Neutral : Tone.Warning,
            status?.Label ?? text.PastTicket,
            text.PastTicketLine(date, clock, past.StaffName));

        var rows = past.Lines.Select((line, index) => new LineRow(
            $"past-{index}",
            Decimal(line.Quantity) ?? line.Quantity,
            $"{line.ProductName} {line.VariantName}",
            [],
            Figure(line.UnitPrice, past.Currency),
            Figure(line.LineTotal, past.Currency),
            Struck: false,
            Selected: false)).ToList();
        var cart = new CartView(
            [text.ColumnQuantity, text.ColumnArticle, text.ColumnUnitPrice, text.ColumnTotal],
            rows,
            rows.Count == 0 ? new EmptyState(text.EmptyTitle, string.Empty) : null,
            null);

        var total = Money(past.Total, past.Currency);
        var figures = new List<Figure>
        {
            new(text.Subtotal, Figure(past.Subtotal, past.Currency)),
            new(text.TaxIncluded, Figure(past.TaxTotal, past.Currency)),
            new(text.TicketTotal, Figure(past.Total, past.Currency)),
        };
        figures.AddRange(past.Payments.Select(payment => new Figure(text.PaymentMethod(payment.Method), Figure(payment.Amount, past.Currency))));

        var rail = new Rail.Past(
            text.PastTicket,
            number,
            past.StaffName is null ? $"{date} {clock}" : $"{date} {clock} · {past.StaffName}",
            figures,
            text.PastTicketFooter);

        var bottom = new BottomBar(
            [new Figure(text.Subtotal, Figure(past.Subtotal, past.Currency)), new Figure(text.TaxIncluded, Figure(past.TaxTotal, past.Currency))],
            text.TicketTotal.ToUpperInvariant(),
            total is { } t ? DisplayFigures.AmountWithCurrency(t, text) : "?",
            new PrimaryKey(text.Close, null, CloseKey, Enabled: true));

        return new TillScreen(text.RightToLeft, top, notice, cart, rail, bottom, new FieldChip(text.NextCount("1"), false), null);
    }

    /// <summary>
    /// The tickets cancelled today, newest first, each with who cancelled it, when, and what it
    /// held, and "Reprendre" (D-087). Drawn in the rail, like every other panel of the sale screen.
    /// </summary>
    private static Rail.Drafts DraftsOf(ScreenState state)
    {
        var text = state.Text;
        var rows = (state.Drafts ?? []).Select(draft =>
        {
            var when = DisplayFigures.Clock(Local(state, draft.At));
            var total = draft.Cart.Total is { } sum ? DisplayFigures.AmountWithCurrency(sum, text) : "—";
            return new DraftRow(
                draft.Id,
                text.CancelledAt(when, draft.ByName),
                $"{text.Lines(draft.Cart.ActiveLines.Count)} · {total}",
                text.ResumeTicket);
        }).ToList();

        return new Rail.Drafts(text.DraftsTitle, text.DraftsHint, rows, rows.Count == 0 ? text.NoDrafts : null, text.Close);
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
        var totalDue = Try(() => state.Cart.Total) ?? (currency is { } known ? Domain.Values.Money.Zero(known) : (Money?)null);
        // Not while a sale is unconfirmed: Encaisser would send it again (D-085).
        var canCollect = state.Cart.ActiveLines.Count > 0 && state.Unconfirmed is null && state.Weighing is null && state.Discounting is null;

        // What the drawer takes: the total rounded to the cash step, by the same function the
        // server uses (Money.ToCashTender, D-034), so the button and the receipt agree.
        var cashDetail = totalDue is { } due
            ? text.CashAmount(DisplayFigures.AmountWithCurrency(due.ToCashTender().Tendered, text))
            : null;

        return new BottomBar(
            [
                // Before and what came off (B4): "3 362,80" then "−42,00", the board's two figures.
                new Figure(text.Subtotal, state.Cart.Subtotal is { } s ? DisplayFigures.Amount(s) : totalDue is { } z0 ? DisplayFigures.Amount(z0) : "—"),
                new Figure(text.Discounts, Try(() => state.Cart.DiscountTotal) is { } off
                    ? DisplayFigures.Amount(-off)
                    : totalDue is { } z ? DisplayFigures.Amount(Domain.Values.Money.Zero(z.Currency)) : "—"),
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
public sealed record FrameChanges(bool Top, bool Notice, bool Cart, bool Rail, bool Bottom, bool Field = false, bool Results = false)
{
    public static FrameChanges All { get; } = new(true, true, true, true, true, true, true);
}

/// <summary>The chip in the search field (G1 board): "QTÉ × 1", marked when a count was typed.</summary>
public sealed record FieldChip(string Label, bool Active);

/// <summary>The name search's results (B1): the rows, or a sentence when there are none to show.</summary>
public sealed record ResultsView(string Query, IReadOnlyList<ResultRow> Rows, string? Message);

/// <summary>One product found: its name, "143,00 DA · stock 12" or why it cannot be sold, and whether a touch sells it.</summary>
public sealed record ResultRow(string VariantId, string Title, string Detail, Chip? Chip, bool Available, bool Highlighted);

/// <summary>A row of the "Tickets" list: "14:05 · Ticket n° …", "3 lignes · 1 240,00", and a label when it is not a plain sale.</summary>
public sealed record TicketRow(string TransactionId, string Title, string Detail, Chip? Chip);

/// <param name="Tab">The open ticket; null on the sign-in screen, where there is none.</param>
/// <param name="Parked">The tickets on hold, oldest first, each a tab that a touch brings back (D-087).</param>
public sealed record TopBar(string? Place, TicketTab? Tab, Connection Connection, StaffChip? Staff, string Clock, IReadOnlyList<ParkedTab> Parked);

/// <summary>A ticket on hold: "Attente 14:05 · 3 lignes".</summary>
public sealed record ParkedTab(string Id, string Title, string Detail);

/// <summary>"Ticket en cours · 14 lignes". No number until the sale is recorded (D-070).</summary>
public sealed record TicketTab(string Title, string Detail);

public sealed record Connection(string Label, Tone Tone);

public sealed record StaffChip(string Name, string Role);

/// <summary>The notice slot: a label, its tone, and a sentence. Never a tone without a label.</summary>
public sealed record NoticeLine(Tone Tone, string Label, string Text);

/// <param name="TicketDiscount">The ticket's discount, as a row at its end (B4); null with none.</param>
public sealed record CartView(
    IReadOnlyList<string> Columns,
    IReadOnlyList<LineRow> Lines,
    EmptyState? Empty,
    LineActions? Actions,
    DiscountLine? TicketDiscount = null);

/// <summary>A discount under a line, or at the end of the ticket: "REMISE 10 % · GESTE COMMERCIAL", "−42,00".</summary>
public sealed record DiscountLine(string Label, string Amount);

/// <summary>Percent or amount, one of the panel's two keys.</summary>
public sealed record DiscountFormChoice(string Form, string Label, bool Selected);

/// <summary>A reason in the panel's list, chosen or not.</summary>
public sealed record ReasonRow(string Code, string Label, bool Selected);

/// <summary>Somebody who may be asked to authorise; without a PIN they are listed, and cannot be chosen.</summary>
public sealed record ApproverRow(string StaffId, string Name, string Role, bool Selected, bool Available);

/// <param name="Struck">Taken out before payment: drawn struck through, never counted.</param>
/// <param name="Selected">Touched by the cashier; its actions open beneath it.</param>
/// <param name="Discount">Its own discount, drawn under it (B4); null with none.</param>
public sealed record LineRow(
    string LineId,
    string Quantity,
    string Article,
    IReadOnlyList<Chip> Chips,
    string UnitPrice,
    string Total,
    bool Struck,
    bool Selected,
    DiscountLine? Discount = null);

public sealed record Chip(Tone Tone, string Label);

public sealed record EmptyState(string Title, string Hint);

/// <summary>What the selected line offers. B2 has the stepper and "Retirer la ligne"; B4 and B5 add theirs.</summary>
/// <param name="Count">The line's count, which − and + change by one.</param>
/// <param name="Quantity">The same count, as the stepper shows it.</param>
/// <param name="MayDecrease">False at one: the last unit goes with "Retirer la ligne".</param>
/// <param name="Reweigh">"Poids", for a line whose weight was typed; null otherwise (B3).</param>
/// <param name="IsWeighed">A weighed line: no stepper, one weighing (B3).</param>
/// <param name="Discount">"Remise" (B4): locked, and pressable; a cashier's asks for a manager's PIN.</param>
/// <param name="Price">"Prix" (B5): locked, rank 3; never on a weighed line.</param>
public sealed record LineActions(
    string LineId, int Count, string Quantity, bool MayDecrease, string Remove, string RemoveKey, string? Reweigh = null, bool IsWeighed = false,
    string? Discount = null, string? DiscountKey = null, string? Price = null);

/// <summary>The weight card (B3, D-090), floating where the search results float.</summary>
/// <param name="PerUnit">"180,00 DA / kg".</param>
/// <param name="Total">"0,556 kg = 100,08 DA", the server's figure; null until it has answered.</param>
/// <param name="Message">What to type, or why what was typed cannot be sold; null once a total is shown.</param>
/// <param name="Refused">The message is a refusal, not a prompt.</param>
public sealed record WeighCard(string Title, string Article, string PerUnit, string? Total, string? Message, bool Refused, string Keys);

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

    /// <summary>At rest: the operation keys, room for the B-block parts, and the Almanac slot at its foot.</summary>
    public sealed record Rest(IReadOnlyList<OperationKey> Operations, AlmanacSlot? Almanac) : Rail;

    /// <summary>The "Tickets" list (B1, D-088): a day, a scope, and the sales, or a sentence.</summary>
    /// <param name="Scope">"Cette caisse" or "Toutes les caisses".</param>
    /// <param name="OtherScope">The key that switches to the other.</param>
    /// <param name="MayGoForward">False on today: there is no tomorrow's list.</param>
    public sealed record Tickets(
        string Label, string Day, string Scope, string OtherScope, IReadOnlyList<TicketRow> Rows, string? Message, bool MayGoForward, string Close) : Rail;

    /// <summary>A past ticket's figures and payments, beside it in the ticket view (B1).</summary>
    public sealed record Past(string Label, string Title, string Subtitle, IReadOnlyList<Figure> Figures, string Footer) : Rail;

    /// <summary>The tickets cancelled today (D-087).</summary>
    /// <param name="Empty">What to say when there are none; null when there are.</param>
    public sealed record Drafts(string Label, string Hint, IReadOnlyList<DraftRow> Rows, string? Empty, string Close) : Rail;

    /// <summary>A sale just recorded: what it came to and what the drawer takes.</summary>
    public sealed record Paid(string Label, string Title, string Subtitle, IReadOnlyList<Figure> Figures, string Footer) : Rail;

    /// <summary>The discount panel (B4): what it is on, percent or amount, what it takes off, the reasons.</summary>
    /// <param name="Detail">What it is taken off: the line's, or the ticket's, total before it.</param>
    /// <param name="Preview">"−42,00 DA", worked out as the sale will; null until a valid value is typed.</param>
    /// <param name="Refused">The message says what is wrong, not what to do next.</param>
    /// <param name="Remove">"Retirer la remise", when one is already given; null otherwise.</param>
    public sealed record Discount(
        string Label, string Title, string Detail, IReadOnlyList<DiscountFormChoice> Forms, string? Preview, string ReasonTitle,
        IReadOnlyList<ReasonRow> Reasons, string? Message, bool Refused, bool MayContinue, string Continue, string Back, string? Remove) : Rail;

    /// <summary>The manager step (B4): who authorises, their PIN as dots, the pad.</summary>
    public sealed record Authorise(
        string Label, string Title, string Summary, string WhoTitle, IReadOnlyList<ApproverRow> Approvers, string PinTitle,
        int PinLength, string? Message, bool MayValidate, string Validate, string Back) : Rail;

    /// <summary>No answer to a sale. Critical, and in the rail because it needs room to say what to do.</summary>
    /// <param name="Acknowledge">The one way on: the cashier has checked (D-085).</param>
    public sealed record Unconfirmed(string Label, string Title, string Body, string Detail, string Acknowledge) : Rail;
}

/// <summary>The rail's operations. B2 has these three; later sessions add theirs.</summary>
public enum Operation
{
    Park,

    /// <summary>"Remise ticket" (B4).</summary>
    TicketDiscount,
    CancelTicket,
    Drafts,

    /// <summary>The "Tickets" list: sales already made (B1).</summary>
    Tickets,
}

/// <summary>An operation key in the rail: its label, its F key when it has one, and whether it is available now.</summary>
public sealed record OperationKey(Operation Operation, string Label, string? Key, bool Enabled);

/// <summary>A cancelled ticket in the drafts list: "Annulé à 14:32 · Nabil B.", "3 lignes · 1 240,00 DA", Reprendre.</summary>
public sealed record DraftRow(string Id, string Title, string Detail, string Resume);

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
