using System.Runtime.Versioning;
using Waymark.Application.Sales;
using Waymark.Contracts.Pos;
using Waymark.Domain.Values;
using Waymark.StoreServer.Sales;
using Waymark.StoreServer.Security;

namespace Waymark.Integration.Tests;

/// <summary>
/// The sale's answer on the wire (D-070). The figure that must not be confused: what the
/// customer hands over is the total rounded to the cash step, not the exact total (D-034).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SaleWireTests
{
    [Fact]
    public void A_completed_sale_says_the_exact_total_and_the_cash_to_collect()
    {
        var total = Money.FromMinorUnits(40_650, Currency.Dzd);
        var sale = new CompletedSale("t1", "S-2026-000001", total, Money.FromMinorUnits(4_283, Currency.Dzd), total.ToCashTender());

        var wire = SaleWire.Completed(sale);

        Assert.Equal(SaleOutcomes.Completed, wire.Outcome);
        Assert.Equal("S-2026-000001", wire.InvoiceNumber);
        Assert.Equal("406.50", wire.TotalTtc);
        Assert.Equal("42.83", wire.TaxTotal);
        Assert.Equal(WireText(total.ToCashTender().Tendered), wire.CashToCollect);
        Assert.NotEqual(wire.TotalTtc, wire.CashToCollect);
        Assert.Equal("DZD", wire.Currency);
        Assert.Null(wire.Reason);
    }

    [Fact]
    public void A_refusal_carries_its_reason_and_no_figures()
    {
        var wire = SaleWire.Refused("111: no product carries this code.");

        Assert.Equal(SaleOutcomes.Refused, wire.Outcome);
        Assert.Equal("111: no product carries this code.", wire.Reason);
        Assert.Null(wire.TotalTtc);
        Assert.Null(wire.InvoiceNumber);
    }

    [Fact]
    public void The_request_becomes_the_command_line_for_line()
    {
        var command = SaleWire.ToCommand(new SaleRequest("till-1", [new("111", 2), new("222", 1)]), "staff-1");

        Assert.Equal("till-1", command.TerminalId);
        Assert.Equal("staff-1", command.StaffId);
        Assert.Equal([new SaleLineRequest("111", 2), new SaleLineRequest("222", 1)], command.Lines);
    }

    // ------------------------------------------------ who is selling (A5, D-083)

    private static readonly SaleRequest AtTillOne = new("till-1", [new("111", 1)]);

    [Fact]
    public void The_seller_is_the_person_the_session_signed_in()
    {
        Assert.Equal("staff-7", SaleWire.Seller(new SignedInTill("staff-7", "till-1", DateTimeOffset.UnixEpoch), AtTillOne));
    }

    [Fact]
    public void No_session_sells_nothing()
    {
        Assert.Null(SaleWire.Seller(null, AtTillOne));
    }

    [Fact]
    public void A_session_opened_at_another_till_sells_nothing()
    {
        // A token copied from one till must not sell at the next: the sale would carry a person
        // who was never at that till.
        Assert.Null(SaleWire.Seller(new SignedInTill("staff-7", "till-2", DateTimeOffset.UnixEpoch), AtTillOne));
    }

    [Fact]
    public void Not_signed_in_is_its_own_answer_with_no_figures()
    {
        var wire = SaleWire.NotSignedIn();

        Assert.Equal(SaleOutcomes.NotSignedIn, wire.Outcome);
        Assert.Null(wire.InvoiceNumber);
        Assert.Null(wire.TotalTtc);
        Assert.False(string.IsNullOrWhiteSpace(wire.Reason));
    }

    private static string WireText(Money money) => StoreServer.WireText.Figure(money);
}
