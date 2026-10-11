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
    [InlineData("adult", Waymark.Application.Customers.AgeAttestation.Adult)]
    [InlineData("guardian_agreed", Waymark.Application.Customers.AgeAttestation.GuardianAgreed)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("ADULT", null)] // anything the server does not know is no attestation, never "adult"
    [InlineData("yes", null)]
    public void An_age_attestation_is_one_of_the_two_or_none(string? wire, Waymark.Application.Customers.AgeAttestation? read)
    {
        Assert.Equal(read, CustomerEndpoints.Age(wire));
    }

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
