using System.Runtime.Versioning;
using Waymark.Contracts.Pos;
using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.StoreServer.Sales;
using Waymark.StoreServer.Security;

namespace Waymark.Integration.Tests;

/// <summary>
/// Past tickets on the wire (session B1, D-088). The silent failures: a cashier reading another
/// till's or another day's sales, a person with no rank let through, and a store day cut at UTC
/// midnight, which files the evening's sales in Algiers under the next day.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TicketsWireTests
{
    private static readonly DateOnly Today = new(2026, 9, 25);
    private static readonly SignedInTill AtTillOne = new("nabil", "till-1", DateTimeOffset.UnixEpoch);
    private static readonly TimeZoneInfo Algiers = TimeZoneInfo.CreateCustomTimeZone("Africa/Algiers", TimeSpan.FromHours(1), "Algiers", "Algiers");

    // ================================================================ who may see

    [Fact]
    public void Todays_tickets_at_ones_own_till_need_no_rank()
    {
        Assert.True(TicketsWire.MaySee(AtTillOne, rank: 1, Today, Today, "till-1"));
        Assert.True(TicketsWire.MaySee(AtTillOne, rank: null, Today, Today, "till-1"));
    }

    [Theory]
    [InlineData(-1, "till-1")] // yesterday, own till
    [InlineData(0, "till-2")]  // today, another till
    [InlineData(0, null)]      // today, every till
    public void Another_day_or_till_needs_rank_two(int days, string? terminal)
    {
        var day = Today.AddDays(days);

        Assert.False(TicketsWire.MaySee(AtTillOne, rank: 1, Today, day, terminal));
        Assert.False(TicketsWire.MaySee(AtTillOne, rank: null, Today, day, terminal));
        Assert.True(TicketsWire.MaySee(AtTillOne, rank: 2, Today, day, terminal));
        Assert.True(TicketsWire.MaySee(AtTillOne, rank: 5, Today, day, terminal));
    }

    // ================================================================ the store's day

    [Fact]
    public void The_stores_day_runs_midnight_to_midnight_in_its_own_zone()
    {
        var (since, until) = TicketsWire.Bounds(Today, Algiers);

        Assert.Equal(new DateTimeOffset(2026, 9, 24, 23, 0, 0, TimeSpan.Zero), since.ToUniversalTime());
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 23, 0, 0, TimeSpan.Zero), until.ToUniversalTime());
    }

    [Fact]
    public void A_sale_at_half_past_eleven_in_algiers_is_on_the_algiers_day()
    {
        var lateEvening = new DateTimeOffset(2026, 9, 25, 22, 30, 0, TimeSpan.Zero);

        Assert.Equal(Today, TicketsWire.DayOf(lateEvening, Algiers));
        Assert.Equal(Today.AddDays(1), TicketsWire.DayOf(lateEvening.AddHours(1), Algiers));
    }

    // ================================================================ the mapping

    private static Money Dzd(long minorUnits) => Money.FromMinorUnits(minorUnits, Currency.Dzd);

    [Fact]
    public void A_ticket_crosses_with_its_lines_its_figures_and_its_payments_and_no_customer()
    {
        var ticket = new PastTicket(
            "t1", "S-2026-000142", new DateTimeOffset(2026, 9, 25, 13, 5, 0, TimeSpan.Zero), "till-1", "nabil", "Nabil B.",
            TransactionStatus.PartiallyRefunded,
            [new PastTicketLine("v1", "Lait UHT Candia", "Brique 1L", Quantity.FromThousandths(3_000, "pc"), Dzd(14_300), Dzd(2_283), Dzd(42_900))],
            Dzd(42_900), Dzd(2_283), Dzd(42_900),
            [new PastPayment(PaymentMethod.MobileWallet, Dzd(42_900))]);

        var wire = TicketsWire.Found(ticket);

        Assert.Equal(TicketOutcomes.Found, wire.Outcome);
        var detail = wire.Ticket!;
        Assert.Equal(("partially_refunded", "429.00", "DZD"), (detail.Status, detail.Total, detail.Currency));
        Assert.Equal(new PastTicketLineWire("Lait UHT Candia", "Brique 1L", "3", "pc", "143.00", "429.00", "v1", "0", false), Assert.Single(detail.Lines));
        Assert.Equal(new PastPaymentWire("mobile_wallet", "429.00"), Assert.Single(detail.Payments));
        Assert.DoesNotContain(typeof(PastTicketDetail).GetProperties(), property => property.Name.Contains("Customer", StringComparison.Ordinal));
    }

    [Fact]
    public void A_list_says_its_day_and_counts_the_ticket_lines()
    {
        var list = TicketsWire.List(TicketOutcomes.Answered, Today, allTills: false,
            [new PastTicketSummary("t1", "S-1", DateTimeOffset.UnixEpoch, "till-1", TransactionStatus.Completed, 3, Dzd(12_050))]);

        Assert.Equal(("2026-09-25", false), (list.Day, list.AllTills));
        Assert.Equal(new TicketSummary("t1", "S-1", DateTimeOffset.UnixEpoch, "till-1", "completed", 3, "120.50", "DZD"), Assert.Single(list.Tickets));
    }
}
