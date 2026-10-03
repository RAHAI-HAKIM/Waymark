using Waymark.Application.Commands;
using Waymark.Application.Sales;
using Waymark.Domain;
using Waymark.Domain.Customers;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Privacy;
using Waymark.Domain.Values;

namespace Waymark.Application.Customers;

/// <summary>What a sale may write about its customer, once <see cref="TabCharges.PrepareAsync"/> has said yes.</summary>
/// <param name="CustomerId">The customer the sale is recorded against; null for a sale with none.</param>
/// <param name="OverrideAuthorisedBy">Who let the tab part past the limit; null when it fitted.</param>
public sealed record TabSale(string? CustomerId, string? OverrideAuthorisedBy)
{
    public static TabSale None { get; } = new(null, null);
}

/// <summary>What a sale spending store credit writes (B9b): whose, the balance before, and what has expired and is written off first.</summary>
public sealed record CreditSpend(string CustomerId, Money Balance, Money Expired);

/// <summary>
/// A sale's customer and its tab part (B7, D-096), for <see cref="CompleteSaleHandler"/>: the module
/// on, the customer there, and the charge asked of <see cref="Tab.Check"/> before anything is
/// written. A charge past the limit goes on only with an owner's authorisation, and says whose; no
/// override lets a frozen or overdue tab, or a customer with no tab, take a charge.
/// </summary>
public sealed class TabCharges(ICustomerLedger customers, ITenantConfiguration configuration, IPseudonymiser pseudonymiser, ILedgerCurrency ledgerCurrency)
{
    private const string SourceModule = "Waymark.Application.Sales";

    public Task<TenantSettings> SettingsAsync(CancellationToken cancellationToken = default) => configuration.CurrentAsync(cancellationToken);

    /// <param name="tabPart">The tab part of the settlement; null when the sale takes nothing on the tab.</param>
    /// <param name="overrideBy">The person of <c>ManageCredit</c> the host resolved from the till's authorisation; null when none was cited.</param>
    /// <exception cref="SaleRefusedException">Nothing is written, and no invoice number spent.</exception>
    public async Task<TabSale> PrepareAsync(
        TenantSettings settings, string? customerId, Money? tabPart, string? overrideBy, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (customerId is null)
        {
            return tabPart is null ? TabSale.None : throw new SaleRefusedException("A ticket on the tab needs its customer.");
        }

        if (!settings.CustomerModule)
        {
            throw new SaleRefusedException("This shop keeps no customers: the customer module is off.");
        }

        var customer = await customers.FindAsync(customerId, cancellationToken)
            ?? throw new SaleRefusedException("No such customer.");
        if (tabPart is not { } charge)
        {
            return new TabSale(customer.CustomerId, null);
        }

        var movements = await customers.MovementsAsync(customer.CustomerId, cancellationToken);
        var age = Tab.Age(ledgerCurrency.Currency, [.. movements.Select(movement => new TabMovement(movement.OccurredAt, movement.Amount))]);
        var check = Tab.Check(age, customer.CreditLimit, customer.TabFrozenAt is not null, charge, now, settings.CreditOverdueDays);

        return check.Verdict switch
        {
            TabVerdict.Accepted => new TabSale(customer.CustomerId, null),
            TabVerdict.AboveLimit when overrideBy is not null => new TabSale(customer.CustomerId, overrideBy),
            TabVerdict.AboveLimit => throw new SaleRefusedException(
                $"{customer.CustomerName}: past the tab's limit, {check.Available} left. The owner may let it through."),
            TabVerdict.NoTab => throw new SaleRefusedException($"{customer.CustomerName} has no tab."),
            TabVerdict.Frozen => throw new SaleRefusedException($"{customer.CustomerName}'s tab is frozen."),
            _ => throw new SaleRefusedException($"{customer.CustomerName}'s oldest charge is overdue: something is repaid first."),
        };
    }

    /// <summary>
    /// The store credit part of a sale (B9b, D-101): the module on, the customer there, the credit read
    /// by <see cref="StoreCredit.Age"/> with the tenant's expiry, and the amount asked of
    /// <see cref="StoreCredit.MayRedeem"/> before anything is written. Null when the sale spends none.
    /// </summary>
    /// <exception cref="SaleRefusedException">Nothing is written, and no invoice number spent.</exception>
    public async Task<CreditSpend?> PrepareCreditAsync(
        TenantSettings settings, string? customerId, Money? credit, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (credit is not { } spent)
        {
            return null;
        }

        if (customerId is null)
        {
            throw new SaleRefusedException("Store credit is a named customer's: attach them first.");
        }

        if (!settings.CustomerModule)
        {
            throw new SaleRefusedException("This shop keeps no customers: the customer module is off.");
        }

        var customer = await customers.FindAsync(customerId, cancellationToken)
            ?? throw new SaleRefusedException("No such customer.");
        var movements = await customers.CreditMovementsAsync(customer.CustomerId, cancellationToken);
        var age = StoreCredit.Age(
            ledgerCurrency.Currency, [.. movements.Select(movement => new CreditLine(movement.OccurredAt, movement.Amount))], now, settings.CreditExpiryDays);

        return StoreCredit.MayRedeem(age.Available, spent) switch
        {
            RedeemVerdict.Accepted => new CreditSpend(customer.CustomerId, age.Balance, age.Expired),
            RedeemVerdict.NotAboveZero => throw new SaleRefusedException("A store credit part is above zero."),
            _ => throw new SaleRefusedException($"{customer.CustomerName} has {age.Available} of store credit: the part cannot be more."),
        };
    }

    /// <summary>Stages <c>customers.credit</c> at the ledger's new balance, in the sale's own unit of work (D-098).</summary>
    public Task KeepCreditAsync(string customerId, Money balance, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        customers.StageCreditAsync(customerId, balance, at, cancellationToken);

    /// <summary>A sale recorded against a named customer: collection of their purchase, and of a charge when there is one (D-045).</summary>
    public void Log(CommandContext context, string customerId, string staffId, string terminalId, bool onTab)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Record(new ProcessingEvent(
            Operation.Collection,
            ProcessingLogEntrySubjectType.Customer,
            pseudonymiser.PseudonymFor(SubjectDomain.Customer, customerId),
            ActorType.Staff,
            staffId,
            onTab ? ProcessingPurpose.CreditManagement : ProcessingPurpose.PosSale,
            ProcessingLegalBasis.Contract,
            SourceModule,
            TerminalId: terminalId));
    }
}
