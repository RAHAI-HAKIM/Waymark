using System.Globalization;
using Waymark.Contracts.Pos;
using Waymark.Contracts.Reference;
using Waymark.Domain.Customers;
using Waymark.Domain.Values;
using Waymark.Pos.Checkout;

namespace Waymark.Pos.Screen;

/// <summary>Which customer panel floats over the frozen ticket (B7, D-094).</summary>
public enum CustomerPanelKind
{
    /// <summary>"Client": a number typed, the customers with it, one attached.</summary>
    Search,

    /// <summary>"Nouveau client": a name and a number, the notice handed over (D-096).</summary>
    Create,

    /// <summary>"Modifier le carnet": the limit, the freeze, the tab closed (rank 3).</summary>
    ChangeTab,

    /// <summary>"Remboursement": cash into the drawer for the tab (D-055).</summary>
    Repay,

    /// <summary>"Remboursement encaissé": the new balance, once.</summary>
    Repaid,

    /// <summary>"Avoir émis": store credit issued by a refund, the balance shown once (B9a).</summary>
    CreditIssued,

    /// <summary>"Petite caisse" (B10): cash in or out with no sale behind it, its reason and amount.</summary>
    PettyCash,

    /// <summary>The paid-in or paid-out recorded, once.</summary>
    PettyCashDone,

    /// <summary>"Pointage" (B10): a person, their PIN, then in or out.</summary>
    Clock,

    /// <summary>The arrival or the departure recorded, once.</summary>
    ClockDone,

    /// <summary>"Ouvrir la caisse" (C1, D-111): the float counted. It has no way out but opening, or another person.</summary>
    DrawerOpen,

    /// <summary>"Clôturer la caisse", 1 of 3: the drawer counted.</summary>
    DrawerCount,

    /// <summary>2 of 3: the count confirmed, with its variance and its note to who may see them.</summary>
    DrawerConfirm,

    /// <summary>A blind count past the threshold: the note the server asked for, the count frozen.</summary>
    DrawerNote,

    /// <summary>3 of 3: closed, with its Z number. It has no way out but "Terminer", which signs out.</summary>
    DrawerClosed,
}

/// <summary>
/// A customer panel while it is open (B7, B9a). One record for the five panels, each reading the fields
/// it has: what is typed, what the server answered, and whether something is on its way.
/// </summary>
/// <param name="Phone">The number as typed, digits only; in the search, what was typed: a full name or the number (D-100).</param>
/// <param name="OnName">The name field has the keys (creation), else the number's.</param>
/// <param name="Found">The server's answer to the number; null until asked.</param>
/// <param name="Chosen">The customer touched in the results.</param>
/// <param name="Typed">An amount typed: the new limit, the repayment.</param>
/// <param name="Refused">The server's reason, or the till's, when something was refused.</param>
/// <param name="ForRefund">Attaching a customer to a refund for its store credit (B9a), not to the ticket.</param>
/// <param name="Before">The tab before a repayment; <see cref="After"/> after it.</param>
/// <param name="Issued">The refund that issued store credit, and to whom (<see cref="IssuedTo"/>).</param>
/// <param name="Checked">Créer was pressed: the fields to correct are marked from then on.</param>
/// <param name="Direction">"Petite caisse" (B10): <c>in</c> or <c>out</c>, once chosen.</param>
/// <param name="Pin">"Pointage" (B10): the PIN typed, shown as dots, sent once.</param>
/// <param name="Staff">"Pointage": who may clock, the sign-in list.</param>
/// <param name="Clocked">"Pointage": the server's answer once in or out.</param>
/// <param name="Drawer">The drawer's panels (C1): the till's cash session as the server last gave it.</param>
/// <param name="Closed">The close as the server recorded it, for the result.</param>
/// <param name="Frozen">A blind count was confirmed: it is not typed again (D-111).</param>
public sealed record CustomerPanelState(
    CustomerPanelKind Kind,
    string Phone = "",
    string Name = "",
    bool OnName = false,
    CustomerSearchAnswer? Found = null,
    string? Chosen = null,
    string Typed = "",
    ReasonCodeList? Reasons = null,
    string? ReasonCode = null,
    string? Refused = null,
    bool Sending = false,
    bool ForRefund = false,
    TabAnswer? Before = null,
    TabAnswer? After = null,
    RefundAnswer? Issued = null,
    string? IssuedTo = null,
    DateTimeOffset? At = null,
    bool Checked = false,
    string? Direction = null,
    string Pin = "",
    TillStaff? Staff = null,
    ClockAnswer? Clocked = null,
    CashSessionState? Drawer = null,
    ClosedCashSessionWire? Closed = null,
    bool Frozen = false);

/// <summary>The carnet open in the ticket's place (B7): the server's answer, and when and by whom it was opened, which the server logged.</summary>
public sealed record CarnetState(TabAnswer? Tab, bool Offline, DateTimeOffset OpenedAt, string? OpenedBy);

/// <summary>
/// What the till knows of customers for one frame (B7, D-096): the tenant's two switches, the tab of
/// the customer attached to the ticket, and what is open.
/// </summary>
/// <param name="Module">The tenant keeps customers: without it no customer key exists.</param>
/// <param name="TabAsPart">The tab may be a part of a ticket; off, a ticket goes on it whole or not at all.</param>
/// <param name="Tab">The attached customer's tab, as the server last answered; null until asked.</param>
public sealed record CustomerScreenState(
    bool Module,
    bool TabAsPart,
    TabAnswer? Tab = null,
    CarnetState? Carnet = null,
    CustomerPanelState? Panel = null);

/// <summary>The customer key beside the field (G1 board, F5): "Client · par téléphone", or the attached customer with ✕.</summary>
public sealed record ClientKey(string Label, string? Detail, bool Attached, string Key);

/// <summary>A floating customer panel as drawn: the payment panel's frame, its parts named (B7).</summary>
/// <param name="Tiles">Figures in boxes along the top: "SOLDE DÛ", "PLAFOND ACTUEL".</param>
/// <param name="Big">One large figure with its label: a new balance, a credit balance.</param>
/// <param name="Figures">Rows of figures under it: "Solde avant", "Remboursé en espèces".</param>
/// <param name="Notice">The information notice's block on a creation (Art. 32).</param>
/// <param name="Keys">Secondary keys: Geler, Fermer le carnet, Tout le dû.</param>
/// <param name="Pad">The pad, for a number or an amount.</param>
/// <param name="Locked">The key that goes on may ask a PIN: its padlock, as the board draws it.</param>
public sealed record FormPanel(
    string? Label,
    string Title,
    string? Subtitle,
    IReadOnlyList<Figure> Tiles,
    IReadOnlyList<FormField> Fields,
    string? Note,
    string? Notice,
    PanelMessage? Message,
    string? RowsTitle,
    IReadOnlyList<FormRow> Rows,
    string? NoRows,
    IReadOnlyList<FormKey> Keys,
    string? BigLabel,
    string? Big,
    IReadOnlyList<Figure> Figures,
    bool Pad,
    string Primary,
    bool MayPrimary,
    bool Locked,
    string PrimaryKey,
    string CloseKey,
    string? Footer)
{
    /// <summary>
    /// Whether the pad reads 1 2 3 on top, as a phone's and every PIN pad of the till do, rather than
    /// 7 8 9 as the payment pad does for money: the field with the keys holds a number or a PIN, which
    /// shows no currency. One till had both layouts for a PIN (block B review).
    /// </summary>
    public bool PhonePad => Fields.FirstOrDefault(entry => entry.Active) is { Suffix: null };

    /// <summary>
    /// The keys are the panel's first choice and are drawn before its field: cash in or cash out is
    /// decided before the amount. They were drawn last, under the reasons (block B review).
    /// </summary>
    public bool KeysFirst { get; init; }

    /// <summary>The rows are the panel's first choice and are drawn before its field: who clocks, then their PIN.</summary>
    public bool RowsFirst { get; init; }

    /// <summary>
    /// The large figure leads, then the tiles and the message, then the fields (C1): a count to confirm
    /// and a close's result are read from the figure down.
    /// </summary>
    public bool BigFirst { get; init; }

    /// <summary>
    /// The panel has no ✕ and Échap does nothing (C1, D-111): nothing sells without an open drawer,
    /// and a close is finished, not left.
    /// </summary>
    public bool NoClose { get; init; }

    /// <summary>The notice and the message lead, before the field (C1): what the drawer's opening is about, then its float.</summary>
    public bool NoticeFirst { get; init; }

    /// <summary>The message's figure, drawn large inside it: a variance, read before its sentence.</summary>
    public string? MessageFigure { get; init; }
}

/// <summary>A field of a panel: what it is, what it holds, and whether it has the keys or is refused.</summary>
public sealed record FormField(string Id, string Title, string Value, string? Placeholder, bool Active, bool Invalid, string? Suffix = null);

/// <summary>A row to touch: a customer found, a reason.</summary>
public sealed record FormRow(string Id, string Label, string? Detail, bool Selected);

/// <summary>A secondary key; <paramref name="Locked"/> when it may ask a PIN; <paramref name="Chosen"/> when it is the choice made.</summary>
public sealed record FormKey(string Id, string Label, bool Enabled, bool Locked, bool Chosen = false);

/// <summary>The carnet in the ticket's place (B7): it does not float, it is a view of its own.</summary>
public sealed record CarnetView(
    string Label,
    string Title,
    string Subtitle,
    NoticeLine? State,
    IReadOnlyList<CarnetFigure> Figures,
    string StatementTitle,
    IReadOnlyList<string> Columns,
    IReadOnlyList<StatementRow> Rows,
    string? Empty,
    string? Message,
    string Footer,
    FormKey Change,
    string Repay,
    bool MayRepay,
    string RepayKey,
    string CloseKey);

public sealed record CarnetFigure(string Label, string Value, string? Detail);

/// <summary>A movement of the statement, with the balance after it.</summary>
public sealed record StatementRow(string Date, string Movement, string Amount, string Balance);

/// <summary>Where a tab stands, read before any colour (CLAUDE.md §6).</summary>
public enum TabStanding
{
    NoTab,
    Open,
    Frozen,
    Overdue,
}

/// <summary>
/// The customer screens' rules (B7, B9a), apart from the window so they are tested without one: what
/// each panel shows, what a key typed does. The tab's figures are the server's; whether a charge fits
/// is <see cref="Tab.Check"/>, asked here as the server asks it.
/// </summary>
public static class CustomerScreen
{
    public const string PhoneField = "phone";

    public const string NameField = "name";

    public const string AmountField = "amount";

    public const string WholeDueKey = "whole_due";

    public const string FreezeKey = "freeze";

    public const string UnfreezeKey = "unfreeze";

    public const string CloseTabKey = "close_tab";

    /// <summary>"Petite caisse": which way the cash goes (B10); the wire's own words.</summary>
    public const string CashInKey = CashDirections.In;

    public const string CashOutKey = CashDirections.Out;

    /// <summary>"Modifier le carnet", or "Ouvrir un carnet", on the carnet view.</summary>
    public const string ChangeKey = "change";

    private const int MaxTyped = 10;

    // ======================================================== the key, the notice

    /// <summary>The customer key: none with the module off; the attached customer once there is one.</summary>
    public static ClientKey? Key(ScreenState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Customer is not { Module: true })
        {
            return null;
        }

        var text = state.Text;
        var attached = state.Refunding is { } refund && state.Viewing is not null ? refund.Customer : state.Cart.Customer;
        return attached is null
            ? new ClientKey(text.ClientKey, text.ClientByPhone, false, "F5")
            : new ClientKey(attached.ShortName, null, true, "F5");
    }

    /// <summary>"CLIENT RATTACHÉ · Samira B. · touchez le nom pour ouvrir le carnet", when nothing else is to be said.</summary>
    public static NoticeLine? Attached(ScreenState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Customer is { Module: true } && state.Cart.Customer is { } customer
            ? new NoticeLine(Tone.Neutral, state.Text.ClientAttached, state.Text.ClientAttachedDetail(customer.ShortName))
            : null;
    }

    // ======================================================== the tab's standing

    /// <summary>Where the tab stands: frozen first, then overdue by the tenant's days, as <see cref="Tab.Check"/> orders them.</summary>
    public static TabStanding Standing(TabAnswer tab, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (tab.Limit is null)
        {
            return TabStanding.NoTab;
        }

        if (tab.Frozen)
        {
            return TabStanding.Frozen;
        }

        return tab.OverdueDays is { } days && tab.OldestUnpaid is { } oldest && now - oldest > TimeSpan.FromDays(days)
            ? TabStanding.Overdue
            : TabStanding.Open;
    }

    /// <summary>Whether a charge goes on the tab: the server's own rule, on the server's figures.</summary>
    public static TabCheck? Check(TabAnswer tab, Money charge, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (tab.Currency is not { } currency || Money(tab.Balance, currency) is not { } balance)
        {
            return null;
        }

        return Domain.Customers.Tab.Check(
            new TabAge(balance, tab.OldestUnpaid), Money(tab.Limit, currency), tab.Frozen, charge, now, tab.OverdueDays);
    }

    // ======================================================== the floating panels

    public static FormPanel? Panel(ScreenState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Customer?.Panel is not { } panel || state.Discounting is { Authorising: true })
        {
            return null;
        }

        return panel.Kind switch
        {
            CustomerPanelKind.Search => SearchPanel(state, panel),
            CustomerPanelKind.Create => CreatePanel(state, panel),
            CustomerPanelKind.ChangeTab => ChangeTabPanel(state, panel),
            CustomerPanelKind.Repay => RepayPanel(state, panel),
            CustomerPanelKind.Repaid => RepaidPanel(state, panel),
            CustomerPanelKind.PettyCash => PettyCashPanel(state, panel),
            CustomerPanelKind.PettyCashDone => PettyCashDonePanel(state, panel),
            CustomerPanelKind.Clock => ClockPanel(state, panel),
            CustomerPanelKind.ClockDone => ClockDonePanel(state, panel),
            CustomerPanelKind.DrawerOpen or CustomerPanelKind.DrawerCount or CustomerPanelKind.DrawerConfirm or CustomerPanelKind.DrawerNote
                or CustomerPanelKind.DrawerClosed => DrawerScreen.Panel(state, panel),
            _ => IssuedPanel(state, panel),
        };
    }

    private static FormPanel SearchPanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var number = IsNumber(panel.Phone);
        var valid = Searchable(panel.Phone);
        var customers = panel.Found?.Customers;
        var message = panel.Refused is { } refused ? new PanelMessage(text.ClientRefused, refused)
            : number && panel.Phone.Length >= 10 && !valid ? new PanelMessage(text.NotAPhone, text.NotAPhoneDetail(Phone(panel.Phone)))
            : panel.Checked && !valid && !number ? new PanelMessage(text.NameIncomplete, text.NameIncompleteDetail)
            : null;

        string primary;
        bool may;
        var locked = false;
        if (customers is { Count: > 0 })
        {
            primary = panel.ForRefund ? text.AttachToRefund : text.AttachToTicket;
            may = panel.Chosen is not null && !panel.Sending;
        }
        else if (customers is not null)
        {
            primary = text.CreateThisClient;
            may = !panel.Sending;
            locked = true;
        }
        else
        {
            primary = text.SearchClient;
            may = valid && !panel.Sending;
        }

        return new FormPanel(
            null,
            text.ClientTitle,
            panel.ForRefund ? text.ClientForRefund : text.ClientForTicket(state.Cart.ActiveLines.Count),
            [],
            [new FormField(PhoneField, text.NameOrPhoneTitle, number ? Phone(panel.Phone) : panel.Phone, null, true, message is not null && panel.Refused is null)],
            text.NameOrPhoneRule,
            null,
            message,
            customers is null ? text.ResultTitle : text.ResultsTitle(customers.Count),
            [.. (customers ?? []).Select(customer => new FormRow(
                customer.CustomerId, customer.Name, Phone(customer.Phone ?? string.Empty), customer.CustomerId == panel.Chosen))],
            customers is null ? (valid ? text.ResultsAfterPause : text.NoSearchYet) : customers.Count == 0 ? text.NoClientWithNumber : null,
            [],
            null,
            null,
            [],
            true,
            primary,
            may,
            locked,
            text.EnterKey,
            text.EscapeKey,
            customers is { Count: > 0 } ? text.ConsultationLogged : customers is not null ? text.CreateNeedsManager : text.TicketFrozenWhileSearching);
    }

    private static FormPanel CreatePanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var nameOk = panel.Name.Trim().Length is > 0 and <= 100;
        var phoneOk = PhoneNumber.TryNormalise(panel.Phone, out _);
        var message = panel.Refused is { } refused ? new PanelMessage(text.ClientRefused, refused)
            : panel.NeedsCorrecting() ? new PanelMessage(text.FieldsToCorrect(Count(!nameOk, !phoneOk)), text.FieldsToCorrectDetail)
            : null;

        return new FormPanel(
            null,
            text.NewClientTitle,
            panel.ForRefund ? text.ClientForRefund : text.NewClientSubtitle,
            [],
            [
                new FormField(NameField, text.NameTitle, panel.Name, null, panel.OnName, panel.NeedsCorrecting() && !nameOk),
                new FormField(PhoneField, text.PhoneTitle, Phone(panel.Phone), null, !panel.OnName, panel.NeedsCorrecting() && !phoneOk),
            ],
            text.NameAndPhoneOnly,
            text.NoticeToHand,
            message,
            null,
            [],
            null,
            [],
            null,
            null,
            [],
            true,
            text.CreateAndAttach,
            !panel.Sending,
            true,
            text.EnterKey,
            text.EscapeKey,
            text.CreateNeedsManager);
    }

    private static FormPanel ChangeTabPanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var tab = state.Customer?.Carnet?.Tab;
        var name = Short(tab);
        var opening = tab?.Limit is null;
        var currency = tab?.Currency ?? "DZD";
        var typedOk = DiscountEntry.TryParse(panel.Typed, DiscountForms.Amount, out _);

        var keys = new List<FormKey>();
        if (!opening)
        {
            keys.Add(tab!.Frozen ? new FormKey(UnfreezeKey, text.UnfreezeKey, !panel.Sending, true) : new FormKey(FreezeKey, text.FreezeKey, !panel.Sending, true));
            keys.Add(new FormKey(CloseTabKey, text.CloseTabKey, !panel.Sending, true));
        }

        return new FormPanel(
            text.CarnetLabel,
            opening ? text.OpenTabTitle(name) : text.ChangeTabTitle(name),
            text.ChangeTabSubtitle,
            [
                new Figure(text.CurrentLimit, Shown(tab?.Limit, currency, text, "—")),
                new Figure(text.BalanceDue, Shown(tab?.Balance, currency, text, "—")),
            ],
            [new FormField(AmountField, text.NewLimit, Typed(panel.Typed), null, true, panel.Typed.Length > 0 && !typedOk, text.CurrencySymbol(Currency.FromCode(currency)))],
            opening ? null : text.CloseTabNote,
            null,
            panel.Refused is { } refused ? new PanelMessage(text.ChangeRefused, refused) : null,
            null,
            [],
            null,
            keys,
            null,
            null,
            [],
            true,
            text.SetLimit,
            typedOk && !panel.Sending,
            true,
            text.EnterKey,
            text.EscapeKey,
            text.OwnerPinAsked);
    }

    private static FormPanel RepayPanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var tab = state.Customer?.Carnet?.Tab;
        var currency = tab?.Currency ?? "DZD";
        var balance = Money(tab?.Balance, currency);
        var amount = DiscountEntry.TryParse(panel.Typed, DiscountForms.Amount, out var minor)
            ? Domain.Values.Money.FromMinorUnits(minor, Currency.FromCode(currency))
            : (Money?)null;
        // The tab's own rule, the one the server applies (D-108): the whole due clears the tab and the
        // cash rounds once; a part is a multiple of the cash step.
        var plan = amount is { } a && balance is { } b ? Domain.Customers.Tab.Repay(b, a) : (TabRepayment?)null;
        var verdict = plan?.Verdict;
        var reasons = ReasonsFor(panel.Reasons, CashInKey);
        var step = Domain.Values.Money.FromMinorUnits(Currency.FromCode(currency).CashRoundingStep, Currency.FromCode(currency));

        var message = panel.Refused is { } refused ? new PanelMessage(text.RepayRefused, refused)
            : verdict == RepaymentVerdict.AboveBalance ? new PanelMessage(text.AboveDue, text.AboveDueDetail(Short(tab), Shown(tab?.Balance, currency, text, "?")))
            : verdict == RepaymentVerdict.NotAboveZero ? new PanelMessage(text.NothingRepaid, text.NothingRepaidDetail)
            : verdict == RepaymentVerdict.NotOnCashStep ? new PanelMessage(text.RepayOnStep, text.RepayOnStepDetail(DisplayFigures.AmountWithCurrency(step, text)))
            : panel.Reasons is not null && reasons.Count == 0 ? new PanelMessage(text.RepayRefused, text.NoCashReasons)
            : null;

        // What the drawer takes, said before "Encaisser" when the cash step moves it: "286,00 dû,
        // arrondi −1,00, espèces à encaisser 285,00".
        List<Figure> figures = [new Figure(text.BalanceDue, Shown(tab?.Balance, currency, text, "?"))];
        if (plan is { Accepted: true } rounded && !rounded.Variance.IsZero)
        {
            figures.Add(new Figure(text.CashRounding, DisplayFigures.Amount(rounded.Variance)));
            figures.Add(new Figure(text.CashTaken, DisplayFigures.AmountWithCurrency(rounded.Cash, text)));
        }

        return new FormPanel(
            text.CarnetLabel,
            text.RepayTitle(Short(tab)),
            text.RepaySubtitle,
            figures,
            [new FormField(AmountField, text.AmountRepaid, Typed(panel.Typed), null, true, message is not null && panel.Refused is null, text.CurrencySymbol(Currency.FromCode(currency)))],
            null,
            null,
            message,
            text.CashReasonTitle,
            [.. reasons.Select(reason => new FormRow(reason.Code, text.RightToLeft ? reason.LabelAr : reason.LabelFr, null, reason.Code == panel.ReasonCode))],
            null,
            [new FormKey(WholeDueKey, text.WholeDue(Shown(tab?.Balance, currency, text, "?")), balance is { IsPositive: true } && !panel.Sending, false)],
            null,
            null,
            [],
            true,
            text.CashIn,
            verdict == RepaymentVerdict.Accepted && panel.ReasonCode is not null && !panel.Sending,
            false,
            text.EnterKey,
            text.EscapeKey,
            null);
    }

    private static FormPanel RepaidPanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var currency = panel.After?.Currency ?? "DZD";
        var before = Money(panel.Before?.Balance, currency);
        var after = Money(panel.After?.Balance, currency);
        var when = panel.At is { } at ? DisplayFigures.Clock(TimeZoneInfo.ConvertTime(at, state.Zone)) : string.Empty;

        // What came off the tab, and what the drawer took for it: the same, unless the cash step
        // rounded the whole due (D-108). "Solde avant 286,00, arrondi −1,00, réglé en espèces 285,00".
        var cleared = before is { } x && after is { } y ? x - y : (Money?)null;
        var cash = Money(panel.After?.CashCollected, currency) ?? cleared;
        List<Figure> paid = [new Figure(text.BalanceBefore, before is { } b ? DisplayFigures.Amount(b) : "?")];
        if (cash is { } taken && cleared is { } off && taken != off)
        {
            paid.Add(new Figure(text.CashRounding, DisplayFigures.Amount(taken - off)));
        }

        paid.Add(new Figure(text.RepaidInCash, cash is { } collected ? DisplayFigures.Amount(collected) : "?"));

        return new FormPanel(
            text.CarnetLabel,
            text.RepaidTitle,
            $"{Short(panel.After)} · {when}{(state.Context?.StaffName is { } staff ? $" · {staff}" : string.Empty)}",
            [],
            [],
            null,
            null,
            null,
            null,
            [],
            null,
            [],
            text.NewBalanceDue,
            after is { } shown ? DisplayFigures.AmountWithCurrency(shown, text) : "?",
            // What came into the drawer, as the amount it is: "Réglé en espèces 286,00", never "−286,00".
            paid,
            false,
            text.Finish,
            true,
            false,
            text.EnterKey,
            text.EscapeKey,
            null);
    }

    private static FormPanel IssuedPanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var issued = panel.Issued;
        var currency = issued?.Currency ?? "DZD";
        var after = Money(issued?.CreditBalance, currency);

        // What this refund issued: the rest given as credit, and the share the sale had paid in credit,
        // which came back as credit first (B9b, D-101). Both are in the balance after.
        var rest = Money(issued?.Rest, currency) is { } given
            ? given + (Money(issued?.ToCredit, currency) ?? Domain.Values.Money.Zero(given.Currency))
            : (Money?)null;
        var number = issued?.InvoiceNumber ?? "?";
        var when = panel.At is { } at ? DisplayFigures.Clock(TimeZoneInfo.ConvertTime(at, state.Zone)) : string.Empty;

        return new FormPanel(
            text.ReturnLabel,
            text.CreditIssuedTitle,
            $"{text.ReturnNumber(number)} · {panel.IssuedTo ?? string.Empty} · {when}",
            [],
            [],
            null,
            null,
            null,
            null,
            [],
            null,
            [],
            text.CreditAfterReturn(panel.IssuedTo ?? string.Empty),
            after is { } shown ? DisplayFigures.AmountWithCurrency(shown, text) : "?",
            [
                new Figure(text.CreditBefore, after is { } a && rest is { } r ? DisplayFigures.Amount(a - r) : "?"),
                new Figure(text.IssuedForReturn(number), rest is { } issuedNow ? $"+{DisplayFigures.Amount(issuedNow)}" : "?"),
            ],
            false,
            text.Finish,
            true,
            false,
            text.EnterKey,
            text.EscapeKey,
            null);
    }

    // ======================================================== B10: petite caisse, pointage

    /// <summary>The cash reasons that go this way: the reason's own direction, or either (D-102).</summary>
    public static IReadOnlyList<ReasonCodeOption> ReasonsFor(ReasonCodeList? reasons, string? direction) =>
        [.. (reasons?.ReasonCodes ?? []).Where(reason => reason.Direction is null || reason.Direction == direction)];

    private static FormPanel PettyCashPanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var reasons = panel.Direction is null ? [] : ReasonsFor(panel.Reasons, panel.Direction);
        var chosen = reasons.FirstOrDefault(reason => reason.Code == panel.ReasonCode);
        var typedOk = DiscountEntry.TryParse(panel.Typed, DiscountForms.Amount, out var hundredths) && hundredths > 0;
        var noteNeeded = chosen is { RequiresNote: true };
        var currency = Currency.FromCode(state.Context?.Currency ?? "DZD");

        var fields = new List<FormField> { new(AmountField, text.AmountTitle, panel.Typed, null, !panel.OnName, panel.Typed.Length > 0 && !typedOk, text.CurrencySymbol(currency)) };
        if (noteNeeded)
        {
            fields.Add(new FormField(NameField, text.NoteTitle, panel.Name, null, panel.OnName, false));
        }

        return new FormPanel(
            null,
            text.PettyCashTitle,
            text.PettyCashSubtitle,
            [],
            fields,
            panel.Direction is null ? text.ChooseCashDirection : null,
            null,
            panel.Refused is { } refused ? new PanelMessage(text.ClientRefused, refused)
                : panel.Reasons is not null && panel.Direction is not null && reasons.Count == 0 ? new PanelMessage(text.ClientRefused, text.NoCashReasons)
                : null,
            panel.Direction is null ? null : panel.Direction == CashOutKey ? text.CashOutReasonTitle : text.CashReasonTitle,
            [.. reasons.Select(reason => new FormRow(reason.Code, text.RightToLeft ? reason.LabelAr : reason.LabelFr, null, reason.Code == panel.ReasonCode))],
            null,
            [
                new FormKey(CashInKey, text.CashInKey, !panel.Sending, false, panel.Direction == CashInKey),
                new FormKey(CashOutKey, text.CashOutKey, !panel.Sending, true, panel.Direction == CashOutKey),
            ],
            null,
            null,
            [],
            true,
            text.RecordKey,
            panel.Direction is not null && typedOk && chosen is not null && (!noteNeeded || panel.Name.Trim().Length > 0) && !panel.Sending,
            panel.Direction == CashOutKey,
            text.EnterKey,
            text.EscapeKey,
            null)
        {
            KeysFirst = true,
        };
    }

    private static FormPanel PettyCashDonePanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var reason = (panel.Reasons?.ReasonCodes ?? []).FirstOrDefault(r => r.Code == panel.ReasonCode);
        var currency = state.Context?.Currency ?? "DZD";
        return new FormPanel(
            text.PettyCashTitle.ToUpperInvariant(), panel.Direction == CashOutKey ? text.CashOutRecorded : text.CashInRecorded, null, [], [], null, null, null, null, [], null, [],
            text.AmountTitle,
            Money(panel.Typed.Replace(',', '.'), currency) is { } amount ? DisplayFigures.AmountWithCurrency(amount, text) : panel.Typed,
            reason is null ? [] : [new Figure(text.ReasonLabel, text.RightToLeft ? reason.LabelAr : reason.LabelFr)],
            false, text.Finish, true, false, text.EnterKey, text.EscapeKey, null);
    }

    private static FormPanel ClockPanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var message = panel.Refused is { } refused ? new PanelMessage(text.ClientRefused, refused) : null;
        return new FormPanel(
            null,
            text.ClockTitle,
            text.ClockSubtitle,
            [],
            [new FormField(PhoneField, text.ClockPinTitle, new string('●', panel.Pin.Length), null, true, message is not null)],
            null,
            null,
            message,
            text.WhoClocks,
            [.. (panel.Staff?.Staff ?? []).Select(person => new FormRow(
                person.StaffId, person.StaffName, text.RightToLeft ? person.RoleLabelAr : person.RoleLabelFr, person.StaffId == panel.Chosen))],
            panel.Staff is null ? text.CarnetOffline : null,
            [],
            null,
            null,
            [],
            true,
            text.ClockPrimary,
            panel.Chosen is not null && panel.Pin.Length >= 4 && !panel.Sending,
            false,
            text.EnterKey,
            text.EscapeKey,
            null)
        {
            RowsFirst = true,
        };
    }

    private static FormPanel ClockDonePanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var name = panel.Staff?.Staff.FirstOrDefault(person => person.StaffId == panel.Chosen)?.StaffName ?? string.Empty;
        var clocked = panel.Clocked;
        var arrived = clocked?.Outcome == ClockOutcomes.ClockedIn;
        var at = clocked?.At is { } when ? DisplayFigures.Clock(TimeZoneInfo.ConvertTime(when, state.Zone)) : "?";
        return new FormPanel(
            text.ClockTitle.ToUpperInvariant(), arrived ? text.ClockedInTitle : text.ClockedOutTitle, null, [], [], null, null, null, null, [], null, [],
            name,
            at,
            clocked?.Since is { } since && !arrived ? [new Figure(text.OnDutySince, DisplayFigures.Clock(TimeZoneInfo.ConvertTime(since, state.Zone)))] : [],
            false, text.Finish, true, false, text.EnterKey, text.EscapeKey, null);
    }

    // ======================================================== the carnet

    /// <summary>The carnet in the ticket's place: the figures, the standing by its label, the statement oldest first.</summary>
    public static CarnetView? Carnet(ScreenState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Customer?.Carnet is not { } carnet)
        {
            return null;
        }

        var text = state.Text;
        var opened = DisplayFigures.Clock(TimeZoneInfo.ConvertTime(carnet.OpenedAt, state.Zone));
        var footer = text.OpeningLogged(opened, carnet.OpenedBy ?? string.Empty);
        if (carnet.Tab is not { Outcome: CustomerOutcomes.Ok } tab)
        {
            var reason = carnet.Offline || carnet.Tab is null
                ? text.CarnetOffline
                : RefusalText.Say(text, carnet.Tab.Refusal, carnet.Tab.Reason, carnet.Tab.Currency ?? "DZD");
            return new CarnetView(
                text.CarnetLabel, text.CarnetLabel, string.Empty, null, [], text.StatementTitle, [], [], null, reason, footer,
                new FormKey(ChangeKey, text.ChangeTabKey, false, true), text.RepayKey, false, text.EnterKey, text.EscapeKey);
        }

        var currency = tab.Currency ?? "DZD";
        var standing = Standing(tab, state.Now);
        var days = tab.OldestUnpaid is { } oldest ? (int)Math.Floor((state.Now - oldest).TotalDays) : (int?)null;
        var since = tab.OldestUnpaid is { } first
            ? TimeZoneInfo.ConvertTime(first, state.Zone).ToString("dd/MM", CultureInfo.InvariantCulture)
            : null;

        var state2 = standing switch
        {
            TabStanding.Frozen => new NoticeLine(Tone.Critical, text.TabFrozen, text.TabFrozenDetail),
            TabStanding.Overdue => new NoticeLine(Tone.Warning, text.TabOverdue, text.TabOverdueDetail(days ?? 0, tab.OverdueDays ?? 0)),
            TabStanding.NoTab => new NoticeLine(Tone.Neutral, text.NoTab, text.NoTabDetail),
            _ => null,
        };

        // The statement, oldest first, each with the balance after it: the ledger's own sum (D-055).
        var rows = new List<StatementRow>();
        var running = Domain.Values.Money.Zero(Currency.FromCode(currency));
        foreach (var movement in tab.Movements ?? [])
        {
            if (Money(movement.Amount, currency) is not { } amount)
            {
                continue;
            }

            running += amount;
            rows.Add(new StatementRow(
                TimeZoneInfo.ConvertTime(movement.At, state.Zone).ToString("dd/MM", CultureInfo.InvariantCulture),
                text.MovementLabel(movement.Kind, amount.IsNegative),
                (amount.IsPositive ? "+" : string.Empty) + DisplayFigures.Amount(amount),
                DisplayFigures.Amount(running)));
        }

        var balance = Money(tab.Balance, currency);
        return new CarnetView(
            text.CarnetLabel,
            text.CarnetTitle(tab.Customer?.Name ?? string.Empty),
            text.CarnetSubtitle,
            state2,
            [
                new CarnetFigure(text.BalanceDue, Shown(tab.Balance, currency, text, "?"), null),
                new CarnetFigure(text.Limit, Shown(tab.Limit, currency, text, "—"), null),
                new CarnetFigure(text.Available, Shown(tab.Available, currency, text, "—"), null),
                new CarnetFigure(
                    text.OldestUnpaid,
                    days is { } d ? (tab.OverdueDays is { } rule ? text.DaysOf(d, rule) : text.Days(d)) : "—",
                    since),
            ],
            text.StatementTitle,
            [text.ColumnDate, text.ColumnMovement, text.ColumnAmount, text.ColumnBalance],
            rows,
            rows.Count == 0 ? text.NoMovement : null,
            null,
            footer,
            new FormKey(ChangeKey, tab.Limit is null ? text.OpenTabKey : text.ChangeTabKey, true, true),
            text.RepayKey,
            balance is { IsPositive: true },
            text.EnterKey,
            text.EscapeKey);
    }

    // ======================================================== keys typed

    /// <summary>A key typed into the panel open: a digit to the number or the amount, any letter to the name while it has the keys.</summary>
    public static CustomerPanelState Press(CustomerPanelState panel, char key)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (panel.Sending)
        {
            return panel;
        }

        if (panel.Kind == CustomerPanelKind.Clock)
        {
            return key is >= '0' and <= '9' && panel.Pin.Length < 12 ? panel with { Pin = panel.Pin + key, Refused = null } : panel;
        }

        if (panel.Kind is CustomerPanelKind.DrawerConfirm or CustomerPanelKind.DrawerNote or CustomerPanelKind.DrawerClosed)
        {
            // The note, where the panel has one; the count is no longer typed here.
            return panel.OnName && panel.Name.Length < 200 && !char.IsControl(key) ? panel with { Name = panel.Name + key, Refused = null } : panel;
        }

        if (panel.Kind == CustomerPanelKind.PettyCash && panel.OnName)
        {
            return panel.Name.Length >= 200 || char.IsControl(key) ? panel : panel with { Name = panel.Name + key, Refused = null };
        }

        if (panel.Kind == CustomerPanelKind.Create && panel.OnName)
        {
            return panel.Name.Length >= 100 || char.IsControl(key) ? panel : panel with { Name = panel.Name + key, Refused = null };
        }

        if (panel.Kind == CustomerPanelKind.Search)
        {
            // A name or a number (D-100); a new one is a new question: the answer to the old one goes.
            return (char.IsLetterOrDigit(key) || key is ' ' or '-' or '\'') && panel.Phone.Length < 60
                ? panel with { Phone = panel.Phone + key, Found = null, Chosen = null, Refused = null, Checked = false }
                : panel;
        }

        if (panel.Kind == CustomerPanelKind.Create)
        {
            // A new number is a new question: the answer to the old one goes.
            return key is >= '0' and <= '9' && panel.Phone.Length < 10
                ? panel with { Phone = panel.Phone + key, Found = panel.Kind == CustomerPanelKind.Search ? null : panel.Found, Chosen = null, Refused = null }
                : panel;
        }

        if (panel.Kind is not (CustomerPanelKind.ChangeTab or CustomerPanelKind.Repay or CustomerPanelKind.PettyCash
            or CustomerPanelKind.DrawerOpen or CustomerPanelKind.DrawerCount))
        {
            return panel;
        }

        var mark = key is ',' or '.';
        if (!(key is >= '0' and <= '9' || mark) || panel.Typed.Length >= MaxTyped || (mark && panel.Typed.Contains(',', StringComparison.Ordinal)))
        {
            return panel;
        }

        var typed = panel.Typed + (mark ? ',' : key);
        return panel with { Typed = typed == "," ? "0," : typed, Refused = null };
    }

    /// <summary>⌫ in the field that has the keys.</summary>
    public static CustomerPanelState Backspace(CustomerPanelState panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        return panel.Kind switch
        {
            CustomerPanelKind.Clock => panel with { Pin = panel.Pin.Length > 0 ? panel.Pin[..^1] : string.Empty, Refused = null },
            CustomerPanelKind.PettyCash when panel.OnName => panel with { Name = panel.Name.Length > 0 ? panel.Name[..^1] : string.Empty, Refused = null },
            CustomerPanelKind.Create when panel.OnName => panel with { Name = panel.Name.Length > 0 ? panel.Name[..^1] : string.Empty, Refused = null },
            CustomerPanelKind.DrawerConfirm or CustomerPanelKind.DrawerNote or CustomerPanelKind.DrawerClosed => panel.OnName
                ? panel with { Name = panel.Name.Length > 0 ? panel.Name[..^1] : string.Empty, Refused = null }
                : panel,
            CustomerPanelKind.Search or CustomerPanelKind.Create => panel with
            {
                Phone = panel.Phone.Length > 0 ? panel.Phone[..^1] : string.Empty,
                Found = panel.Kind == CustomerPanelKind.Search ? null : panel.Found,
                Chosen = null,
                Refused = null,
            },
            _ => panel with { Typed = panel.Typed.Length > 0 ? panel.Typed[..^1] : string.Empty, Refused = null },
        };
    }

    /// <summary>"Effacer": the field that has the keys emptied.</summary>
    public static CustomerPanelState Clear(CustomerPanelState panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        return panel.Kind switch
        {
            CustomerPanelKind.Clock => panel with { Pin = string.Empty, Refused = null },
            CustomerPanelKind.PettyCash when panel.OnName => panel with { Name = string.Empty, Refused = null },
            CustomerPanelKind.Create when panel.OnName => panel with { Name = string.Empty, Refused = null },
            CustomerPanelKind.DrawerConfirm or CustomerPanelKind.DrawerNote or CustomerPanelKind.DrawerClosed => panel.OnName
                ? panel with { Name = string.Empty, Refused = null }
                : panel,
            CustomerPanelKind.Search or CustomerPanelKind.Create => panel with { Phone = string.Empty, Found = panel.Kind == CustomerPanelKind.Search ? null : panel.Found, Chosen = null, Refused = null },
            _ => panel with { Typed = string.Empty, Refused = null },
        };
    }

    /// <summary>What a creation has to correct before it is sent: nothing, or the fields marked.</summary>
    public static bool NeedsCorrecting(this CustomerPanelState panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        return panel.Kind == CustomerPanelKind.Create && panel.Refused is null && panel.Checked
            && (panel.Name.Trim().Length is 0 or > 100 || !PhoneNumber.TryNormalise(panel.Phone, out _));
    }

    /// <summary>"0550 12 34 56": a number in the way it is read out, from the digits or the stored "+213" form.</summary>
    public static string Phone(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        if (digits.Contains('•', StringComparison.Ordinal))
        {
            return digits;
        }

        var local = digits.StartsWith("+213", StringComparison.Ordinal) ? "0" + digits[4..] : digits;
        if (local.Length <= 4)
        {
            return local;
        }

        var groups = new List<string> { local[..4] };
        for (var i = 4; i < local.Length; i += 2)
        {
            groups.Add(local[i..Math.Min(i + 2, local.Length)]);
        }

        return string.Join(' ', groups);
    }

    /// <summary>What was typed in the search is a number: digits only.</summary>
    public static bool IsNumber(string typed) => typed.Length > 0 && typed.All(char.IsAsciiDigit);

    /// <summary>Whether the search may be asked: the 10 digits of a number, or a full name (D-100).</summary>
    public static bool Searchable(string typed) =>
        IsNumber(typed) ? PhoneNumber.TryNormalise(typed, out _) : CustomerNameSearch.IsFullName(typed);

    /// <summary>The amount typed, with the display's comma.</summary>
    private static string Typed(string typed) => typed;

    private static int Count(params bool[] wrong) => wrong.Count(w => w);

    private static string Short(TabAnswer? tab) =>
        tab?.Customer is { } customer ? new AttachedCustomer(customer.CustomerId, customer.Name, customer.Phone).ShortName : string.Empty;

    private static string Shown(string? amount, string currency, TillText text, string none) =>
        Money(amount, currency) is { } money ? DisplayFigures.AmountWithCurrency(money, text) : none;

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
}
