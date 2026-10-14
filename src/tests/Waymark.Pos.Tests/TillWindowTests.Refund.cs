using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Waymark.Contracts.Pos;
using Waymark.Domain.Enums;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// B9 (D-098): a refund at the till, driven through the window. The silent failures: a refund paid
/// out that the server never wrote; a line sent as more than the cashier touched, or back on the shelf
/// when the cashier said not; a manager's authorisation never asked when the shop asks for one; store
/// credit offered on a ticket that cannot take it.
/// </summary>
public sealed partial class TillWindowTests
{
    /// <summary>The past ticket open, then "Rembourser".</summary>
    private static void OpenRefund(Till till)
    {
        till.TypeSlowly("S-2026-000142");
        till.Key(Key.Enter, PhysicalKey.Enter);
        Assert.Equal("t-142", till.Session.Viewing?.TransactionId);
        till.Tap(Operation(till, Screen.Operation.Refund), new Point(20, 20));
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is ReasonRow { Code: "retour_defectueux" }));
    }

    /// <summary>The milk touched (one unit), the reason, Continuer: the server's quote floats over the ticket.</summary>
    private static PaymentPanel Quote(Till till)
    {
        till.Tap(till.Rows()[0], new Point(200, 10));
        till.Tap(Keyed<ReasonRow>(till, row => row.Code == "retour_defectueux"), new Point(20, 20));
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => RefundPanel(till) is not null);
        return RefundPanel(till)!;
    }

    private static PaymentPanel? RefundPanel(Till till) =>
        till.Window.GetVisualDescendants().OfType<Border>().Select(border => border.Tag).OfType<PaymentPanel>()
            .SingleOrDefault(panel => panel.Title == TillText.French.RefundTitle);

    [Fact]
    public Task A_line_touched_a_reason_and_rembourser_refund_it_in_cash_and_the_refund_ticket_opens() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        OpenRefund(till);

        var panel = Quote(till);

        // The server's figures, never the till's: 143,00 back, 145,00 out of the drawer.
        Assert.Equal(DisplayFigures.AmountWithCurrency(Waymark.Domain.Values.Money.FromMinorUnits(14_500, Waymark.Domain.Values.Currency.Dzd), TillText.French), panel.Due);
        Assert.DoesNotContain(panel.Methods, method => method.Method == PaymentMethod.StoreCredit); // no customer on it
        var quote = till.Server.Refunds.Single();
        Assert.True(quote.Quote);

        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Session.Viewing?.TransactionId == "r-143");

        var sent = till.Server.Refunds[^1];
        Assert.False(sent.Quote);
        Assert.Equal(("t-142", "retour_defectueux", RefundDestinations.Cash, (string?)null), (sent.Original, sent.ReasonCode, sent.RefundTo, sent.Authorisation));
        Assert.Equal(new RefundLineRequest("v-milk", "143.00", "1", true), Assert.Single(sent.Lines));
        Assert.Null(RefundPanel(till));
    });

    [Fact]
    public Task Plus_and_en_rayon_on_the_line_touched_are_what_is_sent() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        OpenRefund(till);

        till.Tap(till.Rows()[0], new Point(200, 10));
        till.Tap(Keyed<DiscountFormChoice>(till, key => key.Form == RefundScreen.MoreKey), new Point(10, 10));
        till.Tap(Keyed<DiscountFormChoice>(till, key => key.Form == RefundScreen.MoreKey), new Point(10, 10)); // never above what is left
        till.Tap(Keyed<DiscountFormChoice>(till, key => key.Form == RefundScreen.RestockKey), new Point(10, 10));
        till.Tap(Keyed<ReasonRow>(till, row => row.Code == "retour_defectueux"), new Point(20, 20));
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => RefundPanel(till) is not null);

        Assert.Equal(new RefundLineRequest("v-milk", "143.00", "2", false), Assert.Single(till.Server.Refunds[^1].Lines));
        Assert.Contains(((LineRow)till.Rows()[0].Tag!).Chips, chip => chip.Label == TillText.French.NoRestockChip);
    });

    [Fact]
    public Task Continuer_with_no_line_touched_asks_for_one_and_asks_the_server_nothing() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        OpenRefund(till);

        till.Tap(Keyed<ReasonRow>(till, row => row.Code == "retour_defectueux"), new Point(20, 20));
        till.Key(Key.Enter, PhysicalKey.Enter);

        Assert.Empty(till.Server.Refunds);
        Assert.Contains(till.Window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == TillText.French.ChooseLines);
    });

    [Fact]
    public Task When_the_shop_asks_for_a_manager_the_pin_step_opens_then_the_refund_goes_with_it() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        till.Server.RefundNeedsManager = true;
        OpenRefund(till);
        Quote(till);

        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is ApproverRow));
        Assert.Equal(TillText.French.RefundLabel, till.Window.GetVisualDescendants().OfType<Border>().Select(border => border.Tag).OfType<Screen.Approval>().Single().Title);
        Assert.Equal("t-142", till.Session.Viewing?.TransactionId); // nothing refunded yet

        till.Tap(Keyed<ApproverRow>(till, row => row.StaffId == "samia"), new Point(20, 20));
        till.Window.KeyTextInput("1357");
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is Screen.Approval { PinLength: 4 }));
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Session.Viewing?.TransactionId == "r-143");

        Assert.Equal("auth-samia", till.Server.Refunds[^1].Authorisation);
    });

    [Fact]
    public Task Past_the_shops_window_the_owners_pin_is_asked_and_the_panel_says_why() => Headless.Run(() =>
    {
        // D-125: one owner's PIN, asked for the window's capability, stands for the refund's too.
        using var till = Till.SignedIn();
        till.Server.RefundPastWindow = true;
        OpenRefund(till);
        Quote(till);

        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is ApproverRow));
        Assert.Equal(Capabilities.RefundPastWindow, till.Server.Approvers[^1]);
        var step = till.Window.GetVisualDescendants().OfType<Border>().Select(border => border.Tag).OfType<Screen.Approval>().Single();
        Assert.Equal((TillText.French.OwnerApproval, TillText.French.OwnerPin), (step.Label, step.PinTitle));
        Assert.EndsWith("Vendu il y a plus de 7 jours : le propriétaire rembourse.", step.Summary, StringComparison.Ordinal);
        Assert.Equal("t-142", till.Session.Viewing?.TransactionId); // nothing refunded yet

        till.Tap(Keyed<ApproverRow>(till, row => row.StaffId == "samia"), new Point(20, 20));
        till.Window.KeyTextInput("1357");
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is Screen.Approval { PinLength: 4 }));
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Session.Viewing?.TransactionId == "r-143");

        Assert.Equal(Capabilities.RefundPastWindow, till.Server.Authorisations[^1].Capability);
        Assert.Equal("auth-samia", till.Server.Refunds[^1].Authorisation);
    });

    [Fact]
    public Task A_past_ticket_with_no_tva_shows_no_total_ht_and_no_tva_line_and_a_taxed_one_shows_both() => Headless.Run(() =>
    {
        // D-122: a ticket with no TVA in it says nothing of TVA, on the rail and on the bottom bar.
        bool Says(Till till, string label) => till.Window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == label);

        using (var plain = Till.SignedIn(server => server.Served = FakeStoreServer.Ticket with { Subtotal = "286.00", TaxTotal = "0.00" }))
        {
            plain.TypeSlowly("S-2026-000142");
            plain.Key(Key.Enter, PhysicalKey.Enter);
            plain.WaitFor(() => plain.Session.Viewing?.TransactionId == "t-142");

            Assert.False(Says(plain, TillText.French.TotalBeforeTax));
            Assert.False(Says(plain, TillText.French.TaxIncluded));
            Assert.True(Says(plain, TillText.French.TicketTotal));
        }

        using var taxed = Till.SignedIn();
        taxed.TypeSlowly("S-2026-000142");
        taxed.Key(Key.Enter, PhysicalKey.Enter);
        taxed.WaitFor(() => taxed.Session.Viewing?.TransactionId == "t-142");

        Assert.True(Says(taxed, TillText.French.TotalBeforeTax));
        Assert.True(Says(taxed, TillText.French.TaxIncluded));
    });

    [Fact]
    public Task With_no_answer_nothing_is_refunded_and_the_panel_says_so() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        OpenRefund(till);
        Quote(till);
        till.Server.RefundsAnswer = false;

        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => RefundPanel(till)?.Message is not null);

        Assert.Equal(TillText.French.RefundOffline, RefundPanel(till)!.Message!.Body);
        Assert.Equal("t-142", till.Session.Viewing?.TransactionId);
    });

    [Fact]
    public Task Store_credit_on_a_ticket_with_no_customer_attaches_one_first_then_shows_the_credit() => Headless.Run(() =>
    {
        using var till = Till.SignedIn(server =>
        {
            server.MayCredit = true;
            server.CustomerModule = true;
        });
        OpenRefund(till);
        Quote(till);

        // Avoir chosen, nobody on the ticket: the panel says it is nominative, and Rembourser waits.
        till.Tap(Keyed<MethodChoice>(till, choice => choice.Method == PaymentMethod.StoreCredit), new Point(20, 20));
        var panel = RefundPanel(till)!;
        Assert.Equal((TillText.French.CreditOut, TillText.French.CreditIsNamed, false), (panel.DueLabel, panel.Message!.Title, panel.MayPrimary));
        till.Key(Key.Enter, PhysicalKey.Enter); // pressed anyway: nothing is sent for nobody
        Assert.All(till.Server.Refunds, sent => Assert.True(sent.Quote));

        till.Tap(Keyed<PanelAction>(till, _ => true), new Point(20, 20));
        Keys(till, "0550123456");
        till.Key(Key.Enter, PhysicalKey.Enter); // Chercher
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is FormRow { Id: "c-samira" }));
        till.Tap(Keyed<FormRow>(till, row => row.Id == "c-samira"), new Point(20, 10));
        till.Key(Key.Enter, PhysicalKey.Enter); // Rattacher au retour: quoted again, for her
        till.WaitFor(() => RefundPanel(till) is { MayPrimary: true });
        Assert.Equal("c-samira", till.Server.Refunds[^1].CustomerId);

        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Session.Viewing?.TransactionId == "r-143");

        Assert.Equal((RefundDestinations.StoreCredit, "c-samira"), (till.Server.Refunds[^1].RefundTo, till.Server.Refunds[^1].CustomerId));
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.CreditIssuedTitle);
    });

    [Fact]
    public Task Echap_closes_the_panel_then_the_refund_and_the_ticket_stays_read_only() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        OpenRefund(till);
        Quote(till);

        till.Key(Key.Escape, PhysicalKey.Escape);
        Assert.Null(RefundPanel(till));
        Assert.Contains(till.Window.GetVisualDescendants().OfType<Control>(), control => control.Tag is ReasonRow);

        till.Key(Key.Escape, PhysicalKey.Escape);
        Assert.DoesNotContain(till.Window.GetVisualDescendants().OfType<Control>(), control => control.Tag is ReasonRow);
        Assert.Equal("t-142", till.Session.Viewing?.TransactionId);
        Assert.True(Assert.Single(till.Server.Refunds).Quote); // the quote only: nothing refunded
    });

    private sealed partial class FakeStoreServer
    {
        /// <summary>The refund written: S-2026-000143, one milk back from S-2026-000142 (B9).</summary>
        public static readonly PastTicketDetail RefundTicket = new(
            "r-143", "S-2026-000143", new DateTimeOffset(2026, 9, 25, 10, 5, 0, TimeSpan.Zero), "till-1", "Nabil B.", "completed",
            [new PastTicketLineWire("Lait UHT Candia", "Brique 1L", "-1", "pc", "143.00", "-143.00", "v-milk", "0")],
            "-131.19", "-11.81", "-143.00", "DZD", [new PastPaymentWire("cash", "-143.00")], OriginalInvoiceNumber: "S-2026-000142");

        /// <summary>Every refund sent, quotes included, as sent.</summary>
        public List<RefundRequest> Refunds { get; } = [];

        /// <summary>False: the server gives no answer to a refund.</summary>
        public bool RefundsAnswer { get; set; } = true;

        /// <summary>The shop's refund_min_rank is above the cashier's: a refund needs an authorisation.</summary>
        public bool RefundNeedsManager { get; set; }

        /// <summary>Every capability the PIN step listed approvers for, in order.</summary>
        public List<string> Approvers { get; } = [];

        public Task<TillStaff?> ApproversAsync(string capability, CancellationToken cancellationToken = default)
        {
            Approvers.Add(capability);
            return StaffAsync(cancellationToken);
        }

        /// <summary>The sale is past the shop's refund window and the seller is not the owner (D-125).</summary>
        public bool RefundPastWindow { get; set; }

        /// <summary>The ticket has its customer and the module is on.</summary>
        public bool MayCredit { get; set; }

        public Task<Waymark.Contracts.Reference.ReasonCodeList?> ReturnReasonsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Waymark.Contracts.Reference.ReasonCodeList?>(new Waymark.Contracts.Reference.ReasonCodeList("return",
                [new Waymark.Contracts.Reference.ReasonCodeOption("retour_defectueux", "منتج معيب", "Produit défectueux", false, false)]));

        /// <summary>As the server answers: a quote of 143,00 (145,00 out of the drawer), then the refund, or a manager first.</summary>
        public Task<RefundAnswer?> RefundAsync(RefundRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            Refunds.Add(request);
            return Task.FromResult<RefundAnswer?>(
                !RefundsAnswer ? null
                : request.Quote ? new RefundAnswer(RefundOutcomes.Quoted, null, null, "143.00", "0.00", "143.00", request.RefundTo, "145.00", null, MayCredit, "DZD",
                    CustomerOnTicket: request.CustomerId is not null)
                : RefundPastWindow && request.Authorisation is null ? new RefundAnswer(
                    RefundOutcomes.PinRequired, Reason: "Sold more than 7 days ago.", Refusal: new Refusal(RefusalCodes.RefundWindowPassed, ["7"]))
                : RefundNeedsManager && request.Authorisation is null ? new RefundAnswer(RefundOutcomes.PinRequired, Reason: "A manager authorises it.")
                : new RefundAnswer(RefundOutcomes.Refunded, "r-143", "S-2026-000143", "143.00", "0.00", "143.00", request.RefundTo,
                    request.RefundTo == RefundDestinations.StoreCredit ? null : "145.00", request.RefundTo == RefundDestinations.StoreCredit ? "143.00" : null,
                    MayCredit, "DZD", CustomerOnTicket: request.CustomerId is not null));
        }
    }
}
