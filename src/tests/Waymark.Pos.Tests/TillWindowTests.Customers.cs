using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Waymark.Contracts.Pos;
using Waymark.Domain.Enums;
using Waymark.Pos.Screen;
using Waymark.Pos.Ui;

namespace Waymark.Pos.Tests;

/// <summary>
/// B7 (D-096): the customers at the till, driven through the window. The silent failures: a customer
/// key with the module off; a ticket sold with its customer dropped; a customer created with nobody's
/// authorisation; a ticket put on the tab past its limit with no owner named, or on a frozen tab at all;
/// a repayment sent for some other amount than the one typed.
/// </summary>
public sealed partial class TillWindowTests
{
    private static FormPanel? FormOf(Till till) =>
        till.Window.GetVisualDescendants().OfType<Border>().Select(border => border.Tag).OfType<FormPanel>().SingleOrDefault();

    private static PaymentPanel? PaymentOf(Till till) =>
        till.Window.GetVisualDescendants().OfType<Border>().Select(border => border.Tag).OfType<PaymentPanel>().SingleOrDefault();

    /// <summary>
    /// Typed a key at a time, slower than a scanner's burst: eight characters at once are a scan (D-063),
    /// and a scan is ignored while a panel floats.
    /// </summary>
    private static void Keys(Till till, string text)
    {
        foreach (var c in text)
        {
            till.Window.KeyTextInput(c.ToString());
            Thread.Sleep(70);
            till.Pump();
        }
    }

    private static Till WithCustomers(Action<FakeStoreServer>? more = null) => Till.SignedIn(server =>
    {
        server.CustomerModule = true;
        more?.Invoke(server);
    });

    /// <summary>F5, the number, Chercher, the first found, Rattacher.</summary>
    private static void AttachSamira(Till till)
    {
        till.Key(Key.F5, PhysicalKey.F5);
        Keys(till, "0550123456");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is FormRow { Id: "c-samira" }));
        till.Tap(Keyed<FormRow>(till, row => row.Id == "c-samira"), new Point(20, 10));
        till.Key(Key.Enter, PhysicalKey.Enter);
        Assert.Equal("c-samira", till.Session.Cart.Customer?.CustomerId);
    }

    /// <summary>The PIN step: Samia, 1357, Entrée.</summary>
    private static void SamiaAuthorises(Till till)
    {
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is ApproverRow));
        till.Tap(Keyed<ApproverRow>(till, row => row.StaffId == "samia"), new Point(20, 20));
        till.Window.KeyTextInput("1357");
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is Screen.Approval { PinLength: 4 }));
        till.Key(Key.Enter, PhysicalKey.Enter);
    }

    [Fact]
    public Task With_the_module_off_there_is_no_customer_key_and_f5_does_nothing() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();

        till.Key(Key.F5, PhysicalKey.F5);

        Assert.DoesNotContain(till.Window.GetVisualDescendants().OfType<Control>(), control => Equals(control.Tag, TillViews.ClientKeyTag));
        Assert.Null(FormOf(till));
    });

    [Fact]
    public Task A_customer_found_by_number_is_attached_named_on_the_ticket_and_sent_with_the_sale() => Headless.Run(() =>
    {
        using var till = WithCustomers();
        till.ScanMany(1);

        AttachSamira(till);

        Assert.Equal(["0550123456"], till.Server.Searches.Where(search => search.StartsWith('0')));
        Assert.Contains(till.Window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Samira B.");
        Assert.Null(FormOf(till));

        till.Pay();
        Assert.Equal("c-samira", till.Server.Requests.Single().CustomerId);
        Assert.Null(till.Session.Cart.Customer); // the next ticket starts with nobody
    });

    [Fact]
    public Task A_full_name_lists_its_customers_only_once_the_typing_has_paused() => Headless.Run(() =>
    {
        using var till = WithCustomers();

        till.Key(Key.F5, PhysicalKey.F5);
        Keys(till, "Samira Benali");
        Assert.Empty(till.Server.Named); // nothing asked while the name is being typed

        till.WaitFor(() => till.Server.Named.Count == 1); // asked by itself after the pause, once
        Assert.Equal("Samira Benali", till.Server.Named[0]);
        till.WaitFor(() => FormOf(till)?.Rows.Count == 1);
        Assert.Equal("•••• •• 34 56", FormOf(till)!.Rows[0].Detail);
    });

    [Fact]
    public Task A_first_name_alone_asks_nobody_and_says_the_name_is_incomplete() => Headless.Run(() =>
    {
        using var till = WithCustomers();

        till.Key(Key.F5, PhysicalKey.F5);
        Keys(till, "Samira");
        till.Key(Key.Enter, PhysicalKey.Enter);

        Assert.Equal(TillText.French.NameIncomplete, FormOf(till)!.Message!.Title);
        Thread.Sleep(TillWindow.CustomerSearchAfter + TimeSpan.FromMilliseconds(300));
        till.Pump();
        Assert.Empty(till.Server.Named);
    });

    [Fact]
    public Task A_number_that_is_not_one_is_refused_before_the_server_is_asked() => Headless.Run(() =>
    {
        using var till = WithCustomers();

        till.Key(Key.F5, PhysicalKey.F5);
        Keys(till, "0950123456");
        till.Key(Key.Enter, PhysicalKey.Enter);

        Assert.Equal(TillText.French.NotAPhone, FormOf(till)!.Message!.Title);
        Assert.DoesNotContain(till.Server.Searches, search => search.StartsWith('0'));
    });

    [Fact]
    public Task Nobody_with_the_number_creates_them_with_a_managers_pin_and_attaches_them() => Headless.Run(() =>
    {
        using var till = WithCustomers();

        till.Key(Key.F5, PhysicalKey.F5);
        Keys(till, "0661457890");
        till.Key(Key.Enter, PhysicalKey.Enter); // Chercher: nobody
        till.WaitFor(() => FormOf(till)?.Primary == TillText.French.CreateThisClient);
        till.Key(Key.Enter, PhysicalKey.Enter); // Créer ce client
        Assert.Equal(TillText.French.NewClientTitle, FormOf(till)!.Title);

        Keys(till, "Nadia Ouali");
        till.Key(Key.Enter, PhysicalKey.Enter); // Créer et rattacher: a cashier asks a manager
        SamiaAuthorises(till);
        till.WaitFor(() => till.Session.Cart.Customer is not null);

        var created = till.Server.Created.Single();
        Assert.Equal(("Nadia Ouali", "0661457890", "auth-samia"), (created.Name, created.Phone, created.Authorisation));
        Assert.Equal("Nadia O.", till.Session.Cart.Customer!.ShortName);
    });

    [Fact]
    public Task The_attached_name_opens_the_carnet_and_a_repayment_goes_in_for_what_was_typed() => Headless.Run(() =>
    {
        using var till = WithCustomers();
        AttachSamira(till);

        till.Key(Key.F5, PhysicalKey.F5); // the attached customer: the carnet
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Border>().Any(border => border.Tag is CarnetView));
        var carnet = till.Window.GetVisualDescendants().OfType<Border>().Select(border => border.Tag).OfType<CarnetView>().Single();
        Assert.Equal(3, carnet.Rows.Count);
        Assert.Equal("3 200,00", carnet.Rows[^1].Balance); // the ledger's own sum, oldest first

        till.Tap(till.Window.GetVisualDescendants().OfType<TillKey>().Single(key => Equals(key.Tag, TillText.French.RepayKey)), new Point(20, 20));
        till.WaitFor(() => FormOf(till)?.Rows.Count > 0);
        Keys(till, "1200");
        till.Tap(Keyed<FormRow>(till, row => row.Id == "caisse-reglement"), new Point(20, 10));
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.RepaidTitle);

        Assert.Equal(("1200.00", "caisse-reglement"), (till.Server.Repaid.Single().Amount, till.Server.Repaid.Single().ReasonCode));
    });

    [Fact]
    public Task A_repayment_above_the_balance_is_refused_at_the_till_and_never_sent() => Headless.Run(() =>
    {
        using var till = WithCustomers();
        AttachSamira(till);
        till.Key(Key.F5, PhysicalKey.F5);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Border>().Any(border => border.Tag is CarnetView));
        till.Tap(till.Window.GetVisualDescendants().OfType<TillKey>().Single(key => Equals(key.Tag, TillText.French.RepayKey)), new Point(20, 20));
        till.WaitFor(() => FormOf(till)?.Rows.Count > 0);

        Keys(till, "3500");
        till.Tap(Keyed<FormRow>(till, row => row.Id == "caisse-reglement"), new Point(20, 10));
        till.Key(Key.Enter, PhysicalKey.Enter);

        Assert.Equal((TillText.French.AboveDue, false), (FormOf(till)!.Message!.Title, FormOf(till)!.MayPrimary));
        Assert.Empty(till.Server.Repaid);
    });

    [Fact]
    public Task A_new_limit_needs_the_owners_pin_and_is_sent_as_typed() => Headless.Run(() =>
    {
        using var till = WithCustomers();
        AttachSamira(till);
        till.Key(Key.F5, PhysicalKey.F5);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Border>().Any(border => border.Tag is CarnetView));

        till.Tap(till.Window.GetVisualDescendants().OfType<TillKey>().Single(key => key.Tag is FormKey { Id: CustomerScreen.ChangeKey }), new Point(20, 20));
        Keys(till, "12000");
        till.Key(Key.Enter, PhysicalKey.Enter);
        SamiaAuthorises(till);
        till.WaitFor(() => till.Server.Limits.Count == 1);

        Assert.Equal((LimitActions.Set, "12000.00", "auth-samia"), (till.Server.Limits[0].Action, till.Server.Limits[0].Limit, till.Server.Limits[0].Authorisation));
    });

    [Fact]
    public Task A_ticket_goes_on_the_tab_whole_when_the_shop_says_so() => Headless.Run(() =>
    {
        using var till = WithCustomers(server => server.TabAsPart = false);
        till.ScanMany(1);
        AttachSamira(till);

        till.Key(Key.F12, PhysicalKey.F12);
        till.WaitFor(() => PaymentOf(till)?.Methods.Any(method => method.Method == PaymentMethod.OnAccount) == true);
        till.Tap(Keyed<MethodChoice>(till, choice => choice.Method == PaymentMethod.OnAccount), new Point(20, 20));
        Assert.Equal(TillText.French.ValidateOnTab, PaymentOf(till)!.Primary);
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Server.Requests.Count == 1);

        var sale = till.Server.Requests[0];
        Assert.Equal(("c-samira", (string?)null), (sale.CustomerId, sale.TabOverride));
        Assert.Equal(new TenderRequest(TenderMethods.OnAccount, "143.00"), Assert.Single(sale.Tenders!));
    });

    [Fact]
    public Task Past_the_limit_the_owners_pin_lets_the_ticket_on_and_is_named_on_the_sale() => Headless.Run(() =>
    {
        using var till = WithCustomers(server =>
        {
            server.TabAsPart = false;
            server.TabBalance = "7900.00";
            server.TabAvailable = "100.00";
        });
        till.ScanMany(1);
        AttachSamira(till);

        till.Key(Key.F12, PhysicalKey.F12);
        till.WaitFor(() => PaymentOf(till)?.Methods.Any(method => method.Method == PaymentMethod.OnAccount) == true);
        till.Tap(Keyed<MethodChoice>(till, choice => choice.Method == PaymentMethod.OnAccount), new Point(20, 20));
        Assert.Equal((TillText.French.AboveLimit, TillText.French.OverrideWithOwnerPin), (PaymentOf(till)!.Message!.Title, PaymentOf(till)!.Primary));

        till.Key(Key.Enter, PhysicalKey.Enter);
        SamiaAuthorises(till);
        till.WaitFor(() => till.Server.Requests.Count == 1);

        Assert.Equal("auth-samia", till.Server.Requests[0].TabOverride);
    });

    [Fact]
    public Task A_frozen_tab_takes_nothing_and_nothing_lets_it_through() => Headless.Run(() =>
    {
        using var till = WithCustomers(server => server.TabFrozen = true);
        till.ScanMany(1);
        AttachSamira(till);

        till.Key(Key.F12, PhysicalKey.F12);
        till.WaitFor(() => PaymentOf(till)?.Methods.Any(method => method.Method == PaymentMethod.OnAccount) == true);
        till.Tap(Keyed<MethodChoice>(till, choice => choice.Method == PaymentMethod.OnAccount), new Point(20, 20));
        till.Key(Key.Enter, PhysicalKey.Enter);

        Assert.Equal((TillText.French.TabFrozenRefused, false), (PaymentOf(till)!.Message!.Title, PaymentOf(till)!.MayPrimary));
        Assert.Empty(till.Server.Requests);
    });

    // ------------------------------------------------------------------ B9b: store credit spent (Hakim's rule asked)

    [Fact]
    public Task Store_credit_is_offered_prefilled_with_the_smaller_of_the_credit_and_the_rest_and_sent_as_a_part() => Headless.Run(() =>
    {
        using var till = WithCustomers(server => server.TabCredit = "100.00");
        till.ScanMany(1); // 143,00
        AttachSamira(till);

        till.Key(Key.F12, PhysicalKey.F12);
        till.WaitFor(() => PaymentOf(till)?.Methods.Any(method => method.Method == PaymentMethod.StoreCredit) == true);
        till.Tap(Keyed<MethodChoice>(till, choice => choice.Method == PaymentMethod.StoreCredit), new Point(20, 20));
        Assert.Equal("100,00", PaymentOf(till)!.Entry!.Amount); // the credit, smaller than the 143,00 left

        till.Key(Key.Enter, PhysicalKey.Enter); // Ajouter la part
        till.Key(Key.Enter, PhysicalKey.Enter); // Valider: the rest in cash
        till.WaitFor(() => till.Server.Requests.Count == 1);

        Assert.Equal([new TenderRequest(TenderMethods.StoreCredit, "100.00")], till.Server.Requests[0].Tenders);
        Assert.Equal("c-samira", till.Server.Requests[0].CustomerId);
    });

    [Fact]
    public Task More_than_the_credit_is_refused_in_the_panel_and_never_added() => Headless.Run(() =>
    {
        using var till = WithCustomers(server => server.TabCredit = "100.00");
        till.ScanMany(1);
        AttachSamira(till);

        till.Key(Key.F12, PhysicalKey.F12);
        till.WaitFor(() => PaymentOf(till)?.Methods.Any(method => method.Method == PaymentMethod.StoreCredit) == true);
        till.Tap(Keyed<MethodChoice>(till, choice => choice.Method == PaymentMethod.StoreCredit), new Point(20, 20));
        Keys(till, "120");
        till.Key(Key.Enter, PhysicalKey.Enter);

        Assert.Equal((TillText.French.AboveCredit, false), (PaymentOf(till)!.Message!.Title, PaymentOf(till)!.MayPrimary));
        Assert.Empty(PaymentOf(till)!.Parts);
    });

    [Fact]
    public Task No_credit_no_avoir_key() => Headless.Run(() =>
    {
        using var till = WithCustomers();
        till.ScanMany(1);
        AttachSamira(till);

        till.Key(Key.F12, PhysicalKey.F12);
        till.WaitFor(() => PaymentOf(till)?.Methods.Any(method => method.Method == PaymentMethod.OnAccount) == true);

        Assert.DoesNotContain(PaymentOf(till)!.Methods, method => method.Method == PaymentMethod.StoreCredit);
    });

    private sealed partial class FakeStoreServer
    {
        /// <summary>The tenant keeps customers (D-096): the context says so.</summary>
        public bool CustomerModule { get; set; }

        /// <summary>The tab may be a part of a ticket.</summary>
        public bool TabAsPart { get; set; } = true;

        /// <summary>Samira's tab: 8 000,00, 3 200,00 owed, this much available.</summary>
        public string TabAvailable { get; set; } = "4800.00";

        public bool TabFrozen { get; set; }

        public string TabBalance { get; set; } = "3200.00";

        /// <summary>Samira's store credit available (B9b).</summary>
        public string TabCredit { get; set; } = "0.00";

        /// <summary>Every name searched (D-100).</summary>
        public List<string> Named { get; } = [];

        public Task<CustomerSearchAnswer?> FindCustomersByNameAsync(string name, string terminalId, string sessionToken, CancellationToken cancellationToken = default)
        {
            Named.Add(name);
            return Task.FromResult<CustomerSearchAnswer?>(new CustomerSearchAnswer(
                CustomerOutcomes.Ok,
                string.Equals(name, "Samira Benali", StringComparison.OrdinalIgnoreCase)
                    ? [new CustomerSummaryWire("c-samira", "Samira Benali", "•••• •• 34 56")]
                    : []));
        }

        public List<CreateCustomerRequest> Created { get; } = [];

        public List<RepaymentRequest> Repaid { get; } = [];

        public List<LimitRequest> Limits { get; } = [];

        /// <summary>0550 12 34 56 is Samira Benali's and Samir Benali's; any other number, nobody's.</summary>
        public Task<CustomerSearchAnswer?> FindCustomersAsync(string phone, string terminalId, string sessionToken, CancellationToken cancellationToken = default)
        {
            Searches.Add(phone);
            return Task.FromResult<CustomerSearchAnswer?>(new CustomerSearchAnswer(
                CustomerOutcomes.Ok,
                phone == "0550123456"
                    ? [new CustomerSummaryWire("c-samira", "Samira Benali", "+213550123456"), new CustomerSummaryWire("c-samir", "Samir Benali", "+213550123456")]
                    : []));
        }

        /// <summary>As the server answers: a cashier creates nobody without an authorisation.</summary>
        public Task<CustomerAnswer?> CreateCustomerAsync(CreateCustomerRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            Created.Add(request);
            return Task.FromResult<CustomerAnswer?>(request.Authorisation is null && !SellerMayDiscount
                ? new CustomerAnswer(CustomerOutcomes.NotAllowed, null, "An authorisation is asked first.")
                : new CustomerAnswer(CustomerOutcomes.Ok, new CustomerSummaryWire("c-new", request.Name, "+213" + request.Phone[1..])));
        }

        public Task<TabAnswer?> TabAsync(string customerId, string terminalId, string sessionToken, CancellationToken cancellationToken = default) =>
            Task.FromResult<TabAnswer?>(Tab(TabBalance));

        public Task<TabAnswer?> RepayAsync(string customerId, RepaymentRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            Repaid.Add(request);
            return Task.FromResult<TabAnswer?>(Tab("2000.00"));
        }

        public Task<TabAnswer?> ChangeLimitAsync(string customerId, LimitRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            Limits.Add(request);
            return Task.FromResult<TabAnswer?>(Tab("3200.00"));
        }

        public Task<Waymark.Contracts.Reference.ReasonCodeList?> CashReasonsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Waymark.Contracts.Reference.ReasonCodeList?>(new Waymark.Contracts.Reference.ReasonCodeList("cash_movement",
                [
                    new Waymark.Contracts.Reference.ReasonCodeOption("caisse-reglement", "تسديد دين زبون", "Remboursement de carnet", false, false, "in"),
                    new Waymark.Contracts.Reference.ReasonCodeOption("caisse-depot", "إيداع في البنك", "Dépôt en banque", false, false, "out"),
                    new Waymark.Contracts.Reference.ReasonCodeOption("caisse-divers", "متنوع", "Divers", true, false),
                ]));

        private TabAnswer Tab(string balance) => new(
            CustomerOutcomes.Ok,
            new CustomerSummaryWire("c-samira", "Samira Benali", "+213550123456"),
            balance,
            "8000.00",
            TabAvailable,
            TabFrozen,
            DateTimeOffset.UtcNow.AddDays(-19), // within the 30 days, whenever the test runs
            30,
            "DZD",
            [
                new TabMovementWire("charge", "1800.00", new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero)),
                new TabMovementWire("payment", "-1800.00", new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero)),
                new TabMovementWire("charge", "3200.00", DateTimeOffset.UtcNow.AddDays(-19)),
            ],
            null,
            TabCredit);
    }
}
