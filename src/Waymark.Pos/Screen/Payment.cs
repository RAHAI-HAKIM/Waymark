using Waymark.Contracts.Pos;
using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Pos.Screen;

/// <summary>A card or BaridiMob part added in the payment panel (B6, D-095).</summary>
/// <param name="Reference">As kept: trimmed, and never a card number (<see cref="PaymentReference"/>).</param>
public sealed record TenderEntry(PaymentMethod Method, Money Amount, string? Reference)
{
    /// <summary>What the sale sends: the part as given, which the server settles again on its own total.</summary>
    public TenderRequest ToWire() => new(
        Method == PaymentMethod.MobileWallet ? TenderMethods.MobileWallet : TenderMethods.Card,
        Contracts.Figures.Amount(Amount.MinorUnits),
        Reference);

    public TenderPart ToPart() => new(Method, Amount, Reference);
}

/// <summary>What the payment panel has to say is wrong with the part being added.</summary>
public enum PaymentProblem
{
    /// <summary>More than is left to pay: a card gives no change.</summary>
    AboveRest,

    /// <summary>Not an amount above zero with at most two decimals.</summary>
    PartInvalid,

    /// <summary>The reference read as a card number: it was cleared, never kept.</summary>
    ReferenceLooksLikeCard,

    /// <summary>Too long, or a character no terminal prints.</summary>
    ReferenceInvalid,
}

/// <summary>
/// The payment panel while it is open (B6, D-095): the parts added, which method's key is down, what
/// is typed for the part being added and its reference, and which of the two takes the keys. The
/// ticket is frozen under it (D-094).
/// </summary>
/// <param name="Typed">The part's amount as typed; empty while it is the rest, prefilled.</param>
/// <param name="Refused">The server's reason, when it refused the sale: the parts stay.</param>
/// <param name="Sending">The sale is on its way: nothing is pressed twice.</param>
public sealed record PaymentState(
    IReadOnlyList<TenderEntry> Parts,
    PaymentMethod Method,
    string Typed,
    string Reference,
    bool OnReference,
    PaymentProblem? Problem = null,
    string? Refused = null,
    bool Sending = false)
{
    /// <summary>Opened by Encaisser: no part, cash chosen, so Entrée sends it all in cash.</summary>
    public static PaymentState Open { get; } = new([], PaymentMethod.Cash, string.Empty, string.Empty, false);

    /// <summary>Whether a part is being added: a card or BaridiMob key is down.</summary>
    public bool AddingPart => Method != PaymentMethod.Cash;
}

/// <summary>The payment panel as drawn, floating over the frozen ticket (G1 board "Encaissement").</summary>
/// <param name="Figures">"Total du ticket", then "Arrondi espèces".</param>
/// <param name="Due">The cash left to collect, rounded to the cash step: the big figure.</param>
/// <param name="NoParts">What the parts list says with none; null when there are some.</param>
/// <param name="Entry">The part being added; null while cash is chosen.</param>
/// <param name="Hint">A line under the fields: how to leave, or that the amount is the rest.</param>
/// <param name="PadAvailable">The pad types a part's amount or reference: with cash chosen there is nothing to type.</param>
public sealed record PaymentPanel(
    string Title,
    string Subtitle,
    IReadOnlyList<Figure> Figures,
    string DueLabel,
    string Due,
    string PartsTitle,
    IReadOnlyList<PartRow> Parts,
    string? NoParts,
    IReadOnlyList<MethodChoice> Methods,
    PartEntry? Entry,
    string? Hint,
    PanelMessage? Message,
    bool PadAvailable,
    string Primary,
    bool MayPrimary,
    string PrimaryKey,
    string CloseKey);

/// <summary>A part in the list, with its ✕.</summary>
public sealed record PartRow(int Index, PaymentMethod Method, string Label, string? Reference, string Amount);

/// <summary>Espèces, Carte, BaridiMob.</summary>
public sealed record MethodChoice(PaymentMethod Method, string Label, bool Selected);

/// <summary>A card or BaridiMob part being added: its amount, its reference, and the rest after it.</summary>
/// <param name="Prefilled">The amount shown is the rest: the first key typed replaces it.</param>
/// <param name="Rest">What is left after this part, or "—" when the part is more than is left.</param>
/// <param name="Currency">The symbol beside the amount: "DA".</param>
public sealed record PartEntry(
    string AmountTitle,
    string Amount,
    bool Prefilled,
    string ReferenceTitle,
    string Reference,
    string ReferencePlaceholder,
    bool OnReference,
    string RestTitle,
    string Rest,
    string WholeRest,
    string WholeRestAmount,
    string NoChange,
    string Currency);

/// <summary>A refusal in the panel, labelled before it is coloured.</summary>
public sealed record PanelMessage(string Title, string Body);

/// <summary>
/// The payment panel's rules (B6, D-095), apart from the window so they are tested without one: what
/// it shows, what a key typed does, and whether a part may be added. How the parts and the cash settle
/// is <see cref="Tender"/>'s, asked here as the server asks it; with no part the ticket is all cash
/// and the rule is not asked, as on the server.
/// </summary>
public static class PaymentScreen
{
    /// <summary>Longest amount typed: 9 999 999,99.</summary>
    private const int MaxTyped = 10;

    public static PaymentPanel? Of(ScreenState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Paying is not { } open || state.Cart.Total is not { } total)
        {
            return null;
        }

        var text = state.Text;
        var rest = RestOf(total, open.Parts);
        var cash = CashOf(total, open.Parts);

        var parts = open.Parts
            .Select((part, index) => new PartRow(
                index, part.Method, Label(part.Method, text), part.Reference is null ? null : text.PartReference(part.Reference), DisplayFigures.Amount(part.Amount)))
            .ToList();
        var methods = new[] { PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.MobileWallet }
            .Select(method => new MethodChoice(method, Label(method, text), method == open.Method))
            .ToList();

        PartEntry? entry = null;
        var mayPrimary = !open.Sending;
        var message = open.Refused is { } refused ? new PanelMessage(text.SaleRefusedInPanel, refused) : null;
        string? hint = text.PaymentCloseHint;

        if (open.AddingPart)
        {
            var amount = AmountOf(open, rest);
            var above = amount is { } a && a > rest;
            var prefilled = open.Typed.Length == 0;
            entry = new PartEntry(
                text.PartAmountTitle(Label(open.Method, text)),
                prefilled ? DisplayFigures.Amount(rest) : open.Typed,
                prefilled,
                text.ReferenceTitle,
                open.Reference,
                text.ReferencePlaceholder,
                open.OnReference,
                text.RestAfterPart,
                amount is { } part && !above ? DisplayFigures.AmountWithCurrency(rest - part, text) : "—",
                text.WholeRest,
                DisplayFigures.Amount(rest),
                text.NoChangeOnCard,
                text.CurrencySymbol(total.Currency));
            hint = prefilled ? text.PartPrefilled : null;
            mayPrimary = mayPrimary && amount is { IsPositive: true } && !above;

            message = open.Problem switch
            {
                PaymentProblem.ReferenceLooksLikeCard => new PanelMessage(text.ReferenceRefused, text.ReferenceLooksLikeCard),
                PaymentProblem.ReferenceInvalid => new PanelMessage(text.ReferenceRefused, text.ReferenceInvalidDetail),
                PaymentProblem.PartInvalid => new PanelMessage(text.PartInvalid, text.PartInvalidDetail),
                _ when above => new PanelMessage(text.AboveRest, text.AboveRestDetail(DisplayFigures.AmountWithCurrency(rest, text))),
                _ => message,
            };
        }

        return new PaymentPanel(
            text.PaymentTitle,
            text.PaymentSubtitle(state.Cart.ActiveLines.Count),
            [new Figure(text.TicketTotal, DisplayFigures.Amount(total)), new Figure(text.CashRounding, DisplayFigures.Amount(cash.Variance))],
            text.CashToCollect,
            DisplayFigures.AmountWithCurrency(cash.Tendered, text),
            text.PartsTitle,
            parts,
            parts.Count == 0 ? text.NoParts : null,
            methods,
            entry,
            hint,
            message,
            open.AddingPart && !open.Sending,
            open.AddingPart ? text.AddPart : text.PaymentValidate,
            mayPrimary,
            text.EnterKey,
            text.EscapeKey);
    }

    /// <summary>What is left to pay once the parts added are taken off.</summary>
    public static Money RestOf(Money total, IReadOnlyList<TenderEntry> parts) =>
        parts.Aggregate(total, (left, part) => left - part.Amount);

    /// <summary>The cash to collect and its rounding: the whole ticket with no part, else <see cref="Tender.Settle"/>'s.</summary>
    public static CashTender CashOf(Money total, IReadOnlyList<TenderEntry> parts) => parts.Count == 0
        ? total.ToCashTender()
        : Tender.Settle(total, [.. parts.Select(part => part.ToPart())]).Cash;

    /// <summary>The part's amount: what was typed, or the rest while nothing is; null when what was typed is no amount.</summary>
    public static Money? AmountOf(PaymentState open, Money rest)
    {
        ArgumentNullException.ThrowIfNull(open);
        if (open.Typed.Length == 0)
        {
            return rest;
        }

        return DiscountEntry.TryParse(open.Typed, DiscountForms.Amount, out var hundredths)
            ? Money.FromMinorUnits(hundredths, rest.Currency)
            : null;
    }

    /// <summary>
    /// "Ajouter la part" (Entrée): the part goes in the list and cash is chosen again, or the panel
    /// says why not. A reference that reads as a card number is cleared, never kept (D-095).
    /// </summary>
    public static PaymentState AddPart(PaymentState open, Money total)
    {
        ArgumentNullException.ThrowIfNull(open);
        if (!open.AddingPart)
        {
            return open;
        }

        var rest = RestOf(total, open.Parts);
        if (AmountOf(open, rest) is not { IsPositive: true } amount)
        {
            return open with { Problem = PaymentProblem.PartInvalid, Refused = null };
        }

        if (amount > rest)
        {
            return open with { Problem = PaymentProblem.AboveRest, Refused = null };
        }

        switch (PaymentReference.Read(open.Reference, out var reference))
        {
            case ReferenceVerdict.LooksLikeACardNumber:
                return open with { Reference = string.Empty, OnReference = true, Problem = PaymentProblem.ReferenceLooksLikeCard, Refused = null };

            case ReferenceVerdict.Invalid:
                return open with { OnReference = true, Problem = PaymentProblem.ReferenceInvalid, Refused = null };
        }

        return open with
        {
            Parts = [.. open.Parts, new TenderEntry(open.Method, amount, reference)],
            Method = PaymentMethod.Cash,
            Typed = string.Empty,
            Reference = string.Empty,
            OnReference = false,
            Problem = null,
            Refused = null,
        };
    }

    /// <summary>A method's key: cash takes nothing typed; a card or BaridiMob starts a part at the rest.</summary>
    public static PaymentState Choose(PaymentState open, PaymentMethod method)
    {
        ArgumentNullException.ThrowIfNull(open);
        return open with { Method = method, Typed = string.Empty, Reference = string.Empty, OnReference = false, Problem = null };
    }

    /// <summary>
    /// A key typed, on the pad or the keyboard, while a part is being added: to the reference when it
    /// has the keys, else to the amount, whose first key replaces the prefilled rest. Nothing with cash chosen.
    /// </summary>
    public static PaymentState Press(PaymentState open, char key)
    {
        ArgumentNullException.ThrowIfNull(open);
        if (!open.AddingPart || open.Sending)
        {
            return open;
        }

        if (open.OnReference)
        {
            return open.Reference.Length >= PaymentReference.MaximumLength + 8
                ? open
                : open with { Reference = open.Reference + key, Problem = null };
        }

        var mark = key is ',' or '.';
        if (!(key is >= '0' and <= '9' || mark) || open.Typed.Length >= MaxTyped || (mark && open.Typed.Contains(',', StringComparison.Ordinal)))
        {
            return open;
        }

        var typed = open.Typed + (mark ? ',' : key);
        return open with { Typed = typed == "," ? "0," : typed, Problem = null };
    }

    /// <summary>⌫: the last character of whichever field has the keys.</summary>
    public static PaymentState Backspace(PaymentState open)
    {
        ArgumentNullException.ThrowIfNull(open);
        if (!open.AddingPart)
        {
            return open;
        }

        return open.OnReference
            ? open with { Reference = open.Reference.Length > 0 ? open.Reference[..^1] : string.Empty, Problem = null }
            : open with { Typed = open.Typed.Length > 0 ? open.Typed[..^1] : string.Empty, Problem = null };
    }

    /// <summary>"Tout le reste": the amount goes back to the rest, prefilled.</summary>
    public static PaymentState WholeRest(PaymentState open)
    {
        ArgumentNullException.ThrowIfNull(open);
        return open with { Typed = string.Empty, OnReference = false, Problem = null };
    }

    /// <summary>A part's ✕.</summary>
    public static PaymentState RemovePart(PaymentState open, int index)
    {
        ArgumentNullException.ThrowIfNull(open);
        return index < 0 || index >= open.Parts.Count
            ? open
            : open with { Parts = [.. open.Parts.Where((_, i) => i != index)], Problem = null, Refused = null };
    }

    public static string Label(PaymentMethod method, TillText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return method switch
        {
            PaymentMethod.Card => text.MethodCard,
            PaymentMethod.MobileWallet => text.MethodWallet,
            _ => text.MethodCash,
        };
    }
}
