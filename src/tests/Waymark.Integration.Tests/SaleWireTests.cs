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

    [Fact]
    public void A_typed_weight_reaches_the_command_as_thousandths_and_a_label_or_a_count_as_none()
    {
        var request = new SaleRequest("till", [
            new SaleRequestLine("4011", 1, "0.556"),
            new SaleRequestLine("2100537001008", 1),
            new SaleRequestLine("6130000000017", 3),
        ]);

        var lines = SaleWire.ToCommand(request, "staff").Lines;

        Assert.Equal([556L, null, null], lines.Select(line => line.WeightThousandths));
    }

    [Fact]
    public void A_weight_that_is_not_one_reaches_the_command_as_zero_so_the_sale_is_refused_not_sold_by_count()
    {
        var request = new SaleRequest("till", [new SaleRequestLine("4011", 1, "0,556")]);

        Assert.Equal(0L, SaleWire.ToCommand(request, "staff").Lines.Single().WeightThousandths);
    }

    // ------------------------------------------------------------------ discounts (B4, D-091)

    [Fact]
    public void A_discount_reaches_the_command_as_given_with_whoever_the_server_says_authorised_it()
    {
        var request = new SaleRequest("till", [
            new SaleRequestLine("6130000000017", 2, Discount: new DiscountRequest(DiscountForms.Percent, "12.5", "geste_commercial", "auth-1")),
            new SaleRequestLine("6130000000024", 1),
        ], new DiscountRequest(DiscountForms.Amount, "50.00", "geste_commercial", "auth-1"));

        var command = SaleWire.ToCommand(request, "nabil", cited => cited == "auth-1" ? "samia" : null);

        Assert.Equal(new GivenDiscount(DiscountForm.Percent, 1_250, "geste_commercial", "samia"), command.Lines[0].Discount);
        Assert.Null(command.Lines[1].Discount);
        Assert.Equal(new GivenDiscount(DiscountForm.Amount, 5_000, "geste_commercial", "samia"), command.TicketDiscount);
    }

    [Fact]
    public void An_authorisation_the_server_does_not_hold_reaches_the_command_with_nobody_so_the_sale_is_refused()
    {
        var request = new SaleRequest("till", [
            new SaleRequestLine("6130000000017", 1, Discount: new DiscountRequest(DiscountForms.Percent, "10", "geste_commercial", "forged"))]);

        Assert.Equal(string.Empty, SaleWire.ToCommand(request, "nabil", _ => null).Lines[0].Discount!.AuthorisedBy);
    }

    [Theory]
    [InlineData("percent", "10,5")]  // a comma: the till sends invariant text
    [InlineData("percent", "10.555")]
    [InlineData("rebate", "10")]
    public void A_discount_that_cannot_be_read_reaches_the_command_as_nothing_off_so_the_sale_is_refused(string form, string value)
    {
        var request = new SaleRequest("till", [new SaleRequestLine("6130000000017", 1, Discount: new DiscountRequest(form, value, "geste_commercial", "auth-1"))]);

        Assert.Equal(0, SaleWire.ToCommand(request, "nabil", _ => "samia").Lines[0].Discount!.Value);
    }
}
