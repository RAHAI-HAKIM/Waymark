using Waymark.Application.Commands;
using Waymark.Application.Sales;
using Waymark.Domain;
using Waymark.Domain.Customers;
using Waymark.Domain.Enums;
using Waymark.Domain.Ledgers;
using Waymark.Domain.Organisation;
using Waymark.Domain.Privacy;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.Domain.Work;

namespace Waymark.Application.Customers;

/// <summary>Why a customer command was refused. Nothing was written, not even a log row.</summary>
public enum CustomerRefusal
{
    /// <summary>The tenant keeps no customers (<c>customer_module</c> off, D-096).</summary>
    ModuleOff,

    /// <summary>No such customer, or one erased.</summary>
    NotFound,

    /// <summary>What was sent cannot be used: an empty name, a number that is not one, an amount that is not above zero.</summary>
    Invalid,

    /// <summary>No information notice is in force: a customer cannot be created without being told (Art. 32).</summary>
    NoNotice,

    /// <summary>The tab's rules said no: above the ceiling, above the balance.</summary>
    Refused,
}

/// <summary>A customer command refused, with why, in words, for the cashier.</summary>
public sealed class CustomerRefusedException(CustomerRefusal refusal, string reason) : Exception(reason)
{
    public CustomerRefusal Refusal { get; } = refusal;
}

/// <summary>A customer as the till lists one: never more than it needs to pick the right person.</summary>
public sealed record CustomerSummary(string CustomerId, string Name, string? Phone);

/// <summary>A customer's tab, as the till shows it (B7, D-096).</summary>
/// <param name="Available">The limit less the balance; null with no tab.</param>
/// <param name="OldestUnpaid">When the oldest charge still unpaid was made; null when nothing is owed.</param>
/// <param name="OverdueDays">The tenant's overdue days, so the till can say how old is too old; null when the rule is off.</param>
/// <param name="Movements">Every movement at this store, oldest first: the statement.</param>
public sealed record TabView(
    CustomerSummary Customer,
    Money Balance,
    Money? Limit,
    Money? Available,
    bool Frozen,
    DateTimeOffset? OldestUnpaid,
    int? OverdueDays,
    IReadOnlyList<ReceivableMovement> Movements);

/// <summary>What a limit command does (B7, D-096).</summary>
public enum LimitChange
{
    /// <summary>Give, raise, lower or take away the limit (null: no tab).</summary>
    Set,

    Freeze,

    Unfreeze,
}

/// <summary>Find customers by their number (B7).</summary>
public sealed record FindCustomers(string StaffId, string Phone, string? TerminalId = null) : ICommand<IReadOnlyList<CustomerSummary>>;

/// <summary>Create a customer at the till, the information notice handed over (B7). The host has checked <c>CreateCustomer</c>.</summary>
public sealed record CreateCustomer(string StaffId, string Name, string Phone, string? TerminalId = null) : ICommand<CustomerSummary>;

/// <summary>Open a customer's tab: the balance, what is left, the statement (B7).</summary>
public sealed record OpenTab(string StaffId, string CustomerId, string? TerminalId = null) : ICommand<TabView>;

/// <summary>Change a customer's limit, or freeze or unfreeze the tab (B7). The host has checked <c>ManageCredit</c>.</summary>
/// <param name="Limit">For <see cref="LimitChange.Set"/>, in minor units; null closes the tab.</param>
public sealed record ChangeCreditLimit(string StaffId, string CustomerId, LimitChange Change, long? Limit, string? TerminalId = null) : ICommand<TabView>;

/// <summary>A repayment in cash at this till (B7): a <c>paid_in</c> on the drawer and a <c>payment</c> on the tab (D-055).</summary>
/// <param name="Amount">Minor units.</param>
/// <param name="ReasonCode">An active <c>cash_movement</c> reason, the <c>paid_in</c>'s.</param>
public sealed record RepayTab(string TerminalId, string StaffId, string CustomerId, long Amount, string ReasonCode) : ICommand<TabView>;

/// <summary>
/// What the customer commands share (B7, D-096): the module switched on before anything, the
/// customer found, the tab read, and every look at a named person logged under their pseudonym
/// (D-045, D-061). A consultation commits even when nothing else does: the log row is the evidence.
/// </summary>
public abstract class CustomerCommandBase(
    ICustomerLedger customers,
    ITenantConfiguration configuration,
    IPseudonymiser pseudonymiser,
    ILedgerCurrency ledgerCurrency)
{
    private const string SourceModule = "Waymark.Application.Customers";

    protected ICustomerLedger Customers => customers;

    protected async Task<TenantSettings> ModuleOnAsync(CancellationToken cancellationToken)
    {
        var settings = await configuration.CurrentAsync(cancellationToken);
        return settings.CustomerModule
            ? settings
            : throw new CustomerRefusedException(CustomerRefusal.ModuleOff, "This shop keeps no customers: the customer module is off.");
    }

    protected async Task<Customer> CustomerAsync(string customerId, CancellationToken cancellationToken) =>
        await customers.FindAsync(customerId, cancellationToken)
        ?? throw new CustomerRefusedException(CustomerRefusal.NotFound, "No such customer.");

    /// <summary>A personal-data operation on one customer, staged with the work it records.</summary>
    protected void Log(CommandContext context, Operation operation, string customerId, string staffId, string? terminalId) =>
        context.Record(new ProcessingEvent(
            operation,
            ProcessingLogEntrySubjectType.Customer,
            pseudonymiser.PseudonymFor(SubjectDomain.Customer, customerId),
            ActorType.Staff,
            staffId,
            ProcessingPurpose.CreditManagement,
            ProcessingLegalBasis.Contract,
            SourceModule,
            TerminalId: terminalId));

    /// <summary>The tab as the till shows it, from the customer, the movements and the rule (<see cref="Tab"/>).</summary>
    protected TabView ViewOf(Customer customer, IReadOnlyList<ReceivableMovement> movements, TenantSettings settings, Money? limit, bool frozen)
    {
        var age = Tab.Age(ledgerCurrency.Currency, [.. movements.Select(movement => new TabMovement(movement.OccurredAt, movement.Amount))]);
        return new TabView(
            new CustomerSummary(customer.CustomerId, customer.CustomerName, customer.ContactPhone),
            age.Balance,
            limit,
            limit is { } l ? l - age.Balance : null,
            frozen,
            age.OldestUnpaid,
            settings.CreditOverdueDays,
            movements);
    }

    protected static string Normalised(string phone) => PhoneNumber.TryNormalise(phone, out var normalised)
        ? normalised!
        : throw new CustomerRefusedException(CustomerRefusal.Invalid, "That is not a telephone number: 10 digits starting 05, 06, 07, 02, 03 or 04.");
}

/// <summary>
/// Customers with this number (B7). Each one listed is a consultation of that person and is logged
/// as one; a number no customer has lists no one and logs nothing.
/// </summary>
public sealed class FindCustomersHandler(ICustomerLedger customers, ITenantConfiguration configuration, IPseudonymiser pseudonymiser, ILedgerCurrency currency)
    : CustomerCommandBase(customers, configuration, pseudonymiser, currency), ICommandHandler<FindCustomers, IReadOnlyList<CustomerSummary>>
{
    public async Task<IReadOnlyList<CustomerSummary>> HandleAsync(FindCustomers command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        await ModuleOnAsync(cancellationToken);

        var found = await Customers.FindByPhoneAsync(Normalised(command.Phone), cancellationToken);
        foreach (var customer in found)
        {
            Log(context, Operation.Consultation, customer.CustomerId, command.StaffId, command.TerminalId);
        }

        return [.. found.Select(customer => new CustomerSummary(customer.CustomerId, customer.CustomerName, customer.ContactPhone))];
    }
}

/// <summary>
/// A customer created at the till (B7, D-096): a name, a number in its one form, and the information
/// notice in force, which the cashier has handed over. No tab: a limit is the owner's, set after.
/// </summary>
public sealed class CreateCustomerHandler(
    ICustomerLedger customers, ITenantConfiguration configuration, IPseudonymiser pseudonymiser, ILedgerCurrency currency,
    IStaging staging, IStoreCalendar calendar, TimeProvider clock)
    : CustomerCommandBase(customers, configuration, pseudonymiser, currency), ICommandHandler<CreateCustomer, CustomerSummary>
{
    /// <summary>Longer than any name a till has room to show.</summary>
    public const int MaxName = 100;

    public async Task<CustomerSummary> HandleAsync(CreateCustomer command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        await ModuleOnAsync(cancellationToken);

        var name = (command.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > MaxName)
        {
            throw new CustomerRefusedException(CustomerRefusal.Invalid, $"A name of 1 to {MaxName} characters.");
        }

        var phone = Normalised(command.Phone);
        var notice = await Customers.NoticeInForceAsync(NoticeType.Information, calendar.Today, cancellationToken)
            ?? throw new CustomerRefusedException(CustomerRefusal.NoNotice, "No information notice is published: a customer is not created without being told what is kept (Art. 32).");

        var now = clock.GetUtcNow();
        var customer = new Customer
        {
            CustomerId = context.NewId(),
            CustomerName = name,
            ContactPhone = phone,
            JoinDate = calendar.Today,
            CollectionNoticeVersion = notice,
            LegalBasis = LegalBasis.Contract,
            CreatedAt = now,
            UpdatedAt = now,
        };
        staging.Add(customer);
        Log(context, Operation.Collection, customer.CustomerId, command.StaffId, command.TerminalId);

        return new CustomerSummary(customer.CustomerId, name, phone);
    }
}

/// <summary>A customer's tab, opened at the till (B7): a consultation of a named person, logged.</summary>
public sealed class OpenTabHandler(ICustomerLedger customers, ITenantConfiguration configuration, IPseudonymiser pseudonymiser, ILedgerCurrency currency)
    : CustomerCommandBase(customers, configuration, pseudonymiser, currency), ICommandHandler<OpenTab, TabView>
{
    public async Task<TabView> HandleAsync(OpenTab command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        var settings = await ModuleOnAsync(cancellationToken);
        var customer = await CustomerAsync(command.CustomerId, cancellationToken);
        var movements = await Customers.MovementsAsync(customer.CustomerId, cancellationToken);

        Log(context, Operation.Consultation, customer.CustomerId, command.StaffId, command.TerminalId);
        return ViewOf(customer, movements, settings, customer.CreditLimit, customer.TabFrozenAt is not null);
    }
}

/// <summary>
/// A limit given, changed or taken away, or the tab frozen or unfrozen (B7, D-096): the customer's
/// row changed and a <c>credit_limit_events</c> row saying who did it. A limit above the tenant's
/// ceiling is refused (<see cref="Tab.MayLimit"/>); one below what is owed is not, it only stops
/// new charges. Freezing a frozen tab, or unfreezing an open one, changes nothing and writes nothing.
/// </summary>
public sealed class ChangeCreditLimitHandler(
    ICustomerLedger customers, ITenantConfiguration configuration, IPseudonymiser pseudonymiser, ILedgerCurrency currency,
    IStaging staging, TimeProvider clock)
    : CustomerCommandBase(customers, configuration, pseudonymiser, currency), ICommandHandler<ChangeCreditLimit, TabView>
{
    private readonly ILedgerCurrency _currency = currency;

    public async Task<TabView> HandleAsync(ChangeCreditLimit command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        var settings = await ModuleOnAsync(cancellationToken);
        var customer = await CustomerAsync(command.CustomerId, cancellationToken);
        var movements = await Customers.MovementsAsync(customer.CustomerId, cancellationToken);
        var now = clock.GetUtcNow();

        var limit = customer.CreditLimit;
        var frozenAt = customer.TabFrozenAt;
        CreditLimitEvent? change = null;
        switch (command.Change)
        {
            case LimitChange.Set:
                limit = command.Limit is { } minor ? Money.FromMinorUnits(minor, _currency.Currency) : null;
                switch (Tab.MayLimit(limit, settings.MaxCreditLimit))
                {
                    case LimitVerdict.Negative:
                        throw new CustomerRefusedException(CustomerRefusal.Invalid, "A limit is zero or more.");
                    case LimitVerdict.AboveCeiling:
                        throw new CustomerRefusedException(CustomerRefusal.Refused, $"At most {settings.MaxCreditLimit}: the shop's ceiling for any tab.");
                }

                if (limit != customer.CreditLimit)
                {
                    change = Event(context, customer, CreditLimitEventType.Set, customer.CreditLimit, limit, command.StaffId, now);
                }

                break;

            case LimitChange.Freeze when frozenAt is null:
                frozenAt = now;
                change = Event(context, customer, CreditLimitEventType.Frozen, null, null, command.StaffId, now);
                break;

            case LimitChange.Unfreeze when frozenAt is not null:
                frozenAt = null;
                change = Event(context, customer, CreditLimitEventType.Unfrozen, null, null, command.StaffId, now);
                break;
        }

        if (change is not null)
        {
            staging.Add(change);
            await Customers.StageTabAsync(customer.CustomerId, limit, frozenAt, now, cancellationToken);
            Log(context, Operation.Modification, customer.CustomerId, command.StaffId, command.TerminalId);
        }
        else
        {
            Log(context, Operation.Consultation, customer.CustomerId, command.StaffId, command.TerminalId);
        }

        return ViewOf(customer, movements, settings, limit, frozenAt is not null);
    }

    private static CreditLimitEvent Event(
        CommandContext context, Customer customer, CreditLimitEventType type, Money? previous, Money? next, string staffId, DateTimeOffset now) => new()
        {
            EventId = context.NewId(),
            CustomerId = customer.CustomerId,
            EventType = type,
            PreviousLimit = previous,
            NewLimit = next,
            StaffId = staffId,
            OccurredAt = now,
        };
}

/// <summary>
/// A repayment in cash (B7, D-055): a <c>paid_in</c> on the terminal's open session, so the drawer
/// still reconciles, and a <c>payment</c> on the tab pointing at it; both or neither. Never more
/// than is owed (<see cref="Tab.MayRepay"/>): the tab is not a place to keep money.
/// </summary>
public sealed class RepayTabHandler(
    ICustomerLedger customers, ITenantConfiguration configuration, IPseudonymiser pseudonymiser, ILedgerCurrency currency,
    IStaging staging, ISalesLedger ledger, IReasonCodes reasons, TimeProvider clock)
    : CustomerCommandBase(customers, configuration, pseudonymiser, currency), ICommandHandler<RepayTab, TabView>
{
    private readonly ILedgerCurrency _currency = currency;

    public async Task<TabView> HandleAsync(RepayTab command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TerminalId);
        var settings = await ModuleOnAsync(cancellationToken);
        var customer = await CustomerAsync(command.CustomerId, cancellationToken);
        var movements = await Customers.MovementsAsync(customer.CustomerId, cancellationToken);
        var amount = Money.FromMinorUnits(command.Amount, _currency.Currency);

        var before = Tab.Age(_currency.Currency, [.. movements.Select(movement => new TabMovement(movement.OccurredAt, movement.Amount))]);
        switch (Tab.MayRepay(before.Balance, amount))
        {
            case RepaymentVerdict.NotAboveZero:
                throw new CustomerRefusedException(CustomerRefusal.Invalid, "A repayment is above zero.");
            case RepaymentVerdict.AboveBalance:
                throw new CustomerRefusedException(CustomerRefusal.Refused, $"More than is owed: {before.Balance}.");
        }

        if ((await reasons.ForAsync(ReasonCodeAppliesTo.CashMovement, cancellationToken)).All(reason => reason.Code != command.ReasonCode))
        {
            throw new CustomerRefusedException(CustomerRefusal.Invalid, "Not a reason this shop gives for money into the drawer.");
        }

        var store = await ledger.CurrentStoreAsync(cancellationToken)
            ?? throw new CustomerRefusedException(CustomerRefusal.Refused, "This store has no row in stores; it is not commissioned.");
        var now = clock.GetUtcNow();
        var session = await CashSessions.OpenAsync(ledger, staging, context, store, command.TerminalId, command.StaffId, now, cancellationToken);

        var paidIn = new CashMovement
        {
            MovementId = context.NewId(),
            SessionId = session,
            MovementType = CashMovementType.PaidIn,
            Amount = amount,
            ReasonCode = command.ReasonCode,
            StaffId = command.StaffId,
            OccurredAt = now,
        };
        var repayment = new ReceivableMovement
        {
            MovementId = context.NewId(),
            StoreId = store.StoreId,
            CustomerId = customer.CustomerId,
            MovementType = ReceivableMovementType.Payment,
            Amount = -amount,
            OccurredAt = now,
            CashMovementId = paidIn.MovementId,
            StaffId = command.StaffId,
        };
        staging.Add(paidIn);
        staging.Add(repayment);
        Log(context, Operation.Collection, customer.CustomerId, command.StaffId, command.TerminalId);

        return ViewOf(customer, [.. movements, repayment], settings, customer.CreditLimit, customer.TabFrozenAt is not null);
    }
}
