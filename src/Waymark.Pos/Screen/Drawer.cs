using System.Globalization;
using Waymark.Contracts.Pos;
using Waymark.Domain.Values;
using Waymark.Pos.Checkout;

namespace Waymark.Pos.Screen;

/// <summary>
/// The drawer's panels (C1, D-111), apart from the window so they are tested without one: the float
/// counted at opening, then the close in three steps, count, confirm, result. What the drawer should
/// hold is the server's and is shown only when the server sent it: a blind count never sees it, nor
/// the variance, nor the threshold. Where the till works a variance out, it asks
/// <see cref="Domain.Organisation.Drawer"/>, as the server does.
/// </summary>
public static class DrawerScreen
{
    public const string SwitchUserKey = "switch_user";

    public const string RecountKey = "recount";

    public const string ZReportKey = "z_report";

    public static FormPanel Panel(ScreenState state, CustomerPanelState panel)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(panel);
        return panel.Kind switch
        {
            CustomerPanelKind.DrawerOpen => OpenPanel(state, panel),
            CustomerPanelKind.DrawerCount => CountPanel(state, panel),
            CustomerPanelKind.DrawerConfirm => ConfirmPanel(state, panel),
            CustomerPanelKind.DrawerNote => NotePanel(state, panel),
            _ => ClosedPanel(state, panel),
        };
    }

    /// <summary>The amount typed, zero included: an empty drawer is a count. Null when it does not read.</summary>
    public static long? TypedHundredths(CustomerPanelState panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (DiscountEntry.TryParse(panel.Typed, DiscountForms.Amount, out var hundredths))
        {
            return hundredths;
        }

        // An amount's entry refuses nothing at all, rightly for a discount; a drawer may hold nothing.
        var digits = panel.Typed.Replace(",", string.Empty, StringComparison.Ordinal);
        return digits.Length is > 0 and <= 3 && digits.All(c => c == '0') && !panel.Typed.EndsWith(',') ? 0 : null;
    }

    /// <summary>Whether the server sent what the drawer should hold: it does not to someone counting blind.</summary>
    public static bool ShowsFigures(CustomerPanelState panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        return panel.Drawer?.Open?.Drawer is not null;
    }

    /// <summary>
    /// The count against what the drawer should hold, and whether a note is asked: null when the figures
    /// are not shown or the count does not read.
    /// </summary>
    public static (Money Variance, bool NoteNeeded, Money? Threshold)? Reading(CustomerPanelState panel, string currency)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (panel.Drawer?.Open?.Drawer is not { } drawer || TypedHundredths(panel) is not { } hundredths || Money(drawer.Expected, currency) is not { } expected)
        {
            return null;
        }

        var threshold = Money(panel.Drawer.NoteThreshold, currency);
        var variance = Domain.Organisation.Drawer.Variance(expected, Domain.Values.Money.FromMinorUnits(hundredths, expected.Currency));
        return (variance, Domain.Organisation.Drawer.NeedsNote(variance, threshold), threshold);
    }

    /// <summary>Whether the confirm step may go on: a note where one is asked, and nothing on its way.</summary>
    public static bool MayConfirm(CustomerPanelState panel, string currency)
    {
        ArgumentNullException.ThrowIfNull(panel);
        return !panel.Sending && TypedHundredths(panel) is not null
            && (Reading(panel, currency) is not { NoteNeeded: true } || panel.Name.Trim().Length > 0);
    }

    // ================================================================== opening

    private static FormPanel OpenPanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var context = state.Context;
        var currency = Currency.FromCode(context?.Currency ?? "DZD");
        var now = TimeZoneInfo.ConvertTime(state.Now, state.Zone);
        var role = text.RightToLeft ? context?.RoleLabelAr : context?.RoleLabelFr;

        // What the Z will print: the till, who opens it, and when.
        var facts = new List<string>
        {
            $"{text.DrawerTillTile} · {context?.TerminalName} · {context?.StoreName}",
            $"{text.DrawerOpenedByTile} · {context?.StaffName}{(string.IsNullOrEmpty(role) ? string.Empty : $" · {role}")}",
            $"{text.DrawerDateTile} · {now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} · {DisplayFigures.Clock(now)}",
        };
        if (panel.Drawer?.LastClose is { } last)
        {
            var closed = TimeZoneInfo.ConvertTime(last.ClosedAt, state.Zone);
            facts.Add(text.LastClose(last.ZReportNumber, closed.ToString("dd/MM", CultureInfo.InvariantCulture), DisplayFigures.Clock(closed), last.ClosedBy ?? "?"));
        }

        var typed = TypedHundredths(panel);
        return new FormPanel(
            text.DrawerOpenLabel,
            text.DrawerOpenTitle,
            text.DrawerOpenSubtitle,
            [],
            [new FormField(CustomerScreen.AmountField, text.FloatTitle, panel.Typed, null, true, panel.Typed.Length > 0 && typed is null, text.CurrencySymbol(currency))],
            text.FloatHelp,
            string.Join('\n', facts),
            panel.Refused is { } refused ? new PanelMessage(text.ClientRefused, refused)
                // A scan while the drawer is shut: said here, where the cashier is looking (the board, 1B).
                : panel.Checked ? new PanelMessage(text.NoSaleWithoutDrawer, text.NoSaleWithoutDrawerDetail)
                : null,
            null,
            [],
            null,
            [new FormKey(SwitchUserKey, text.SwitchUserKey, !panel.Sending, false)],
            null,
            null,
            [],
            true,
            text.DrawerOpenTitle,
            typed is not null && !panel.Sending,
            false,
            text.EnterKey,
            text.EscapeKey,
            null)
        {
            NoClose = true,
            NoticeFirst = true,
        };
    }

    // ================================================================== closing, 1 of 3

    private static FormPanel CountPanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var code = state.Context?.Currency ?? "DZD";
        var currency = Currency.FromCode(code);
        var open = panel.Drawer?.Open;
        var drawer = open?.Drawer;
        var typed = TypedHundredths(panel);

        var lines = new List<Figure>();
        if (drawer is not null)
        {
            lines.Add(new Figure(text.DrawerLineFloat, Shown(drawer.OpeningFloat, code)));
            lines.Add(new Figure(text.DrawerLineSales, Shown(drawer.CashSales, code)));
            lines.Add(new Figure(text.DrawerLineRefunds, Shown(drawer.CashRefunds, code)));
            lines.Add(new Figure(text.DrawerLinePaidIn, Shown(drawer.PaidIn, code)));
            lines.Add(new Figure(text.DrawerLinePaidOut, Shown(drawer.PaidOut, code)));
            lines.Add(new Figure(text.DrawerLineTab, Shown(drawer.TabRepayments, code)));

            // The till takes no cash to the safe yet: the line is there only when a row is.
            if (Money(drawer.Drops, code) is { IsZero: false })
            {
                lines.Add(new Figure(text.DrawerLineDrops, Shown(drawer.Drops, code)));
            }
        }

        return new FormPanel(
            text.CloseStep(1),
            text.CloseDrawerTitle,
            Opened(state, panel),
            open is null ? [] : [new Figure(text.OpeningFloatTile, ShownWithCurrency(open.OpeningFloat, code, text)), new Figure(text.TicketsTile, DisplayFigures.Count(open.Tickets))],
            [new FormField(CustomerScreen.AmountField, text.CountedTitle, panel.Typed, null, true, panel.Typed.Length > 0 && typed is null, text.CurrencySymbol(currency))],
            drawer is null ? text.CountHelpBlind : text.CountHelpShown,
            null,
            panel.Refused is { } refused ? new PanelMessage(text.ClientRefused, refused)
                : panel.Checked ? new PanelMessage(text.ClientRefused, text.SettleTicketsFirst)
                : null,
            null,
            [],
            null,
            [],
            drawer is null ? null : text.ExpectedInDrawer,
            drawer is null ? null : ShownWithCurrency(drawer.Expected, code, text),
            lines,
            true,
            text.ContinueKey,
            typed is not null && !panel.Sending && !panel.Checked && open is not null,
            false,
            text.EnterKey,
            text.EscapeKey,
            drawer is null ? text.BlindCloseFooter : text.ShownCloseFooter);
    }

    // ================================================================== closing, 2 of 3

    private static FormPanel ConfirmPanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var code = state.Context?.Currency ?? "DZD";
        var counted = Domain.Values.Money.FromMinorUnits(TypedHundredths(panel) ?? 0, Currency.FromCode(code));
        var mayClose = panel.Drawer?.MayClose ?? false;
        var refused = panel.Refused is { } why ? new PanelMessage(text.ClientRefused, why) : null;

        if (Reading(panel, code) is not { } reading)
        {
            // Blind: the count is confirmed before anything is said of it, and is not typed again.
            return new FormPanel(
                text.CloseStep(2),
                text.ConfirmCountTitle,
                $"{text.BlindCloseFooter} · {state.Context?.TerminalName}",
                [], [], null,
                panel.Frozen ? null : text.CountBecomesFinal,
                refused,
                null, [], null,
                panel.Frozen ? [] : [new FormKey(RecountKey, text.RecountKey, !panel.Sending, false)],
                panel.Frozen ? text.CountConfirmed : text.YouCounted,
                DisplayFigures.AmountWithCurrency(counted, text),
                [],
                false,
                text.ConfirmCountTitle,
                !panel.Sending,
                !mayClose,
                text.EnterKey,
                text.EscapeKey,
                mayClose ? null : text.ManagerPinFooter)
            {
                BigFirst = true,
            };
        }

        var (message, figure, notice) = VarianceBlock(text, reading.Variance, reading.NoteNeeded, reading.Threshold, attached: false);
        return new FormPanel(
            text.CloseStep(2),
            text.ConfirmCloseTitle,
            $"{text.ShownCloseFooter} · {state.Context?.TerminalName}",
            [
                new Figure(text.ExpectedTile, ShownWithCurrency(panel.Drawer?.Open?.Drawer?.Expected, code, text)),
                new Figure(text.CountedTile, DisplayFigures.AmountWithCurrency(counted, text)),
            ],
            [
                new FormField(
                    CustomerScreen.NameField, reading.NoteNeeded ? text.NoteRequiredTitle : text.NoteOptionalTitle, panel.Name, text.NotePlaceholder, true,
                    false),
            ],
            null,
            notice,
            refused ?? message,
            null, [], null,
            [new FormKey(RecountKey, text.RecountKey, !panel.Sending, false)],
            text.YouCounted,
            DisplayFigures.AmountWithCurrency(counted, text),
            [],
            false,
            text.ConfirmCloseTitle,
            MayConfirm(panel, code),
            !mayClose,
            text.EnterKey,
            text.EscapeKey,
            mayClose ? null : text.ManagerPinFooter)
        {
            BigFirst = true,
            MessageFigure = refused is null ? figure : null,
        };
    }

    /// <summary>A blind count the server found past the threshold: its note, and nothing of how far.</summary>
    private static FormPanel NotePanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var code = state.Context?.Currency ?? "DZD";
        var counted = Domain.Values.Money.FromMinorUnits(TypedHundredths(panel) ?? 0, Currency.FromCode(code));
        var mayClose = panel.Drawer?.MayClose ?? false;
        return new FormPanel(
            text.CloseStep(2),
            text.ExplainCountTitle,
            text.ExplainCountSubtitle,
            [],
            [new FormField(CustomerScreen.NameField, text.NoteForManager, panel.Name, null, true, false)],
            null,
            null,
            panel.Refused is { } refused ? new PanelMessage(text.ClientRefused, refused) : new PanelMessage(text.NoteAsked, text.NoteAskedDetail, Tone.Warning),
            null, [], null, [],
            text.CountConfirmed,
            DisplayFigures.AmountWithCurrency(counted, text),
            [],
            false,
            text.ContinueKey,
            panel.Name.Trim().Length > 0 && !panel.Sending,
            !mayClose,
            text.EnterKey,
            text.EscapeKey,
            mayClose ? null : text.PinNextFooter)
        {
            BigFirst = true,
        };
    }

    // ================================================================== closing, 3 of 3

    private static FormPanel ClosedPanel(ScreenState state, CustomerPanelState panel)
    {
        var text = state.Text;
        var code = state.Context?.Currency ?? "DZD";
        var closed = panel.Closed;
        var at = closed is null ? string.Empty : DisplayFigures.Clock(TimeZoneInfo.ConvertTime(closed.ClosedAt, state.Zone));
        var subtitle = text.ClosedSubtitle(state.Context?.TerminalName ?? string.Empty, at, closed?.ValidatedBy ?? "?");
        var number = closed?.ZReportNumber.ToString(CultureInfo.InvariantCulture) ?? "?";

        // To who counted blind: the number, and where the report is. No figure, no colour.
        if (closed is null || Money(closed.Variance, code) is not { } variance)
        {
            return new FormPanel(
                text.CloseStep(3), text.DrawerClosedTitle, subtitle, [], [], text.ZWithManager, null, null, null, [], null, [],
                text.ZNumberLabel, number, [], false, text.Finish, !panel.Sending, false, text.EnterKey, text.EscapeKey, text.FinishReturnsToSignIn)
            {
                BigFirst = true,
                NoClose = true,
            };
        }

        var threshold = Money(panel.Drawer?.NoteThreshold, code);
        var (message, figure, notice) = VarianceBlock(text, variance, closed.PastThreshold ?? false, threshold, attached: true);
        return new FormPanel(
            text.CloseStep(3),
            text.DrawerClosedTitle,
            subtitle,
            [
                new Figure(text.ExpectedTile, ShownWithCurrency(closed.Expected, code, text)),
                new Figure(text.CountedTile, ShownWithCurrency(closed.Counted, code, text)),
            ],
            [], null, notice, message, null, [], null,
            // The Z report is C2's: its key is here, and says it is not available yet.
            [new FormKey(ZReportKey, text.SeeZReport, false, false)],
            text.ZNumberLabel,
            number,
            [],
            false,
            text.Finish,
            !panel.Sending,
            false,
            text.EnterKey,
            text.EscapeKey,
            text.FinishReturnsToSignIn)
        {
            BigFirst = true,
            NoClose = true,
            MessageFigure = figure,
        };
    }

    // ================================================================== the variance, by its label

    /// <summary>
    /// A variance as it is said (CLAUDE.md §6): a shortage past the threshold is critical, any other
    /// shortage or excess a warning, each with its label; a count that matches has no colour, since
    /// there is no positive state.
    /// </summary>
    /// <param name="attached">After the close: "note jointe". Before it, the threshold as what is asked.</param>
    public static (PanelMessage? Message, string? Figure, string? Notice) VarianceBlock(TillText text, Money variance, bool past, Money? threshold, bool attached)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (variance.IsZero)
        {
            return (null, null, $"{text.NoVariance} · {DisplayFigures.AmountWithCurrency(variance, text)} · {text.NoVarianceDetail}");
        }

        var limit = threshold is { } most ? DisplayFigures.AmountWithCurrency(most, text) : null;
        var figure = (variance.IsPositive ? "+" : string.Empty) + DisplayFigures.AmountWithCurrency(variance, text);
        var beyond = limit is null ? null : attached ? text.NoteAttached(limit) : text.NoteThresholdIs(limit);
        if (variance.IsNegative)
        {
            return past
                ? (new PanelMessage(text.ShortPastThreshold, beyond ?? text.LessCashThanExpected), figure, null)
                : (new PanelMessage(text.ShortWithin, limit is null ? text.LessCashThanExpected : text.WithinThreshold(limit), Tone.Warning), figure, null);
        }

        return past
            ? (new PanelMessage(text.OverPastThreshold, beyond ?? text.MoreCashThanExpected, Tone.Warning), figure, null)
            : (new PanelMessage(text.OverCount, text.MoreCashThanExpected, Tone.Warning), figure, null);
    }

    private static string Opened(ScreenState state, CustomerPanelState panel) =>
        panel.Drawer?.Open is { } open
            ? state.Text.SessionOpened(
                state.Context?.TerminalName ?? string.Empty, DisplayFigures.Clock(TimeZoneInfo.ConvertTime(open.OpenedAt, state.Zone)), open.OpenedBy ?? "?")
            : state.Context?.TerminalName ?? string.Empty;

    private static string Shown(string? amount, string currency) =>
        Money(amount, currency) is { } money ? DisplayFigures.Amount(money) : "?";

    private static string ShownWithCurrency(string? amount, string currency, TillText text) =>
        Money(amount, currency) is { } money ? DisplayFigures.AmountWithCurrency(money, text) : "?";

    private static Money? Money(string? text, string currency)
    {
        if (text is null)
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
