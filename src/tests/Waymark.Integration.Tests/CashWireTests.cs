using System.Runtime.Versioning;
using Waymark.Domain.Enums;
using Waymark.StoreServer.Sales;

namespace Waymark.Integration.Tests;

/// <summary>
/// A cash movement on the wire (B10, D-102). The silent failure: a direction nobody knows recorded as
/// cash into the drawer, so a request spelled wrongly inflates what the Z-report expects to count.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CashWireTests
{
    [Fact]
    public void In_and_out_are_read_as_the_wire_spells_them()
    {
        Assert.Equal(CashDirection.In, CashWire.Direction("in"));
        Assert.Equal(CashDirection.Out, CashWire.Direction("out"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sideways")]
    [InlineData("IN")]    // the wire's own spelling, or nothing
    [InlineData(" out")]
    public void A_direction_nobody_knows_is_no_direction_never_cash_in(string? direction)
    {
        // Block B review: "sideways" was recorded as a paid-in of 77,00.
        Assert.Null(CashWire.Direction(direction));
    }
}
