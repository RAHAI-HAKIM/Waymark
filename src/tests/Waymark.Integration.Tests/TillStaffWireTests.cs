using System.Runtime.Versioning;
using Waymark.Contracts.Pos;
using Waymark.Domain.Organisation;
using Waymark.StoreServer.Security;

namespace Waymark.Integration.Tests;

/// <summary>
/// Who the manager step lists (block B review, D-109). The silent failure: every cashier listed as
/// someone who could approve, so a cashier picks a colleague, types a right PIN, and is refused.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TillStaffWireTests
{
    private static TenantSettings Settings(long? refundMinRank = null, long? paidOutMinRank = null) =>
        new(CustomerModule: false, MaxCreditLimit: null, CreditOverdueDays: null, TabAsPart: true, RefundMinRank: refundMinRank, PaidOutMinRank: paidOutMinRank);

    [Theory]
    [InlineData(Capabilities.ApplyDiscount, Capability.ApplyDiscount)]
    [InlineData(Capabilities.VoidTransaction, Capability.VoidTransaction)]
    [InlineData(Capabilities.ViewOtherTickets, Capability.ViewOtherTickets)]
    [InlineData(Capabilities.PaidOut, Capability.PaidOut)]
    public void A_capability_is_read_as_the_wire_names_it(string wire, Capability capability) =>
        Assert.Equal(capability, TillStaffWire.Capability(wire));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("everything")]
    public void A_capability_nobody_knows_is_none(string? wire) => Assert.Null(TillStaffWire.Capability(wire));

    [Theory]
    [InlineData(1L, false)]  // a cashier cannot approve a discount, and is not listed
    [InlineData(2L, true)]
    [InlineData(3L, true)]
    [InlineData(null, false)] // no rank is never permission (D-077)
    public void Only_those_whose_pin_would_be_accepted_may_approve(long? rank, bool may) =>
        Assert.Equal(may, TillStaffWire.MayApprove(rank, Capability.ApplyDiscount, Settings()));

    [Fact]
    public void A_refund_is_approved_by_the_rank_the_shop_raised_it_to()
    {
        // Unset, anyone with a rank refunds; raised to 2, a cashier is not listed.
        Assert.True(TillStaffWire.MayApprove(1, Capability.Refund, Settings()));
        Assert.False(TillStaffWire.MayApprove(1, Capability.Refund, Settings(refundMinRank: 2)));
        Assert.True(TillStaffWire.MayApprove(2, Capability.Refund, Settings(refundMinRank: 2)));
        Assert.False(TillStaffWire.MayApprove(1, Capability.PaidOut, Settings(paidOutMinRank: 2)));
    }
}
