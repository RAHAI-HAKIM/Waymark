using System.Runtime.Versioning;
using Waymark.Application.Sales;
using Waymark.Contracts.Pos;
using Waymark.Domain.Values;
using Waymark.StoreServer.Sales;

namespace Waymark.Integration.Tests;

/// <summary>
/// A refund on the wire (B9, D-098). The silent failures: an authorisation the till names that this
/// session never gave, written as somebody's approval; a quantity misread by a factor of a thousand;
/// the figure that comes out of the drawer confused with the exact refund (D-034).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RefundWireTests
{
    private static Money Dzd(long minorUnits) => Money.FromMinorUnits(minorUnits, Currency.Dzd);

    private static RefundRequest Request(string quantity = "2", string price = "143.00", string to = RefundDestinations.Cash, string? authorisation = null) =>
        new("till-1", "t1", [new RefundLineRequest("v1", price, quantity, Restock: true)], "retour-defectueux", to, Authorisation: authorisation);

    [Fact]
    public void A_line_reaches_the_command_in_thousandths_and_centimes_with_the_session_as_seller()
    {
        var command = RefundWire.ToCommand(Request(quantity: "1.240", price: "180.00"), "nabil", true);

        Assert.Equal(new ReturnedLine("v1", 18_000, 1_240, true), Assert.Single(command.Lines));
        Assert.Equal(("nabil", "t1", RefundTo.Cash, true), (command.StaffId, command.Original, command.To, command.SellerMayRefund));
    }

    [Theory]
    [InlineData("2,5")]  // a comma: the till sends invariant text
    [InlineData("-1")]
    [InlineData("1.2345")]
    public void A_quantity_that_cannot_be_read_reaches_the_command_as_nothing_so_it_is_refused(string quantity)
    {
        Assert.Equal(0, Assert.Single(RefundWire.ToCommand(Request(quantity: quantity), "nabil", true).Lines).Quantity);
    }

    [Fact]
    public void An_authorisation_counts_only_when_this_session_gave_it()
    {
        Assert.Null(RefundWire.ToCommand(Request(authorisation: "forged"), "nabil", false, _ => null).AuthorisedBy);
        Assert.Equal("samia", RefundWire.ToCommand(Request(authorisation: "auth-r"), "nabil", false, cited => cited == "auth-r" ? "samia" : null).AuthorisedBy);
    }

    [Fact]
    public void Store_credit_is_asked_by_its_name_and_anything_else_is_cash()
    {
        Assert.Equal(RefundTo.StoreCredit, RefundWire.ToCommand(Request(to: RefundDestinations.StoreCredit), "nabil", true).To);
        Assert.Equal(RefundTo.Cash, RefundWire.ToCommand(Request(to: "card"), "nabil", true).To);
    }

    [Fact]
    public void A_cash_refund_says_the_exact_refund_and_what_comes_out_of_the_drawer()
    {
        var refund = new RefundedSale("r1", "S-2026-000143", "t1", Dzd(12_050), Dzd(0), Dzd(12_050), RefundTo.Cash, Dzd(-12_050).ToCashTender(), null, false);

        var wire = RefundWire.Answered(refund);

        Assert.Equal((RefundOutcomes.Refunded, "S-2026-000143", "120.50", "120.00"), (wire.Outcome, wire.InvoiceNumber, wire.Total, wire.CashOut));
        Assert.Equal((RefundDestinations.Cash, (string?)null, "DZD"), (wire.RefundTo, wire.CreditBalance, wire.Currency));
    }

    [Fact]
    public void A_refund_to_the_tab_and_store_credit_says_both_shares_and_the_balance()
    {
        var refund = new RefundedSale("r1", "S-1", "t1", Dzd(50_000), Dzd(30_000), Dzd(20_000), RefundTo.StoreCredit, Dzd(0).ToCashTender(), Dzd(45_000), true);

        var wire = RefundWire.Answered(refund);

        Assert.Equal(("500.00", "300.00", "200.00"), (wire.Total, wire.ToTab, wire.Rest));
        Assert.Equal((RefundDestinations.StoreCredit, (string?)null, "450.00", true), (wire.RefundTo, wire.CashOut, wire.CreditBalance, wire.MayCredit));
    }

    [Fact]
    public void A_quote_has_no_ticket_and_says_so()
    {
        var quote = new RefundedSale(string.Empty, string.Empty, "t1", Dzd(14_300), Dzd(0), Dzd(14_300), RefundTo.Cash, Dzd(-14_300).ToCashTender(), null, true);

        var wire = RefundWire.Answered(quote);

        Assert.Equal((RefundOutcomes.Quoted, (string?)null, (string?)null, true), (wire.Outcome, wire.TransactionId, wire.InvoiceNumber, wire.MayCredit));
    }
}
