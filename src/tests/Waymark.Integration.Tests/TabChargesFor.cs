using Waymark.Application.Customers;
using Waymark.Domain;
using Waymark.Domain.Values;
using Waymark.Persistence;
using Waymark.Persistence.Customers;
using Waymark.Persistence.Organisation;
using Waymark.Pseudonymisation;

namespace Waymark.Integration.Tests;

/// <summary>
/// The customer side of a sale (B7), as StoreServer wires it, for the tests that build a
/// <c>CompleteSaleHandler</c> by hand: the real ledger and configuration over the test's context, and
/// one pseudonymiser under the fixture's test key, shared, since a key is costly to make.
/// </summary>
internal static class TabChargesFor
{
    private static readonly Lazy<TenantPseudonymiser> Keys = new(() => TenantKeyFixture.PseudonymiserFor(TenantKeyFixture.KeyBytes));

    public static TenantPseudonymiser Pseudonymiser => Keys.Value;

    public static TabCharges Context(WaymarkDbContext context) =>
        new(new CustomerLedger(context), new TenantConfigurationStore(context, Dzd), Keys.Value, Dzd);

    public static readonly ILedgerCurrency Dzd = new FixedLedgerCurrency(Currency.Dzd);
}
