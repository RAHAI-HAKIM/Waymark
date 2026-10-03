using System.Globalization;
using Waymark.Contracts.Pos;
using Waymark.Domain.Enums;
using Waymark.Domain.Values;
using Waymark.Pos.Checkout;

namespace Waymark.Pos.Screen;

/// <summary>What comes back of one line of the past ticket (B9): thousandths of its unit, and whether it goes back on the shelf.</summary>
public sealed record RefundPick(long Quantity, bool Restock);

/// <summary>
/// A refund being prepared on the past ticket open in the ticket view (B9, D-098): what comes back of
/// each line, the line touched, where the money goes, and the server's quote once Continuer asked for
/// it. The reason, its note and the manager step are the counter panel's (<see cref="DiscountState"/>,
/// <see cref="CounterKind.Refund"/>), as a cancel's are.
/// </summary>
/// <param name="Picks">One per line of the ticket, in its order.</param>
/// <param name="To">One of <c>RefundDestinations</c>.</param>
/// <param name="Quote">What the server says the refund comes to: the floating panel is open while it is set.</param>
/// <param name="Sending">The refund is on its way: nothing is pressed twice.</param>
/// <param name="Refused">The server's reason, when it refused: the panel stays, nothing was written.</param>
/// <param name="Customer">The customer attached at the refund, for store credit, when the sale had none (B9a).</param>
public sealed record RefundState(
    IReadOnlyList<RefundPick> Picks,
    int? Selected,
    string To,
    RefundAnswer? Quote = null,
    bool Sending = false,
    string? Refused = null,
    AttachedCustomer? Customer = null)
{
    /// <summary>Nothing chosen yet; everything chosen goes back on the shelf unless the cashier says not.</summary>
    public static RefundState For(PastTicketDetail ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return new([.. ticket.Lines.Select(_ => new RefundPick(0, true))], null, RefundDestinations.Cash);
    }

    public int Chosen => Picks.Count(pick => pick.Quantity > 0);
}

/// <summary>
/// The refund's rules at the till (B9, D-098), apart from the window so they are tested without one:
/// which tickets may be refunded, what a touch and the line's keys do, what the panels show and what
/// is sent. <b>What a refund gives back is never worked out here</b>: the server's quote says it.
/// </summary>
public static class RefundScreen
{
    /// <summary>The line's keys in the rail, as the counter panel's form choices.</summary>
    public const string LessKey = "less";

    public const string MoreKey = "more";

    public const string RestockKey = "restock";

    /// <summary>A sale, not a refund; paid, or refunded in part; with something left to bring back.</summary>
    public static bool Refundable(PastTicketDetail ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return ticket.OriginalInvoiceNumber is null
            && ticket.Status is "completed" or "partially_refunded"
            && ticket.Lines.Any(line => Left(line) > 0);
    }

    /// <summary>What is left of a line, in thousandths: sold, less what refunds already brought back.</summary>
    public static long Left(PastTicketLineWire line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return Thousandths(line.Quantity) - Thousandths(line.Returned ?? "0");
    }

    /// <summary>
    /// A line of the ticket touched: it becomes the line the keys act on, and if nothing comes back of
    /// it yet, one unit does; a weighed line, all of it, since it comes back whole. A line with nothing
    /// left is not taken.
    /// </summary>
    public static RefundState Touch(RefundState state, PastTicketDetail ticket, int index)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ticket);
        if (index < 0 || index >= ticket.Lines.Count || Left(ticket.Lines[index]) <= 0)
        {
            return state;
        }

        var pick = state.Picks[index];
        var picked = pick.Quantity > 0 ? pick : pick with { Quantity = Step(ticket.Lines[index]) };
        return state with { Selected = index, Picks = With(state.Picks, index, picked), Refused = null };
    }

    /// <summary>"− 1", "+ 1" or "En rayon" on the line touched: never below nothing, never above what is left.</summary>
    public static RefundState Press(RefundState state, PastTicketDetail ticket, string key)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ticket);
        if (state.Selected is not { } index)
        {
            return state;
        }

        var line = ticket.Lines[index];
        var pick = state.Picks[index];
        var step = Step(line);
        var next = key switch
        {
            LessKey => pick with { Quantity = Math.Max(0, pick.Quantity - step) },
            MoreKey => pick with { Quantity = Math.Min(Left(line), pick.Quantity + step) },
            RestockKey => pick with { Restock = !pick.Restock },
            _ => pick,
        };
        return state with { Picks = With(state.Picks, index, next), Refused = null };
    }

    /// <summary>What the till sends: the lines named by variant and price, what comes back, and nothing it worked out.</summary>
    public static RefundRequest Request(
        string terminalId, PastTicketDetail ticket, RefundState state, string reasonCode, string? note, string? authorisation, bool quote)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(state);
        var lines = ticket.Lines
            .Select((line, index) => (line, pick: state.Picks[index]))
            .Where(chosen => chosen.pick.Quantity > 0)
            .Select(chosen => new RefundLineRequest(
                chosen.line.VariantId ?? string.Empty, chosen.line.UnitPrice, Contracts.Figures.Quantity(chosen.pick.Quantity), chosen.pick.Restock))
            .ToList();
        return new RefundRequest(terminalId, ticket.TransactionId, lines, reasonCode, state.To, note, state.Customer?.CustomerId, authorisation, quote);
    }

    /// <summary>The chips a line of the ticket shows while a refund is prepared: what came back before, what comes back now, and where it goes.</summary>
    public static IReadOnlyList<Chip> Chips(PastTicketLineWire line, RefundPick? pick, TillText text)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(text);
        var chips = new List<Chip>();
        if (Thousandths(line.Returned ?? "0") is > 0 and var before)
        {
            chips.Add(new Chip(Tone.Neutral, text.ReturnedChip(Quantity(before))));
        }

        if (pick is { Quantity: > 0 } chosen)
        {
            chips.Add(new Chip(Tone.Warning, text.ReturnChip(Quantity(chosen.Quantity))));
            chips.Add(new Chip(Tone.Neutral, chosen.Restock ? text.RestockChip : text.NoRestockChip));
        }

        return chips;
    }

    /// <summary>
    /// The refund in the rail (B9), drawn as the counter panel draws a cancel: the line touched with
    /// what comes back of it and its keys, then the shop's return reasons, then Continuer, which asks
    /// the server what the refund comes to.
    /// </summary>
    public static Rail.Discount RailPanel(ScreenState state, DiscountState discounting, PastTicketDetail ticket, RefundState refund)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(discounting);
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(refund);
        var text = state.Text;
        var reasons = discounting.Reasons?.ReasonCodes ?? [];
        var chosen = reasons.FirstOrDefault(reason => reason.Code == discounting.ReasonCode);
        var number = text.TicketNumber(ticket.InvoiceNumber ?? "?");

        if (discounting.Noting && chosen is not null)
        {
            // The note step (F-28): the field holds the note; the panel says what it is for.
            var typed = discounting.Note.Trim();
            var reason = text.RightToLeft ? chosen.LabelAr : chosen.LabelFr;
            return new Rail.Discount(
                text.RefundLabel, number, text.RefundChosen(refund.Chosen), [], typed.Length == 0 ? null : $"« {typed} »", text.ReasonTitle,
                [new ReasonRow(chosen.Code, reason, true)], text.NotePrompt(reason), discounting.Problem == DiscountProblem.NoteMissing,
                typed.Length > 0, text.ContinueKey, text.BackToTicket, null);
        }

        var detail = refund.Selected is { } index
            ? text.RefundLineDetail(
                $"{ticket.Lines[index].ProductName} {ticket.Lines[index].VariantName}",
                Quantity(refund.Picks[index].Quantity),
                Quantity(Left(ticket.Lines[index])))
            : text.RefundPrompt;

        IReadOnlyList<DiscountFormChoice> keys = refund.Selected is { } selected
            ?
            [
                new DiscountFormChoice(LessKey, text.LessKey, false),
                new DiscountFormChoice(MoreKey, text.MoreKey, false),
                new DiscountFormChoice(RestockKey, text.RestockKey, refund.Picks[selected].Restock),
            ]
            : [];

        var (message, refused) = discounting.Offline || discounting.Problem == DiscountProblem.Offline ? (text.RefundOffline, true)
            : refund.Refused is { } why && refund.Quote is null ? ($"{text.RefundRefused} · {why}", true)
            : discounting.Reasons is not null && reasons.Count == 0 ? (text.NoReturnReasons, true)
            : discounting.Problem == DiscountProblem.NothingChosen ? (text.ChooseLines, true)
            : discounting.Problem == DiscountProblem.NoReason ? (text.ChooseReason, true)
            : ((string?)null, false);

        return new Rail.Discount(
            text.RefundLabel,
            number,
            detail,
            keys,
            refund.Chosen > 0 ? text.RefundChosen(refund.Chosen) : null,
            text.ReasonTitle,
            [.. reasons.Select(reason => new ReasonRow(
                reason.Code, text.RightToLeft ? reason.LabelAr : reason.LabelFr, reason.Code == discounting.ReasonCode))],
            message,
            refused,
            refund.Chosen > 0 && discounting.ReasonCode is not null && !discounting.Offline && reasons.Count > 0,
            text.ContinueKey,
            text.BackToTicket,
            null);
    }

    /// <summary>
    /// The money going out, floating over the frozen ticket (D-094), as the payment panel draws the
    /// money coming in: the server's figures, what goes back on the tab, then cash or store credit and
    /// the figure handed over. Store credit is offered only where the server says it may be.
    /// </summary>
    public static PaymentPanel? Panel(ScreenState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Viewing is not { } ticket || state.Refunding is not { Quote: { } quote } refund || state.Discounting is { Authorising: true })
        {
            return null;
        }

        var text = state.Text;
        var currency = quote.Currency ?? ticket.Currency;
        var credit = refund.To == RefundDestinations.StoreCredit;
        var figures = new List<Figure> { new(text.RefundTotal, Shown(quote.Total, currency)) };
        if (Money(quote.ToTab, currency) is { IsPositive: true } tab)
        {
            figures.Add(new Figure(text.RefundToTab, DisplayFigures.Amount(tab)));
        }

        // What the sale spent in store credit comes back as store credit first (B9b, D-101).
        if (Money(quote.ToCredit, currency) is { IsPositive: true } back)
        {
            figures.Add(new Figure(text.RefundToCredit, DisplayFigures.Amount(back)));
        }

        if (!credit && Money(quote.Rest, currency) is { } rest && Money(quote.CashOut, currency) is { } cashOut && rest != cashOut)
        {
            figures.Add(new Figure(text.CashRounding, DisplayFigures.Amount(rest - cashOut)));
        }

        var due = credit ? quote.Rest : quote.CashOut ?? quote.Rest;
        var methods = new List<MethodChoice> { new(PaymentMethod.Cash, text.MethodCash, !credit) };
        if (quote.MayCredit)
        {
            methods.Add(new MethodChoice(PaymentMethod.StoreCredit, text.MethodStoreCredit, credit));
        }

        var restocked = refund.Picks.Count(pick => pick is { Quantity: > 0, Restock: true });

        // Store credit is a named customer's (D-098): with none on the ticket, one is attached first.
        var nameless = credit && !quote.CustomerOnTicket && refund.Customer is null;
        var message = refund.Refused is { } why ? new PanelMessage(text.RefundRefused, why)
            : nameless ? new PanelMessage(text.CreditIsNamed, text.CreditIsNamedDetail, Tone.Warning)
            : null;
        return new PaymentPanel(
            text.RefundTitle,
            text.TicketNumber(ticket.InvoiceNumber ?? "?"),
            figures,
            credit ? text.CreditOut : text.CashOut,
            Money(due, currency) is { } amount ? DisplayFigures.AmountWithCurrency(amount, text) : "?",
            text.RefundLinesTitle,
            [],
            text.RefundLinesSummary(refund.Chosen, restocked),
            methods,
            null,
            text.RefundCloseHint,
            message,
            false,
            text.RefundTitle,
            !refund.Sending && !nameless,
            text.EnterKey,
            text.EscapeKey,
            nameless ? new PanelAction(text.AttachClient, "F5") : null);
    }

    /// <summary>A line comes back a unit at a time; a weighed one, all of what is left at once.</summary>
    private static long Step(PastTicketLineWire line) => line.Weighed ? Left(line) : 1_000;

    private static IReadOnlyList<RefundPick> With(IReadOnlyList<RefundPick> picks, int index, RefundPick pick) =>
        [.. picks.Select((existing, i) => i == index ? pick : existing)];

    /// <summary>Exact decimal text from the wire, "1.240", as thousandths; zero for anything else.</summary>
    private static long Thousandths(string text) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? (long)(value * 1_000) : 0;

    /// <summary>A quantity on screen: "2", "1,24".</summary>
    private static string Quantity(long thousandths)
    {
        var wire = Contracts.Figures.Quantity(thousandths);
        return wire.Replace('.', DisplayFigures.DecimalSeparator);
    }

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

    private static string Shown(string? text, string? currency) =>
        Money(text, currency) is { } money ? DisplayFigures.Amount(money) : "?";
}
