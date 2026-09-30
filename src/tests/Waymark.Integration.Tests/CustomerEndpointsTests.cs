using System.Runtime.Versioning;
using Waymark.Domain.Organisation;
using Waymark.StoreServer.Customers;

namespace Waymark.Integration.Tests;

/// <summary>
/// Who acts on a customer (B7, D-096): the seller when their rank allows it, else the person whose
/// authorisation the till cites. The silent failure: a cashier creating customers or raising a tab's
/// limit, every screen looking right, and the owner finding out from the debts.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CustomerEndpointsTests
{
    [Theory]
    [InlineData(Capability.CreateCustomer, 2L)]
    [InlineData(Capability.ManageCredit, 3L)]
    public void A_seller_whose_rank_reaches_the_capability_acts_as_themself(Capability capability, long rank)
    {
        Assert.Equal("seller", CustomerEndpoints.Actor("seller", rank, capability, () => "someone-else"));
    }

    [Theory]
    [InlineData(Capability.CreateCustomer, 1L)]
    [InlineData(Capability.ManageCredit, 2L)]
    [InlineData(Capability.ManageCredit, null)] // no rank is never permission (D-037)
    public void Below_it_the_authorisation_decides_and_without_one_nobody_acts(Capability capability, long? rank)
    {
        Assert.Equal("owner", CustomerEndpoints.Actor("cashier", rank, capability, () => "owner"));
        Assert.Null(CustomerEndpoints.Actor("cashier", rank, capability, () => null));
    }
}
