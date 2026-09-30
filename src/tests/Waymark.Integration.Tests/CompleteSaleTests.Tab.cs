using Microsoft.EntityFrameworkCore;
using Waymark.Application.Commands;
using Waymark.Application.Customers;
using Waymark.Application.IdGenerator;
using Waymark.Application.Organisation;
using Waymark.Application.Sales;
using Waymark.Domain;
using Waymark.Domain.Customers;
using Waymark.Domain.Enums;
using Waymark.Domain.Privacy;
using Waymark.Domain.Reference;
using Waymark.Domain.Values;
using Waymark.Persistence;
using Waymark.Persistence.Customers;
using Waymark.Persistence.Organisation;
using Waymark.Persistence.Privacy;
using Waymark.Persistence.Sales;

namespace Waymark.Integration.Tests;

/// <summary>
/// B7 (D-096): the customers and their tabs, against a real database. The silent failures: a customer
/// kept by a shop that switched the module off; a number stored in a form the next search misses; a
/// look at a named person with no log row; a charge written without its payment row, or the other way
/// round; a charge past the limit written with nobody named for it; a limit changed with no trace of
/// who changed it; a repayment that reaches the tab and not the drawer.
/// </summary>
/// <remarks>
/// The tenant's settings are in <c>system_config</c>, shared by every test of this class's database, so
/// each test here sets all four first. The tests marked <b>Hakim</b> go through <see cref="Tab"/> or the
/// tab part of <see cref="Domain.Sales.Tender"/>, and are red until they are written.
/// </remarks>
public sealed partial class CompleteSaleTests
{
    private const string Repayment = "caisse-reglement";

    private static string NewPhone() => "055" + Random.Shared.Next(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The four settings, all of them, and what B7 needs in the tenant's reference data.</summary>
    private async Task ConfigureAsync(Shop shop, bool module = true, long? ceiling = null, int? overdueDays = null, bool tabAsPart = true)
    {
        await using (var context = database.NewContext())
        {
            if (!await context.NoticeVersions.AnyAsync(notice => notice.NoticeType == NoticeType.Information))
            {
                context.NoticeVersions.Add(new NoticeVersion
                {
                    VersionCode = "information-ar-test", NoticeType = NoticeType.Information, Language = Language.Ar,
                    BodyText = "نص إشعار للاختبار.", EffectiveFrom = "2026-01-01", PublishedAt = Now,
                });
            }

            if (!await context.ReasonCodes.AnyAsync(reason => reason.ReasonCodeValue == Repayment))
            {
                context.ReasonCodes.Add(new ReasonCode
                {
                    ReasonCodeValue = Repayment, AppliesTo = ReasonCodeAppliesTo.CashMovement, LabelAr = "تسديد دين زبون", LabelFr = "Règlement du carnet client", CreatedAt = Now,
                });
            }

            await context.SaveChangesAsync();
        }

        await Run(shop, (context, _) => new SetTenantSettingsHandler(new TenantConfigurationStore(context, TabChargesFor.Dzd), TabChargesFor.Dzd, new FixedClock()),
            new SetTenantSettings(module, ceiling, ceiling is null, overdueDays, overdueDays is null, tabAsPart));
    }

    /// <summary>A command, through the executor, in its own unit of work over this shop's store.</summary>
    private async Task<TResult> Run<TCommand, TResult>(
        Shop shop, Func<WaymarkDbContext, WaymarkUnitOfWork, ICommandHandler<TCommand, TResult>> build, TCommand command)
        where TCommand : ICommand<TResult>
    {
        await using var context = database.NewContext(storeId: shop.StoreId);
        var ids = new UlidGenerator();
        var unitOfWork = new WaymarkUnitOfWork(context);
        var executor = new CommandExecutor(unitOfWork, ids, new ProcessingLogWriter(context, ids, new FixedCurrentStore(shop.StoreId), new FixedClock()));
        return await executor.ExecuteAsync(build(context, unitOfWork), command);
    }

    private Task<CustomerSummary> Create(Shop shop, string name, string phone) =>
        Run(shop, (context, work) => new CreateCustomerHandler(
                new CustomerLedger(context), new TenantConfigurationStore(context, TabChargesFor.Dzd), TabChargesFor.Pseudonymiser, TabChargesFor.Dzd,
                work, new FixedCalendar(), new FixedClock()),
            new CreateCustomer(shop.StaffId, name, phone, shop.TerminalId));

    private Task<IReadOnlyList<CustomerSummary>> Find(Shop shop, string phone) =>
        Run(shop, (context, _) => new FindCustomersHandler(
                new CustomerLedger(context), new TenantConfigurationStore(context, TabChargesFor.Dzd), TabChargesFor.Pseudonymiser, TabChargesFor.Dzd),
            new FindCustomers(shop.StaffId, phone, shop.TerminalId));

    private Task<TabView> Limit(Shop shop, string customerId, LimitChange change, long? limit = null) =>
        Run(shop, (context, work) => new ChangeCreditLimitHandler(
                new CustomerLedger(context), new TenantConfigurationStore(context, TabChargesFor.Dzd), TabChargesFor.Pseudonymiser, TabChargesFor.Dzd,
                work, new FixedClock()),
            new ChangeCreditLimit(shop.StaffId, customerId, change, limit, shop.TerminalId));

    private Task<TabView> Repay(Shop shop, string customerId, long amount) =>
        Run(shop, (context, work) => new RepayTabHandler(
                new CustomerLedger(context), new TenantConfigurationStore(context, TabChargesFor.Dzd), TabChargesFor.Pseudonymiser, TabChargesFor.Dzd,
                work, new SalesLedger(context), new Waymark.Persistence.Reference.ReasonCodes(context), new FixedClock()),
            new RepayTab(shop.TerminalId, shop.StaffId, customerId, amount, Repayment));

    private Task<CompletedSale> OnTab(Shop shop, string customerId, long tabPart, string? overrideBy = null) =>
        Execute(shop, new CompleteSale(
            shop.TerminalId, shop.StaffId, [new SaleLineRequest(shop.Milk.Barcode, 1)],
            Tenders: [new GivenTender(PaymentMethod.OnAccount, tabPart, null)], CustomerId: customerId, TabOverrideBy: overrideBy));

    private static string PseudonymOf(string customerId) => TabChargesFor.Pseudonymiser.PseudonymFor(SubjectDomain.Customer, customerId).Value;

    // ------------------------------------------------------------------ the module switch

    [Fact]
    public async Task With_the_module_off_no_customer_is_found_created_or_sold_to_and_nothing_is_written()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);
        await ConfigureAsync(shop, module: false);

        var find = await Assert.ThrowsAsync<CustomerRefusedException>(() => Find(shop, NewPhone()));
        var create = await Assert.ThrowsAsync<CustomerRefusedException>(() => Create(shop, "Karim", NewPhone()));
        await Assert.ThrowsAsync<SaleRefusedException>(() =>
            Execute(shop, new CompleteSale(shop.TerminalId, shop.StaffId, [new SaleLineRequest(shop.Milk.Barcode, 1)], CustomerId: "anyone")));

        Assert.Equal((CustomerRefusal.ModuleOff, CustomerRefusal.ModuleOff), (find.Refusal, create.Refusal));
        using var read = Read(shop);
        Assert.False(await read.ProcessingLog.AnyAsync());
        Assert.False(await read.Transactions.AnyAsync());
    }

    [Fact]
    public async Task The_settings_read_back_as_set_and_a_key_removed_reads_as_its_default()
    {
        var shop = new Shop(database);
        await ConfigureAsync(shop, ceiling: 5_000_000, overdueDays: 30, tabAsPart: false);

        await using (var context = database.NewContext())
        {
            Assert.Equal(
                new Domain.Organisation.TenantSettings(true, Money.FromMinorUnits(5_000_000, Currency.Dzd), 30, false),
                await new TenantConfigurationStore(context, TabChargesFor.Dzd).CurrentAsync());
        }

        await ConfigureAsync(shop);
        await using (var context = database.NewContext())
        {
            Assert.Equal(new Domain.Organisation.TenantSettings(true, null, null, true), await new TenantConfigurationStore(context, TabChargesFor.Dzd).CurrentAsync());
            Assert.False(await context.SystemConfig.AnyAsync(entry => entry.ConfigKey == Domain.Organisation.TenantSettings.MaxCreditLimitKey));
        }
    }

    // ------------------------------------------------------------------ customers

    [Fact]
    public async Task A_customer_created_at_the_till_keeps_the_number_in_one_form_the_notice_handed_over_and_no_tab()
    {
        var shop = new Shop(database);
        await ConfigureAsync(shop);
        var phone = NewPhone();

        var created = await Create(shop, "  Karim B.  ", $"{phone[..4]} {phone[4..6]} {phone[6..8]} {phone[8..]}");

        using var read = Read(shop);
        var customer = await read.Customers.SingleAsync(row => row.CustomerId == created.CustomerId);
        Assert.Equal(("Karim B.", "+213" + phone[1..], "information-ar-test"), (customer.CustomerName, customer.ContactPhone, customer.CollectionNoticeVersion));
        Assert.Null(customer.CreditLimit); // no tab until the owner gives one
        Assert.Equal(LegalBasis.Contract, customer.LegalBasis);

        var logged = await read.ProcessingLog.SingleAsync(entry => entry.SubjectId == PseudonymOf(created.CustomerId));
        Assert.Equal((Operation.Collection, ProcessingPurpose.CreditManagement, shop.StaffId), (logged.Operation, logged.Purpose, logged.ActorId));
        Assert.DoesNotContain(created.CustomerId, logged.SubjectId, StringComparison.Ordinal); // a pseudonym, never the id
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    public async Task A_number_that_is_not_one_creates_nobody(string phone)
    {
        var shop = new Shop(database);
        await ConfigureAsync(shop);

        var refused = await Assert.ThrowsAsync<CustomerRefusedException>(() => Create(shop, "Karim", phone));

        Assert.Equal(CustomerRefusal.Invalid, refused.Refusal);
    }

    [Fact]
    public async Task A_number_is_found_however_it_is_typed_everyone_on_it_is_listed_and_each_one_listed_is_logged()
    {
        var shop = new Shop(database);
        await ConfigureAsync(shop);
        var phone = NewPhone();
        var karim = await Create(shop, "Karim", phone);
        var amina = await Create(shop, "Amina", phone);

        var found = await Find(shop, "+213 " + phone[1..]);

        Assert.Equal(["Amina", "Karim"], found.Select(customer => customer.Name)); // by name
        using var read = Read(shop);
        Assert.Equal(1, await read.ProcessingLog.CountAsync(entry => entry.SubjectId == PseudonymOf(karim.CustomerId) && entry.Operation == Operation.Consultation));
        Assert.Equal(1, await read.ProcessingLog.CountAsync(entry => entry.SubjectId == PseudonymOf(amina.CustomerId) && entry.Operation == Operation.Consultation));
    }

    [Fact]
    public async Task A_sale_to_a_customer_with_no_tab_part_is_recorded_against_them_and_logged()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);
        await ConfigureAsync(shop);
        var karim = await Create(shop, "Karim", NewPhone());

        var sale = await Execute(shop, new CompleteSale(shop.TerminalId, shop.StaffId, [new SaleLineRequest(shop.Milk.Barcode, 1)], CustomerId: karim.CustomerId));

        using var read = Read(shop);
        Assert.Equal(karim.CustomerId, (await read.Transactions.SingleAsync(row => row.TransactionId == sale.TransactionId)).CustomerId);
        Assert.Contains(await read.ProcessingLog.Where(entry => entry.SubjectId == PseudonymOf(karim.CustomerId)).ToListAsync(),
            entry => entry.Operation == Operation.Collection && entry.Purpose == ProcessingPurpose.PosSale);
        Assert.False(await read.ReceivableMovements.AnyAsync(movement => movement.CustomerId == karim.CustomerId));
    }

    // ------------------------------------------------------------------ Hakim: the tab

    [Fact]
    public async Task A_limit_given_is_on_the_customer_and_in_the_events_with_who_gave_it()
    {
        var shop = new Shop(database);
        await ConfigureAsync(shop);
        var karim = await Create(shop, "Karim", NewPhone());

        var tab = await Limit(shop, karim.CustomerId, LimitChange.Set, 500_000);

        Assert.Equal((Money.FromMinorUnits(500_000, Currency.Dzd), Money.FromMinorUnits(500_000, Currency.Dzd)), (tab.Limit!.Value, tab.Available!.Value));
        using var read = Read(shop);
        Assert.Equal(Money.FromMinorUnits(500_000, Currency.Dzd), (await read.Customers.SingleAsync(row => row.CustomerId == karim.CustomerId)).CreditLimit);
        var change = await read.CreditLimitEvents.SingleAsync(row => row.CustomerId == karim.CustomerId);
        Assert.Equal((CreditLimitEventType.Set, (Money?)null, Money.FromMinorUnits(500_000, Currency.Dzd), shop.StaffId),
            (change.EventType, change.PreviousLimit, change.NewLimit!.Value, change.StaffId));
        Assert.Contains(await read.ProcessingLog.Where(entry => entry.SubjectId == PseudonymOf(karim.CustomerId)).ToListAsync(),
            entry => entry.Operation == Operation.Modification);
    }

    [Fact]
    public async Task A_limit_above_the_ceiling_is_refused_and_nothing_changes()
    {
        var shop = new Shop(database);
        await ConfigureAsync(shop, ceiling: 1_000_000);
        var karim = await Create(shop, "Karim", NewPhone());

        await Assert.ThrowsAsync<CustomerRefusedException>(() => Limit(shop, karim.CustomerId, LimitChange.Set, 1_000_001));

        using var read = Read(shop);
        Assert.Null((await read.Customers.SingleAsync(row => row.CustomerId == karim.CustomerId)).CreditLimit);
        Assert.False(await read.CreditLimitEvents.AnyAsync(row => row.CustomerId == karim.CustomerId));
    }

    [Fact]
    public async Task Freezing_twice_is_one_event_and_unfreezing_is_another()
    {
        var shop = new Shop(database);
        await ConfigureAsync(shop);
        var karim = await Create(shop, "Karim", NewPhone());
        await Limit(shop, karim.CustomerId, LimitChange.Set, 500_000);

        Assert.True((await Limit(shop, karim.CustomerId, LimitChange.Freeze)).Frozen);
        await Limit(shop, karim.CustomerId, LimitChange.Freeze);
        Assert.False((await Limit(shop, karim.CustomerId, LimitChange.Unfreeze)).Frozen);

        using var read = Read(shop);
        Assert.Equal(
            [CreditLimitEventType.Set, CreditLimitEventType.Frozen, CreditLimitEventType.Unfrozen],
            (await read.CreditLimitEvents.Where(row => row.CustomerId == karim.CustomerId).ToListAsync()).OrderBy(row => row.EventId).Select(row => row.EventType));
    }

    [Fact]
    public async Task A_ticket_on_the_tab_writes_its_payment_row_and_the_charge_pointing_at_it()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);
        await ConfigureAsync(shop);
        var karim = await Create(shop, "Karim", NewPhone());
        await Limit(shop, karim.CustomerId, LimitChange.Set, 500_000);

        var sale = await OnTab(shop, karim.CustomerId, 14_300);

        using var read = Read(shop);
        var payment = await read.TransactionPayments.SingleAsync(row => row.TransactionId == sale.TransactionId);
        var charge = await read.ReceivableMovements.SingleAsync(row => row.CustomerId == karim.CustomerId);
        Assert.Equal((PaymentMethod.OnAccount, Dzd(14_300)), (payment.PaymentMethod, payment.Amount));
        Assert.Equal((ReceivableMovementType.Charge, Dzd(14_300), payment.PaymentId, (string?)null),
            (charge.MovementType, charge.Amount, charge.PaymentId, charge.OverrideAuthorisedBy));
    }

    [Fact]
    public async Task A_tab_part_past_the_limit_is_refused_with_nothing_written_and_goes_on_with_the_owner_named()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);
        await ConfigureAsync(shop);
        var karim = await Create(shop, "Karim", NewPhone());
        await Limit(shop, karim.CustomerId, LimitChange.Set, 10_000);

        await Assert.ThrowsAsync<SaleRefusedException>(() => OnTab(shop, karim.CustomerId, 14_300));
        using (var read = Read(shop))
        {
            Assert.False(await read.Transactions.AnyAsync());
        }

        await OnTab(shop, karim.CustomerId, 14_300, overrideBy: shop.StaffId);
        using (var read = Read(shop))
        {
            Assert.Equal(shop.StaffId, (await read.ReceivableMovements.SingleAsync(row => row.CustomerId == karim.CustomerId)).OverrideAuthorisedBy);
        }
    }

    [Fact]
    public async Task A_frozen_tab_takes_no_charge_even_with_an_override()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);
        await ConfigureAsync(shop);
        var karim = await Create(shop, "Karim", NewPhone());
        await Limit(shop, karim.CustomerId, LimitChange.Set, 500_000);
        await Limit(shop, karim.CustomerId, LimitChange.Freeze);

        await Assert.ThrowsAsync<SaleRefusedException>(() => OnTab(shop, karim.CustomerId, 14_300, overrideBy: shop.StaffId));
    }

    [Fact]
    public async Task A_repayment_is_a_paid_in_on_the_drawer_and_a_payment_on_the_tab_pointing_at_it()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);
        await ConfigureAsync(shop);
        var karim = await Create(shop, "Karim", NewPhone());
        await Limit(shop, karim.CustomerId, LimitChange.Set, 500_000);
        await OnTab(shop, karim.CustomerId, 14_300);

        var tab = await Repay(shop, karim.CustomerId, 10_000);

        Assert.Equal(Dzd(4_300), tab.Balance);
        using var read = Read(shop);
        var repayment = await read.ReceivableMovements.SingleAsync(row => row.CustomerId == karim.CustomerId && row.MovementType == ReceivableMovementType.Payment);
        var paidIn = await read.CashMovements.SingleAsync(row => row.MovementId == repayment.CashMovementId);
        Assert.Equal((Dzd(-10_000), CashMovementType.PaidIn, Dzd(10_000), Repayment), (repayment.Amount, paidIn.MovementType, paidIn.Amount, paidIn.ReasonCode));
    }

    [Fact]
    public async Task A_repayment_of_more_than_is_owed_is_refused_and_the_drawer_takes_nothing()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 5, daysAgo: 5);
        await ConfigureAsync(shop);
        var karim = await Create(shop, "Karim", NewPhone());
        await Limit(shop, karim.CustomerId, LimitChange.Set, 500_000);
        await OnTab(shop, karim.CustomerId, 14_300);

        await Assert.ThrowsAsync<CustomerRefusedException>(() => Repay(shop, karim.CustomerId, 14_301));

        using var read = Read(shop);
        Assert.False(await read.CashMovements.AnyAsync());
    }
}
