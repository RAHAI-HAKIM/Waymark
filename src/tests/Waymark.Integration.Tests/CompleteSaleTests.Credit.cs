using Microsoft.EntityFrameworkCore;
using Waymark.Application.Organisation;
using Waymark.Application.Sales;
using Waymark.Domain.Enums;
using Waymark.Domain.Ledgers;
using Waymark.Domain.Organisation;
using Waymark.Domain.Values;
using Waymark.Persistence.Organisation;

namespace Waymark.Integration.Tests;

/// <summary>
/// B9b (D-101): store credit spent at the till, against a real database. The silent failures: credit
/// spent past what the customer has; expired credit spent, or written off without a trace;
/// <c>customers.credit</c> left behind the ledger; a refund that turns store credit into cash.
/// </summary>
/// <remarks>
/// A spend goes through <see cref="Domain.Customers.StoreCredit"/> and the store credit part of
/// <see cref="Domain.Sales.Tender"/>, Hakim's piece: those tests are red until it is written.
/// </remarks>
public sealed partial class CompleteSaleTests
{
    /// <summary>Store credit issued to the customer that many days before the test's moment.</summary>
    private void Credit(string customerId, long centimes, int daysAgo)
    {
        using var context = database.NewContext();
        context.CreditMovements.Add(new CreditMovement
        {
            MovementId = $"credit-{Guid.NewGuid():N}",
            CustomerId = customerId,
            MovementType = CreditMovementType.Issue,
            Amount = Dzd(centimes),
            BalanceAfter = Dzd(centimes),
            OccurredAt = Now.AddDays(-daysAgo),
        });
        context.SaveChanges();
    }

    private Task<TenantSettings> Expiry(Shop shop, int? days) =>
        Run(shop, (context, _) => new SetTenantSettingsHandler(new TenantConfigurationStore(context, TabChargesFor.Dzd), TabChargesFor.Dzd, new FixedClock()),
            new SetTenantSettings(CreditExpiryDays: days, CreditExpiryOff: days is null));

    private Task<CompletedSale> SpendCredit(Shop shop, string customerId, long credit, int milk = 2) =>
        Execute(shop, new CompleteSale(
            shop.TerminalId, shop.StaffId, [new SaleLineRequest(shop.Milk.Barcode, milk)],
            Tenders: [new GivenTender(PaymentMethod.StoreCredit, credit, null)], CustomerId: customerId));

    // ------------------------------------------------------------------ Hakim: credit spent

    [Fact]
    public async Task Store_credit_is_spent_as_a_part_and_the_ledger_and_the_customer_follow()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        await ConfigureAsync(shop);
        var customer = await Create(shop, "Samira", NewPhone());
        Credit(customer.CustomerId, 20_000, daysAgo: 3);

        var sale = await SpendCredit(shop, customer.CustomerId, 10_000);

        using var read = Read(shop);
        var payments = await read.TransactionPayments.Where(p => p.TransactionId == sale.TransactionId).OrderBy(p => p.Sequence).ToListAsync();
        Assert.Equal([(PaymentMethod.StoreCredit, Dzd(10_000)), (PaymentMethod.Cash, Dzd(18_600))], payments.Select(p => (p.PaymentMethod, p.Amount)));
        var redeem = await read.CreditMovements.SingleAsync(m => m.CustomerId == customer.CustomerId && m.MovementType == CreditMovementType.Redeem);
        Assert.Equal((Dzd(-10_000), Dzd(10_000), sale.TransactionId), (redeem.Amount, redeem.BalanceAfter, redeem.TransactionId));
        Assert.Equal(Dzd(10_000), (await read.Customers.SingleAsync(c => c.CustomerId == customer.CustomerId)).Credit);
    }

    [Fact]
    public async Task More_than_the_credit_is_refused_and_nothing_is_written()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        await ConfigureAsync(shop);
        var customer = await Create(shop, "Yacine", NewPhone());
        Credit(customer.CustomerId, 5_000, daysAgo: 3);

        await Assert.ThrowsAsync<SaleRefusedException>(() => SpendCredit(shop, customer.CustomerId, 5_001));

        using var read = Read(shop);
        Assert.False(await read.Transactions.AnyAsync());
        Assert.Single(await read.CreditMovements.Where(m => m.CustomerId == customer.CustomerId).ToListAsync());
    }

    [Fact]
    public async Task Expired_credit_is_written_off_before_the_spend_and_cannot_be_spent()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        await ConfigureAsync(shop);
        var customer = await Create(shop, "Karim", NewPhone());
        Credit(customer.CustomerId, 50_000, daysAgo: 40); // expired under 30 days
        Credit(customer.CustomerId, 20_000, daysAgo: 5);
        await Expiry(shop, 30);
        try
        {
            await Assert.ThrowsAsync<SaleRefusedException>(() => SpendCredit(shop, customer.CustomerId, 20_001));
            var sale = await SpendCredit(shop, customer.CustomerId, 10_000);

            using var read = Read(shop);
            var movements = await read.CreditMovements.Where(m => m.CustomerId == customer.CustomerId && m.MovementType != CreditMovementType.Issue).ToListAsync();
            var expire = movements.Single(m => m.MovementType == CreditMovementType.Expire);
            var redeem = movements.Single(m => m.MovementType == CreditMovementType.Redeem);
            Assert.Equal((Dzd(-50_000), Dzd(20_000)), (expire.Amount, expire.BalanceAfter));
            Assert.Equal((Dzd(-10_000), Dzd(10_000), sale.TransactionId), (redeem.Amount, redeem.BalanceAfter, redeem.TransactionId));
            Assert.Equal(Dzd(10_000), (await read.Customers.SingleAsync(c => c.CustomerId == customer.CustomerId)).Credit);
        }
        finally
        {
            await Expiry(shop, null);
        }
    }

    [Fact]
    public async Task A_refund_gives_store_credit_back_as_store_credit_never_as_cash()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        await ConfigureAsync(shop);
        var customer = await Create(shop, "Nadia", NewPhone());
        Credit(customer.CustomerId, 10_000, daysAgo: 3);
        var sale = await SpendCredit(shop, customer.CustomerId, 10_000); // 286,00: 100,00 credit, 186,00 cash

        var refund = await Refund(shop, sale.TransactionId, [Back(shop.Milk, 2, 14_300)], RefundTo.Cash);

        Assert.Equal((Dzd(10_000), Dzd(18_600)), (refund.ToCredit!.Value, refund.Rest));
        using var read = Read(shop);
        var payments = await read.TransactionPayments.Where(p => p.TransactionId == refund.TransactionId).OrderBy(p => p.Sequence).ToListAsync();
        Assert.Equal([(PaymentMethod.StoreCredit, Dzd(-10_000)), (PaymentMethod.Cash, Dzd(-18_600))], payments.Select(p => (p.PaymentMethod, p.Amount)));
        Assert.Equal(Dzd(10_000), (await read.Customers.SingleAsync(c => c.CustomerId == customer.CustomerId)).Credit);
    }

    // ------------------------------------------------------------------ refused before the rule is asked

    [Fact]
    public async Task Store_credit_with_no_customer_is_refused()
    {
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 5);
        await ConfigureAsync(shop);

        await Assert.ThrowsAsync<SaleRefusedException>(() => Execute(shop, new CompleteSale(
            shop.TerminalId, shop.StaffId, [new SaleLineRequest(shop.Milk.Barcode, 1)], Tenders: [new GivenTender(PaymentMethod.StoreCredit, 1_000, null)])));

        using var read = Read(shop);
        Assert.False(await read.Transactions.AnyAsync());
    }
}
